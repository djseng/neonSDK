//-----------------------------------------------------------------------------
// FILE:        GccCommand.cs
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;
using Neon.WSL;

namespace WslUtil
{
    /// <summary>
    /// Implements the <b>gcc</b> command.
    /// </summary>
    [Command]
    public class GccCommand : CommandBase
    {
        private const string usage = @"
Compiles a C program for Linux within the current WSL2 distribution.  Note that
the current distribution must be Debian or Ubuntu based.

USAGE:

    wsl-util gcc SOURCE-FOLDER OUTPUT-FILE [GCC-OPTIONS...]

ARGUMENTS:

    SOURCE-FOLDER       - Path to the Windows folder holding the source files
    OUTPUT-PATH         - Path to the Windows file where the binary output will be written
    GCC-OPTIONS         - Optional arguments and/or options to be passed to [gcc]

REMARKS:

This command requires that the [neon-ubuntu-20.04] WSL distribution is already installed
as described in the developer setup instructions.

";
        /// <inheritdoc/>
        public override string[] Words => new string[] { "gcc" };

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override async Task RunAsync(CommandLine commandLine)
        {
            if (commandLine.HasHelpOption || commandLine.Arguments.Length == 0)
            {
                Console.WriteLine(usage);
                Program.Exit(commandLine.HasHelpOption ? 0 : -1);
            }

            var sourceFolder = commandLine.Arguments.ElementAtOrDefault(0);
            var outputPath   = commandLine.Arguments.ElementAtOrDefault(1);

            if (string.IsNullOrEmpty(sourceFolder) || string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine(usage);
                Program.Exit(-1);
            }

            Console.WriteLine();

            // Ensure that the [neon-ubuntu-20.04] WSL distro exists.

            var distroName = Wsl2Proxy.List().SingleOrDefault(name => name.Equals("neon-ubuntu-20.04", StringComparison.CurrentCultureIgnoreCase));

            if (distroName == null)
            {
                Console.Error.WriteLine("*** ERROR: WSL distro [neon-ubuntu-20.04] does not exist.");
                Console.Error.WriteLine("           Install this as described in the developer setup instructions.");
                Program.Exit(1);
            }

            var distro = new Wsl2Proxy(distroName);

            if (!distro.IsDebian)
            {
                Console.Error.WriteLine($"*** ERROR: The [{distro.Name}] distro is running: {distro.OSRelease["ID"]}/{distro.OSRelease["ID_LIKE"]}");
                Console.Error.WriteLine($"           [wsl-util gcc ...] requires a Debian/Ubuntu based distribution.");
                Program.Exit(1);
            }

            // We're going to build this within the distro at [/tmp/wsl-util/GUID] by
            // recursively copying the contents of SOURCE-FOLDER to this directory,
            // running GCC to build the thing, passing [*.c] to include all of the C
            // source files and generating the binary as [output.bin] with the folder.
            //
            // We're also going to clear the [/tmp/wsl-util] folder first to ensure that we don't
            // accumulate any old build files over time and we'll also ensure that
            // [gcc] is installed.

            var sbGccArgs = new StringBuilder();

            foreach (var arg in commandLine.Arguments.Skip(2))
            {
                sbGccArgs.AppendWithSeparator(arg);
            }

            var linuxUtilFolder    = LinuxPath.Combine("/", "tmp", "wsl-util");
            var linuxBuildFolder   = LinuxPath.Combine(linuxUtilFolder, Guid.NewGuid().ToString("d"));
            var linuxOutputPath    = LinuxPath.Combine(linuxBuildFolder, "output.bin");
            var windowsUtilFolder  = distro.ToWindowsPath(linuxUtilFolder);
            var windowsBuildFolder = distro.ToWindowsPath(linuxBuildFolder);

            try
            {
                // Delete the [/tmp/wsl-util] folder on Linux and the copy the
                // source from the Windows side into a fresh distro folder.

                NeonHelper.DeleteFolder(windowsUtilFolder);
                NeonHelper.CopyFolder(sourceFolder, windowsBuildFolder);

                // Install [safe-apt-get] if it's not already present.  We're using this
                // because it's possible that projects build in parallel so it's possible
                // that multiple GCC commands could also be running in parallel.
                //
                // This will ensure that parallel package installations won't conflict.

                var linuxSafeAptGetPath   = "/usr/bin/safe-apt-get";
                var windowsSafeAptGetPath = distro.ToWindowsPath(linuxSafeAptGetPath);

                if (!File.Exists(windowsSafeAptGetPath))
                {
                    var resources  = Assembly.GetExecutingAssembly().GetResourceFileSystem("WslUtil.Resources");
                    var toolScript = resources.GetFile("/safe-apt-get.sh").ReadAllText();

                    // Note that we need to escape all "$" characters in the script
                    // so the upload script won't attempt to replace any variables
                    // (with blanks).

                    toolScript = toolScript.Replace("$", "\\$");

                    var uploadScript =
$@"
cat <<EOF > {linuxSafeAptGetPath}
{toolScript}
EOF

chmod 754 {linuxSafeAptGetPath}
";
                    distro.SudoExecuteScript(uploadScript).EnsureSuccess();
                }

                // Perform the build.

                var buildScript =
$@"
set -euo pipefail

apt-get install -yq gcc

cd {linuxBuildFolder}
gcc *.c -o {linuxOutputPath} {sbGccArgs}
";
                distro.SudoExecuteScript(buildScript).EnsureSuccess();

                // Copy the build output to the Windows output path.

                Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
                NeonHelper.DeleteFile(outputPath);
                File.Copy(distro.ToWindowsPath(linuxOutputPath), outputPath);
            }
            finally
            {
                // Remove the temporary distro folder.

                NeonHelper.DeleteFolder(windowsBuildFolder);
            }

            await Task.CompletedTask;
        }
    }
}
