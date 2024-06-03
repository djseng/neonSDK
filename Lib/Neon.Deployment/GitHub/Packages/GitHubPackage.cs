//-----------------------------------------------------------------------------
// FILE:        GitHubPackage.cs
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

using Neon.Common;

namespace Neon.Deployment
{
    /// <summary>
    /// Describes a GitHub package.
    /// </summary>
    public class GitHubPackage
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public GitHubPackage()
        {
        }

        /// <summary>
        /// Specifies the package name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Specifies the package type (one of the <see cref="GitHubPackageType"/> values.
        /// </summary>
        public GitHubPackageType Type { get; set; }

        /// <summary>
        /// Specifies the package visibility.
        /// </summary>
        public GitHubPackageVisibility Visibility { get; set; }

        /// <summary>
        /// Specifies the known versions for the package.
        /// </summary>
        public List<GitHubPackageVersion> Versions { get; set; } = new List<GitHubPackageVersion>();
    }
}
