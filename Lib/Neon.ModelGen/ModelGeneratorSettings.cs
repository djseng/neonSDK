//-----------------------------------------------------------------------------
// FILE:        ModelGeneratorSettings.cs
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
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

namespace Neon.ModelGen
{
    /// <summary>
    /// Specifies model code generator settings.
    /// </summary>
    public class ModelGeneratorSettings
    {
        /// <summary>
        /// Constructs an instance with reasonable settings.
        /// </summary>
        /// <param name="targetGroups">
        /// Specifies the targets to be included in the generated output code.
        /// </param>
        public ModelGeneratorSettings(params string[] targetGroups)
        {
            Covenant.Requires<ArgumentNullException>(targetGroups != null, nameof(targetGroups));

            foreach (var group in targetGroups)
            {
                if (string.IsNullOrEmpty(group))
                {
                    continue;
                }

                if (!Targets.Contains(group, StringComparer.InvariantCultureIgnoreCase))
                {
                    Targets.Add(group);
                }

                if (Targets.Count == 0)
                {
                    throw new ArgumentException("At least one target group must be specified.", nameof(targetGroups));
                }
            }
        }

        /// <summary>
        /// Indicates that service client code should not be generated.  This defaults to
        /// <c>false</c> and may be set to <c>true</c> when only the data models
        /// need to be generated.
        /// </summary>
        public bool NoServiceClients { get; set; } = false;

        /// <summary>
        /// Returns <c>true</c> if service client code generation is enabled.
        /// </summary>
        internal bool ServiceClients => !NoServiceClients;

        /// <summary>
        /// Optionally specifies the user experience framework that the generated 
        /// classes should support by generating additional code.
        /// </summary>
        public UxFrameworks UxFramework { get; set; } = UxFrameworks.None;

        /// <summary>
        /// Enhances data model code generation to prevent property loss
        /// for noSQL scenarios where somebody added a model property before
        /// all referencing applications have regenerated their data models.  
        /// This defaults to <c>true</c>.
        /// </summary>
        public bool RoundTrip { get; set; } = true;

        /// <summary>
        /// Optionally generate generated database persistance related code for
        /// data models tagged with <c>[Persistable]</c>.  This defaults to
        /// <c>false</c>.
        /// </summary>
        public bool Persisted { get; set; } = false;

        /// <summary>
        /// <para>
        /// Used to select a specific targets to be included in the
        /// generated output.
        /// </para>
        /// <note>
        /// All groups will be generated when the <see cref="Targets"/> 
        /// list is empty.
        /// </note>
        /// </summary>
        public List<string> Targets { get; set; } = new List<string>();

        /// <summary>
        /// Specifies the C# <c>namespace</c> to be used when generating the output
        /// code.  This defaults to <c>Neon.ModelGen.Output</c>.
        /// </summary>
        public string TargetNamespace { get; set; } = "Neon.ModelGen.Output";

        /// <summary>
        /// Specifies the C# <c>namespace</c> to be used to filter the
        /// service and data model classes processed by the code generator.
        /// This is especially handy for unit testing.  This defaults to
        /// <c>null</c> which disables any filtering.
        /// </summary>
        public string SourceNamespace { get; set; }

        /// <summary>
        /// Optionally enables source code debuggers to step into methods and properties 
        /// generated for both data and service models.  This is normally used
        /// only when debugging model generation and defaults to <c>false</c>.
        /// </summary>
        public bool AllowDebuggerStepInto { get; set; } = false;

        /// <summary>
        /// <para>
        /// Optionally specifies that model classes should be generated such that 
        /// document type <c>__T</c> properties be serialized using the old v1.x
        /// compatible <b>"__T"</b> property name rather than the <b>"T$$"</b>
        /// property name generated for <b>Neon.ModelGen v2+</b>.
        /// </para>
        /// <para>
        /// New projects should leave this alone but existing 1.x based projects
        /// must set this to <c>true</c> or use the new <b>neon-modelgen --v1compatible</b> 
        /// option to avoid database corruption and/or data transmission issues. 
        /// </para>
        /// </summary>
        public bool V1Compatible { get; set; } = false;
    }
}
