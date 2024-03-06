// -----------------------------------------------------------------------------
// FILE:	    CompilationOptions.cs
// CONTRIBUTOR: NEONFORGE Team
// COPYRIGHT:   Copyright © 2005-2023 by NEONFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License").
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis.Diagnostics;

namespace Neon.Roslyn.Xunit
{
    /// <summary>
    /// Compilation options.
    /// </summary>
    public class CompilationOptions : AnalyzerConfigOptions
    {
        /// <summary>
        /// The options.
        /// </summary>
        public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string value)
        {
            return Options.TryGetValue(key, out value);
        }
    }
}
