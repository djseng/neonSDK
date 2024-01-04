//-----------------------------------------------------------------------------
// FILE:        XenClient.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.SSH;

using Renci.SshNet;

using ThinCLI;

//-----------------------------------------------------------------------------
// IMPLEMENTATION NOTE:
//
// This class originally used [LinuxSshProxy] to SSH into XenServer host machines
// and use the native [xe] client there to manage the hosts and VMs.  This worked
// relatively well, but there were some issues:
//
//      * [LinuxSshProxy] modifies some host Linux settings including disabling
//        SUDO and writing some config proxy related config files.  This will 
//        probably cause some eyebrow raising amongst serious security folks.
//
//      * There are some operations we can't perform, like importing a VM template
//        that needs to be downloaded in pieces and reassembled (to stay below
//        GitHub Releases 2GB artifact file limit).  We also can't export an
//        template XVA file to the controlling computer because there's not 
//        enough disk space on the XenServer host file system (I believe it's
//        limited to 4GB total).  If there was enough space, we could extract
//        the template to the local XenServer filesystem and then used SFTP to
//        download the file to the control computer.  But this won't work.
//
// I discovered that XenXenter and XCP-ng Center both include a small Windows
// version of [xe.exe] that work's great.  This will be a simple drop-in for
// the code below.  All we'll need to do is drop [LinuxSshProxy] and then
// embed and call the [xe.exe] directly, passing the host name and user credentials.
//
// This is a temporary fix though because it won't work on OS/X or (eventually)
// Linux when we port neonDESKTOP to those platforms.  We'll need to see if we
// can build or obtain [xe] for these platforms or convert the code below to
// use the XenServer SDK (C# bindings).  This is being tracked here:
//
//      https://github.com/nforgeio/neonKUBE/issues/1130
//      https://github.com/nforgeio/neonKUBE/issues/1132

// NOTE: XE-CLI commands are documented here:
//
//      https://xcp-ng.org/docs/cli_reference.html#xe-command-reference

namespace Neon.XenServer
{
    /// <summary>
    /// This class provides a simple light-weight XenServer or XCP-ng 
    /// API that connects to the XenServer host operating system via 
    /// SSH and executes commands using the <b>xe</b> XenServer client
    /// tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ideally, we'd use the XenServer .NET API but at this time (Jan 2018),
    /// the API is not compatible with .NET Core which cluster <b>neon-cli</b>
    /// requires because it needs to run on Windows, OSX, and perhaps some day
    /// within the Ubuntu based tool container.
    /// </para>
    /// <para>
    /// The workaround is to simnply connect to the XenServer host via SSH
    /// and perform commands using the <b>xe</b> command line tool installed
    /// with XenServer.  We're going to take advantage of the SSH.NET package
    /// to handle the SSH connection and command execution.
    /// </para>
    /// <para>
    /// XenServer template operations are implemented by the <see cref="Template"/>
    /// property, storage repository operations by <see cref="Storage"/> and
    /// virtual machine operations by <see cref="Machine"/>.
    /// </para>
    /// </remarks>
    /// <threadsafety instance="false"/>
    public sealed partial class XenClient : IDisposable, IXenClient
    {
        //-------------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Identifies the local storage repository.
        /// </summary>
        public const string LocalStorageName = "Local storage";

        /// <summary>
        /// Parses <b>xe</b> client properties formatted like <b>name1:value1; name2: value2;...</b>
        /// into a dictionary, making it easy to retrieve specific values.
        /// </summary>
        /// <param name="property">The property string.</param>
        /// <returns>The case-insensitive dictionary.</returns>
        public static Dictionary<string, string> ParseValues(string property)
        {
            var values = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var item in property.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var colonPos = item.IndexOf(':');

                if (colonPos == -1)
                {
                    continue;
                }

                var key   = item.Substring(0, colonPos).Trim();
                var value = item.Substring(colonPos + 1).Trim();

                values[key] = value;
            }

