//-----------------------------------------------------------------------------
// FILE:        EnumMember.cs
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
using System.Text;

namespace Neon.ModelGen
{
    /// <summary>
    /// Describes an <c>enum</c> member.
    /// </summary>
    public class EnumMember
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public EnumMember()
        {
        }

        /// <summary>
        /// The enumeration value name as it appears in code.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The enumeration value name as it is serialized.
        /// </summary>
        public string SerializedName { get; set; }

        /// <summary>
        /// The enumeration ordinal value.
        /// </summary>
        public string OrdinalValue { get; set; }
    }
}
