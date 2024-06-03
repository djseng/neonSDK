//-----------------------------------------------------------------------------
// FILE:        LinuxSshProxyT.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Renci.SshNet;
using Renci.SshNet.Common;

// $todo(jefflill):
//
// The download methods don't seem to be working for paths like [/proc/meminfo].
// They return an empty stream.

namespace Neon.SSH
{
    /// <summary>
    /// <para>
    /// Uses a SSH/SCP connection to provide access to Linux machines to access
    /// files, run commands, etc.  This extends <see cref="LinuxSshProxy"/> by 
    /// adding the <see cref="Metadata"/> property with a generic type.
    /// </para>
    /// <note>
    /// <b>IMPORTANT:</b> We use this class to manage Ubuntu Linux machines.  This 
    /// will likely work for Debian and other Debian based distros but other distros 
    /// like Alpine and Red Hat may have problems or may not work at all.
    /// </note>
    /// </summary>
    /// <typeparam name="TMetadata">
    /// Defines the metadata type the application wishes to associate with the server.
    /// You may specify <c>object</c> when no additional metadata is required.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// Construct an instance to connect to a specific cluster node.  You may specify
    /// <typeparamref name="TMetadata"/> to associate application specific information
    /// or state with the instance.
    /// </para>
    /// <para>
    /// This class includes methods to invoke Linux commands on the node,
    /// </para>
    /// <para>
    /// Call <see cref="LinuxSshProxy.Dispose()"/> or <see cref="LinuxSshProxy.Disconnect()"/> to close the connection.
    /// </para>
    /// <note>
    /// You can use <see cref="Clone()"/> to make a copy of a proxy that can be
    /// used to perform parallel operations against the same machine.
    /// </note>
    /// </remarks>
    /// <threadsafety instance="false"/>
    public class LinuxSshProxy<TMetadata> : LinuxSshProxy
        where TMetadata : class
    {
        /// <summary>
        /// Constructs a <see cref="LinuxSshProxy{TMetadata}"/>.
        /// </summary>
        /// <param name="name">The display name for the server.</param>
        /// <param name="address">The private cluster IP address for the server.</param>
        /// <param name="credentials">The credentials to be used for establishing SSH connections.</param>
        /// <param name="port">Optionally overrides the standard SSH port (22).</param>
        /// <param name="logWriter">The optional <see cref="TextWriter"/> where operation logs will be written.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="name"/> or if <paramref name="credentials"/> is <c>null</c>.
        /// </exception>
        public LinuxSshProxy(string name, IPAddress address, SshCredentials credentials, int port = NetworkPorts.SSH, TextWriter logWriter = null)
            : base(name, address, credentials, port, logWriter)
        {
        }

        /// <summary>
        /// Releases all associated resources (e.g. any open server connections).
        /// </summary>
        /// <param name="disposing">Pass <c>true</c> if we're disposing, <c>false</c> if we're finalizing.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        /// <summary>
        /// Applications may use this to associate metadata with the instance.
        /// </summary>
        public TMetadata Metadata { get; set; }

        /// <summary>
        /// Returns a clone of the SSH proxy.  This can be useful for situations where you
        /// need to be able to perform multiple SSH/SCP operations against the same
        /// machine in parallel.
        /// </summary>
        /// <returns>The cloned <see cref="LinuxSshProxy{TMetadata}"/>.</returns>
        public new LinuxSshProxy<TMetadata> Clone()
        {
            var clone = new LinuxSshProxy<TMetadata>(Name, Address, credentials);

            CloneTo(clone);

            return clone;
        }

        /// <summary>
        /// Used by derived classes to copy the base class state to a new
        /// instance as well as configure the new connection's SSH and SCP
        /// clients.
        /// </summary>
        /// <param name="target">The target proxy.</param>
        protected void CloneTo(LinuxSshProxy<TMetadata> target)
        {
            Covenant.Requires<ArgumentNullException>(target != null, nameof(target));

            base.CloneTo(target);

            target.Metadata = this.Metadata;
        }
    }
}