            return values;
        }

        //-------------------------------------------------------------------------
        // Instance members

        private bool            isDisposed = false;
        private SftpClient      sftpClient = null;
        private string          username;
        private string          password;
        private TextWriter      logWriter;

        // Implementation Note:
        // --------------------
        // The following PDF documents are handy resources for learning about the
        // XE command line tool.
        //
        //      https://docs.citrix.com/content/dam/docs/en-us/xenserver/current-release/downloads/xenserver-vm-users-guide.pdf
        //      https://docs.citrix.com/content/dam/docs/en-us/xenserver/xenserver-7-0/downloads/xenserver-7-0-management-api-guide.pdf

        /// <summary>
        /// Constructor.  Note that you should dispose the instance when you're finished with it.
        /// </summary>
        /// <param name="addressOrFQDN">The target XenServer IP address or FQDN.</param>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password.</param>
        /// <param name="name">Optionally specifies the XenServer name.</param>
        /// <param name="logFolder">
        /// The folder where log files are to be written, otherwise <c>null</c> or 
        /// empty to disable logging.
        /// </param>
        public XenClient(string addressOrFQDN, string username, string password, string name = null, string logFolder = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(username), nameof(username));
            Covenant.Requires<ArgumentNullException>(password != null, nameof(password));

            if (!NetHelper.TryParseIPv4Address(addressOrFQDN, out var address))
            {
                try
                {
                    var hostEntry = Dns.GetHostEntry(addressOrFQDN);

                    if (hostEntry.AddressList.Length == 0)
                    {
                        throw new XenException($"[{addressOrFQDN}] is not a valid IP address or fully qualified domain name of a XenServer host.");
                    }

                    address = hostEntry.AddressList.First();
                }
                catch
                {
                    throw new XenException($"[{addressOrFQDN}] DNS lookup failed.");
                }
            }
            
            this.logWriter = (TextWriter)null;

