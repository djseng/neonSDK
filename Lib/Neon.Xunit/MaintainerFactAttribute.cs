//-----------------------------------------------------------------------------
// FILE:        MaintainerFactAttribute.cs
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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;

using Xunit;

namespace Neon.Xunit
{
    /// <summary>
    /// Inherits from <see cref="FactAttribute"/> and sets <see cref="FactAttribute.Skip"/> when
    /// the current operating system platform doesn't match any of the specified platform flags.
    /// </summary>
    public class MaintainerFactAttribute : FactAttribute
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public MaintainerFactAttribute()
            : base()
        {
            if (!NeonHelper.IsMaintainer)
            {
                Skip = $"Unit test is enabled only for NEONFORGE maintainers";
            }
        }
    }
}
