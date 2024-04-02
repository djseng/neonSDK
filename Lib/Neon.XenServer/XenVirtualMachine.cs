//-----------------------------------------------------------------------------
// FILE:        XenVirtualMachine.cs
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

using Neon.Common;

namespace Neon.XenServer
{
    /// <summary>
    /// Describes a XenServer virtual machine.
    /// </summary>
    public class XenVirtualMachine : XenObject
    {
        private List<string>    tags = new List<string>();

        /// <summary>
        /// Constructs an instance from raw property values returned by the <b>xe client</b>.
        /// </summary>
        /// <param name="rawProperties">The raw object properties.</param>
        internal XenVirtualMachine(IDictionary<string, string> rawProperties)
            : base(rawProperties)
        {
            if (rawProperties.TryGetValue("uuid", out var uuid))
            {
                this.Uuid = uuid;
            }

            if (rawProperties.TryGetValue("name-label", out var nameLabel))
            {
                this.NameLabel = nameLabel;
            }

            if (rawProperties.TryGetValue("power-state", out var rawPowerState))
            {
                if (NeonHelper.TryParse<XenVmPowerState>(rawPowerState, out var powerState))
                {
                    this.PowerState = powerState;
                }
            }

            if (rawProperties.TryGetValue("name-description", out var description))
            {
                this.Description = description;
            }

            if (rawProperties.TryGetValue("tags", out var rawTags) &&
                !string.IsNullOrWhiteSpace(rawTags))
            {
                foreach (var rawTag in rawTags.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var tag = rawTag.Trim();

                    if (!string.IsNullOrEmpty(tag))
                    {
                        tags.Add(tag);
                    }
                }
            }

            // We're only going to explicitly support one network interface,
            // interface 0.  We're going to attempt extracting the IP address
            // from the [networks] property which will probably look something
            // like this:
            //
            //      networks (MRO): 0/ip: 10.50.0.236; 0/ipv6/0: fe80::46f:19ff:fe95:a5d2

            if (rawProperties.TryGetValue("networks", out var networks))
            {
                var pattern = "0/ip: ";
                var pos     = networks.IndexOf(pattern);

                if (pos != -1)
                {
                    pos += pattern.Length;

                    var posEnd = networks.IndexOf(";", pos);

                    if (posEnd != -1)
                    {
                        Address = networks.Substring(pos, posEnd - pos).Trim();
                    }
                    else
                    {
                        Address = networks.Substring(pos).Trim();
                    }
                }
            }
        }

        /// <summary>
        /// Returns the virtual machine ID.
        /// </summary>
        public string Uuid { get; private set; }

        /// <summary>
        /// Returns the virtual machine name.
        /// </summary>
        public string NameLabel { get; private set; }

        /// <summary>
        /// Returns the virtual machine state.
        /// </summary>
        public XenVmPowerState PowerState { get; private set; } = XenVmPowerState.Unknown;

        /// <summary>
        /// Returns the IP address associated with the VM or <c>null</c>
        /// if the VM is not running or hasn't obtained an address yet.
        /// </summary>
        public string Address { get; private set; }

        /// <summary>
        /// Returns the virtual machine description
        /// </summary>
        public string Description { get; private set; }

        /// <summary>
        /// Returns the virtual machine tags.
        /// </summary>
        public IEnumerable<string> Tags => tags;

        /// <summary>
        /// Indicates whether the virtual machine is running.
        /// </summary>
        public bool IsRunning => PowerState == XenVmPowerState.Running;
    }
}