            if (!string.IsNullOrEmpty(logFolder))
            {
                Directory.CreateDirectory(logFolder);

                this.logWriter = new StreamWriter(new FileStream(Path.Combine(logFolder, $"XENSERVER-{addressOrFQDN}.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite));
            }

            this.Address  = addressOrFQDN;
            this.username = username;
            this.password = password;
            this.Name     = name ?? $"XENSERVER-{addressOrFQDN}";

            // Connect via SFTP.

            this.sftpClient = new SftpClient(addressOrFQDN, username, password);
            this.sftpClient.Connect();

            // Initialize the operation classes.

            this.Storage  = new StorageOperations(this);
            this.Template = new TemplateOperations(this);
            this.Machine  = new MachineOperations(this);
        }

        /// <summary>
        /// Releases any resources associated with the instance.
        /// </summary>
        public void Dispose()
        {
            if (sftpClient != null)
            {
                sftpClient.Dispose();
                sftpClient = null;
            }

            if (logWriter != null)
            {
                logWriter.Close();
                logWriter = null;
            }

            isDisposed = true;
        }

        /// <summary>
        /// Returns the XenServer name as passed to the constructor.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Returns the address or FQDN of the remote XenServer.
        /// </summary>
        public string Address { get; private set; }

        /// <summary>
        /// Implements the XenServer storage repository operations.
        /// </summary>
        public StorageOperations Storage { get; private set; }

        /// <summary>
        /// Implements the XenServer virtual machine template operations.
        /// </summary>
        public TemplateOperations Template { get; private set; }

        /// <summary>
        /// Implements the XenServer virtual machine operations.
        /// </summary>
        public MachineOperations Machine { get; private set; }

        /// <summary>
        /// Returns the client's log writer or <c>null</c>.
        /// </summary>
        public TextWriter LogWriter => this.logWriter;

        /// <summary>
        /// Verifies that that the instance hasn't been disposed.
        /// </summary>
        private void EnsureNotDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(XenClient));
            }
        }

        /// <summary>
        /// Adds the host and credential arguments to the command and arguments passed.
        /// </summary>
        /// <param name="command">The XE command.</param>
        /// <param name="args">The command arguments.</param>
        /// <returns>
        /// The complete set of arguments to the <b>xe</b> command including the host,
        /// credentials, command and command arguments.
        /// </returns>
        private string[] NormalizeArgs(string command, string[] args)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(command), nameof(command));

            var allArgs = new List<string>();

            allArgs.Add("-s"); allArgs.Add(Address);
            allArgs.Add("-u"); allArgs.Add(username);
            allArgs.Add("-pw"); allArgs.Add(password);
            allArgs.Add(command);

            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (!string.IsNullOrEmpty(arg))
                    {
                        allArgs.Add(arg);
                    }
                }
            }

            return allArgs.ToArray();
        }

        /// <summary>
        /// Logs an XE command execution.
        /// </summary>
        /// <param name="command">The <b>XE-CLI</b> command.</param>
        /// <param name="args">The command arguments.</param>
        /// <param name="response">The command response.</param>
        /// <returns>The <paramref name="response"/>.</returns>
        private ExecuteResponse LogXeCommand(string command, string[] args, ExecuteResponse response)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(command), nameof(command));
            Covenant.Requires<ArgumentNullException>(response != null, nameof(response));

            if (logWriter == null)
            {
                return response;
            }

            args = args ?? new string[0];

            logWriter.WriteLine($"START: xe -h {Address} -u {username} -p [REDACTED] {command} {NeonHelper.NormalizeExecArgs(args)}");

            if (response.ExitCode != 0)
            {
                logWriter.WriteLine("STDOUT");

                using (var reader = new StringReader(response.OutputText))
                {
                    foreach (var line in reader.Lines())
                    {
                        logWriter.WriteLine("    " + line);
                    }
                }

                if (!string.IsNullOrEmpty(response.ErrorText))
                {
                    logWriter.WriteLine("STDERR");

                    using (var reader = new StringReader(response.ErrorText))
                    {
                        foreach (var line in reader.Lines())
                        {
                            logWriter.WriteLine("    " + line);
                        }
                    }
                }
            }

            if (response.ExitCode == 0)
            {
                logWriter.WriteLine("END [OK]");
            }
            else
            {
                logWriter.WriteLine($"END [ERROR={response.ExitCode}]");
            }

            logWriter.Flush();

            return response;
        }

        /// <summary>
        /// Invokes a low-level <b>XE-CLI</b> command on the remote XenServer host
        /// that returns text.
        /// </summary>
        /// <param name="command">The <b>XE-CLI</b> command.</param>
        /// <param name="args">The optional arguments formatted as <b>name=value</b>.</param>
        /// <returns>The command response.</returns>
        public ExecuteResponse Invoke(string command, params string[] args)
        {
            EnsureNotDisposed();

            if (command == "vm-reset-powerstate")
            {
                // $hack(jefflill):
                //
                // [vm-reset-powerstate] commands can fail sometimes with errors like:
                //
                //      The operation could not be performed because a domain still exists for the specified VM.
                //
                // It appears that we need to remove the VM's "domain" in case the VM is "stuck".
                // I'm not entirely sure what these domains are, but apparently it's an internal
                // XenServer entity that that manages VM resources while the VM is actually running.
                // We're going to proactively remove any associated domain before resetting the 
                // VM powerstate.
                //
                // We need identify the domain associated with the VM if there is one and 
                // remove it.  We need to SSH into the host to do this because domains aren't
                // managed by the XE command line tool.
                //
                // We'll execute [list_domains] to list all of the existing domains.  This will
                // print output something like:
                //
                //      id |                                 uuid |  state
                //      0  | 5aadf5d8-85e0-4aaf-9ce8-85fd500a2dbb |     R
                //      3  | 79ebe13d-5d6f-1518-c320-45b3103fb866 |     RH
                //
                // Where the [uuid] for each row identify the associated VM and the [id]
                // specifies the domain ID.  We'll look for a line with the VM's UUID and
                // extract the associated domain ID when the VM is listed and then remove
                // the domain via:
                //
                //      /usr/sbin/xl destroy <domid>

                // Extract the VM UUID from the command arguments.

                var vmUuid = args
                    .Where(arg => arg.StartsWith("uuid="))
                    .Select(arg => arg.Substring("uuid=".Length))
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(vmUuid))
                {
                    // Connect to the XenServer host and handle the domain removal.

                    using (var hostProxy = Connect())
                    {
                        var response = hostProxy.SudoCommand("/bin/list_domains").EnsureSuccess();
                        var domainId = -1;

                        using (var reader = new StringReader(response.OutputText))
                        {
                            foreach (var line in reader.Lines())
                            {
                                if (line.Contains(vmUuid))
                                {
                                    var pipePos = line.IndexOf('|');

                                    Covenant.Assert(pipePos > 1);

                                    domainId = int.Parse(line.Substring(0, pipePos).Trim());
                                    break;
                                }
                            }
                        }

                        if (domainId > 0)
                        {
                            hostProxy.SudoCommand("/usr/sbin/xl", "destroy", domainId).EnsureSuccess();
                        }
                    }
                }
            }

            // $note(jefflill):
            //
            // In the olden days, we used to include the [xe.exe] in the library client as content
            // and then use that to execute commands against XenServer host machines.  This was 
            // problematic because Citrix only released Windows binaries (although we could have
            // built our own binaries for OS/X and Linux).  The bigger problem was that the nuget
            // includes the [xe.exe] content for projects that directly reference this package but
            // not for projects that reference this project indirectly via one or more intermediate
            // packages.
            //
            // After poking around the XenServer GitHub repo, I realized that it would be pretty
            // easy to adapt their code to submit commands to these hosts directly via HTTP.
            //
            //      https://github.com/xenserver/xenadmin/tree/master/xe
            //
            // So we're going to do that and decouple from [xe.exe].

            args = NormalizeArgs(command, args).ToArray();

            // Execute the command on the XenServer host via the thin CLI protocol,
            // capturing the output.

            return MainClass.Main(args);
        }

        /// <summary>
        /// Invokes a low-level <b>XE-CLI</b> command on the remote XenServer host
        /// that returns a list of items.
        /// </summary>
        /// <param name="command">The <b>XE-CLI</b> command.</param>
        /// <param name="args">The optional arguments formatted as <b>name=value</b>.</param>
        /// <returns>The command <see cref="XenResponse"/>.</returns>
        public XenResponse InvokeItems(string command, params string[] args)
        {
            EnsureNotDisposed();

            return new XenResponse(Invoke(command, args));
        }

        /// <summary>
        /// Invokes a low-level <b>XE-CLI</b> command on the remote XenServer host
        /// that returns text, throwing an exception on failure.
        /// </summary>
        /// <param name="command">The <b>XE-CLI</b> command.</param>
        /// <param name="args">The optional arguments formatted as <b>name=value</b>.</param>
        /// <returns>The command response.</returns>
        /// <exception cref="XenException">Thrown if the operation failed.</exception>
        public ExecuteResponse SafeInvoke(string command, params string[] args)
        {
            EnsureNotDisposed();

            var response = Invoke(command, args);

            if (response.ExitCode != 0)
            {
                throw new XenException($"XE-COMMAND: {command} MESSAGE: {response.AllText}");
            }

            return response;
        }

        /// <summary>
        /// Invokes a low-level <b>XE-CLI</b> command on the remote XenServer host
        /// that returns a list of items, throwing an exception on failure.
        /// </summary>
        /// <param name="command">The <b>XE-CLI</b> command.</param>
        /// <param name="args">The optional arguments formatted as <b>name=value</b>.</param>
        /// <returns>The command <see cref="XenResponse"/>.</returns>
        /// <exception cref="XenException">Thrown if the operation failed.</exception>
        public XenResponse SafeInvokeItems(string command, params string[] args)
        {
            return new XenResponse(SafeInvoke(command, args));
        }

        /// <summary>
        /// Returns information about the connected XenServer host machine.
        /// </summary>
        /// <returns>The <see cref="XenHostInfo"/>.</returns>
        public XenHostInfo GetHostInfo()
        {
            // List the hosts to obtain the host UUID.  We're going to assume that only the
            // current host will be returned and the configuring a resource pool doesn't change
            // this (which is probably not going to be the case in the real world).

            var response = SafeInvokeItems("host-list");

            Covenant.Assert(response.Items.Count == 1, "[xe host-list] is expected to return exactly one host.");

            var hostUuid = response.Items.Single()["uuid"];

            // Fetch the host parameters and extract the host version information.

            response = SafeInvokeItems("host-param-list", $"uuid={hostUuid}", "--all");

            var hostParams   = response.Items.Single();
            var versionItems = ParseValues(hostParams["software-version"]);
            var edition      = hostParams["edition"];
            var version      = versionItems["product_version"];

            //-----------------------------------------------------------------
            // Extract information about the available cores and memory.

            var cpuItems        = ParseValues(hostParams["cpu_info"]);
            var cpuCount        = cpuItems["cpu_count"];
            var usableCores     = int.Parse(cpuCount);
            var availableMemory = long.Parse(hostParams["memory-free-computed"]);

            //-----------------------------------------------------------------
            // Fetch information about the available disk space.

            // $note(jefflill):
            //
            // We're currently collecting information only for the [Local storage] repository.
            // Eventually, we'll need to modify this to collect information for all attached
            // repositories.

            // Fetch the parameters for the local storage repository and extract [physical-size] and
            // [physical-utilisation] to compute the available disk space.

            var srLocal = SafeInvokeItems("sr-list", $"name-label=Local storage").Items.SingleOrDefault();

            if (srLocal == null)
            {
                throw new XenException($"Cannot locate the [Local storage] storage repository.");
            }

            var srLocalUuid         = srLocal["uuid"];
            var srParams            = SafeInvokeItems("sr-param-list", $"uuid={srLocalUuid}").Items.Single();
            var physicalSize        = long.Parse(srParams["physical-size"]);
            var physicalUtilisation = long.Parse(srParams["physical-utilisation"]);
            var availableDisk       = physicalSize - physicalUtilisation;

            //-----------------------------------------------------------------
            // Construct and return the result.

            return new XenHostInfo()
            {
                Edition         = edition,
                Version         = SemanticVersion.Parse(version),
                Params          = new ReadOnlyDictionary<string, string>(hostParams),
                UsableCores     = usableCores,
                AvailableMemory = availableMemory,
                AvailableDisk   = availableDisk
            };
        }

        /// <summary>
        /// Used for temporarily uploading an ISO disk to a XenServer such that it can be mounted
        /// to a VM, typically for one-time initialization purposes.  NEONKUBE uses this as a very
        /// simple poor man's alternative to <b>cloud-init</b> for initializing a VM on first boot.
        /// </summary>
        /// <param name="isoPath">Path to the source ISO file on the local workstation.</param>
        /// <param name="srName">Optionally specifies the storage repository name.  <b>neon-UUID</b> with a generated UUID will be used by default.</param>
        /// <returns>A <see cref="XenTempIso"/> with information about the new storage repository and its contents.</returns>
        /// <remarks>
        /// <para>
        /// During cluster setup on virtualization platforms like XenServer and Hyper-V, NEONKUBE need
        /// to configure new VMs with IP addresses, hostnames, etc.  Traditionally, we've relied on
        /// being able to SSH into the VM to perform all of these actions, but this relied on being
        /// VM being able to obtain an IP address via DHCP and for setup to be able to discover the
        /// assigned address.
        /// </para>
        /// <para>
        /// The dependency on DHCP is somewhat problematic, because it's conceivable that this may
        /// not be available for more controlled environments.  We looked into using Linux <b>cloud-init</b>
        /// for this, but that requires additional local infrastructure for non-cloud deployments and
        /// was also a bit more complex than what we had time for.
        /// </para>
        /// <para>
        /// Instead of <b>cloud-init</b>, we provisioned our XenServer and Hyper-V node templates
        /// with a <b>neon-init</b> service that runs before the network service to determine
        /// whether a DVD (ISO) is inserted into the VM and runs the <b>neon-init.sh</b> script
        /// there one time, if it exists.  This script will initialize the node's IP address and 
        /// could also be used for other configuration as well, like setting user credentials.
        /// </para>
        /// <note>
        /// In theory, we could have used the same technique for mounting a <b>cloud-init</b> data source
        /// via this ISO, but we decided not to go there, at least for now (we couldn't get that working).
        /// </note>
        /// <note>
        /// NEONKUBE doesn't use this technique for true cloud deployments (AWS, Azure, Google,...) because
        /// we can configure VM networking directly via the cloud APIs.  
        /// </note>
        /// <para>
        /// The XenServer requires the temporary ISO implementation to be a bit odd.  We want these temporary
        /// ISOs to be created directly on the XenServer host machine so users won't have to configure any
        /// additional infrastructure as well as to simplify cluster setup.  We'll be creating a local
        /// ISO storage repository from a folder on the host.  Any files to be added to the repository
        /// must exist when the repository is created and it is not possible to add, modify, or remove
        /// files from a repository after its been created.
        /// </para>
        /// <note>
        /// XenServer hosts have only 4GB of free space at the root Linux level, so you must take care 
        /// not to create large ISOs or to allow these to accumulate.
        /// </note>
        /// <para>
        /// This method uploads the ISO file <paramref name="isoPath"/> from the local workstation to
        /// the XenServer host, creating a new folder named with a UUID.  Then a new storage repository
        /// will be created from this folder and a <see cref="XenTempIso"/> will be returned holding
        /// details about the new storage repository and its contents.  The setup code will use this to 
        /// insert the ISO into a VM.
        /// </para>
        /// <para>
        /// Once the setup code is done with the ISO, it will eject it from the VM and call
        /// <see cref="RemoveTempIso(XenTempIso)"/> to remove the storage repository.
        /// </para>
        /// </remarks>
        public XenTempIso CreateTempIso(string isoPath, string srName = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(isoPath), nameof(isoPath));

            if (string.IsNullOrEmpty(srName))
            {
                srName = "neon-" + Guid.NewGuid().ToString("d");
            }

            var tempIso = new XenTempIso();

            // Create the temporary SR subfolder and upload the ISO file.

            var srMountPath = "/var/run/sr-mount";

            tempIso.SrPath  = LinuxPath.Combine(srMountPath, Guid.NewGuid().ToString("d"));
            tempIso.IsoName = $"neon-dvd-{Guid.NewGuid().ToString("d")}.iso";

            if (!sftpClient.PathExists(srMountPath))
            {
                sftpClient.CreateDirectory(srMountPath);
            }

            if (!sftpClient.PathExists(tempIso.SrPath))
            {
                sftpClient.CreateDirectory(tempIso.SrPath);
                sftpClient.ChangePermissions(tempIso.SrPath, Convert.ToInt16("751", 8));
            }

            var xenIsoPath = LinuxPath.Combine(tempIso.SrPath, tempIso.IsoName);

            using (var isoInput = File.OpenRead(isoPath))
            {
                sftpClient.UploadFile(isoInput, xenIsoPath);
                sftpClient.ChangePermissions(xenIsoPath, Convert.ToInt16("751", 8));
            }

            // Create the new storage repository.  This command returns the [sr-uuid].

            var response = SafeInvoke("sr-create",
                $"name-label={tempIso.IsoName}",
                $"type=iso",
                $"device-config:location={tempIso.SrPath}",
                $"device-config:legacy_mode=true",
                $"content-type=iso");

            tempIso.SrUuid = response.OutputText.Trim();

            // XenServer created a PBD behind the scenes for the new SR.  We're going
            // to need its UUID so we can completely remove the SR later).  Note that
            // doesn't seem to appear immediately so, we'll retry a few times.

            var retry = new ExponentialRetryPolicy(typeof(InvalidOperationException), maxAttempts: 10, initialRetryInterval: TimeSpan.FromSeconds(5), maxRetryInterval: TimeSpan.FromSeconds(5));

            retry.Invoke(
                () =>
                {
                    var result = SafeInvokeItems("pbd-list", $"sr-uuid={tempIso.SrUuid}");

                    tempIso.PdbUuid = result.Items.Single()["uuid"];

                    // Obtain the UUID for the ISO's VDI within the SR.

                    result = SafeInvokeItems("vdi-list", $"sr-uuid={tempIso.SrUuid}");

                    tempIso.VdiUuid = result.Items.Single()["uuid"];
                });

            return tempIso;
        }

        /// <summary>
        /// Removes a temporary ISO disk along with its PBD and storage repository.
        /// </summary>
        /// <param name="tempIso">The ISO disk information returned by <see cref="CreateTempIso(string, string)"/>.</param>
        /// <remarks>
        /// <see cref="CreateTempIso(string, string)"/> for more information.
        /// </remarks>
        public void RemoveTempIso(XenTempIso tempIso)
        {
            // Remove the PBD and SR.

            SafeInvoke("pbd-unplug", $"uuid={tempIso.PdbUuid}");
            SafeInvoke("sr-forget", $"uuid={tempIso.SrUuid}");
        }

        /// <summary>
        /// Establishes an SSH connection to the assocated XenServer.
        /// </summary>
        /// <returns>The connected <see cref="LinuxSshProxy"/>.</returns>
        public LinuxSshProxy Connect()
        {
            var proxy = new LinuxSshProxy(Name, IPAddress.Parse(Address), SshCredentials.FromUserPassword(username, password));

            proxy.Connect();

            return proxy;
        }

        /// <summary>
        /// <para>
        /// Wipes the connected XenServer host by terminating and shutting down all virtual
        /// machines by default and optionally, selected virtual machine templates.
        /// </para>
        /// <note>
        /// **WARNING:** This is dangerous and should only be used when you are **VERY**
        /// sure that important workloads are not being hosted on the XenServer.  We
        /// generally use this for integration testing where XenServer hosts are dedicated
        /// exclusively for specific test runners.
        /// </note>
        /// </summary>
        /// <param name="deleteVMs">Optionally disable virtual machine removal by passing <c>false</c>.</param>
        /// <param name="templateSelector">Optionally specifies a selector that chooses which templates are removed.</param>
        public void WipeHost(bool deleteVMs = true, Func<XenTemplate, bool> templateSelector = null)
        {
            if (templateSelector != null)
            {
                foreach (var template in Template.List()
                    .Where(template => templateSelector(template)))
                {
                    Template.Destroy(template);
                }
            }

            if (deleteVMs)
            {
                Parallel.ForEach(Machine.List(), new ParallelOptions() { MaxDegreeOfParallelism = 10 },
                    machine =>
                    {
                        // Don't mess with the dedicated host controller VM.

                        if (machine.Properties["is-control-domain"] == "true")
                        {
                            return;
                        }

                        if (machine.PowerState != XenVmPowerState.Halted)
                        {
                            Machine.Shutdown(machine, turnOff: true);
                        }

                        Machine.Remove(machine);
                    });
            }
        }
    }
}
