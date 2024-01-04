//-----------------------------------------------------------------------------
// FILE:        GitExtensions.cs
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
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Deployment;

using LibGit2Sharp;
using LibGit2Sharp.Handlers;

using Octokit;

using GitHubBranch     = Octokit.Branch;
using GitHubRepository = Octokit.Repository;
using GitHubSignature  = Octokit.Signature;

using GitBranch     = LibGit2Sharp.Branch;
using GitRepository = LibGit2Sharp.Repository;
using GitSignature  = LibGit2Sharp.Signature;

namespace Neon.GitHub
{
    /// <summary>
    /// Implements handy local git extension methods.
    /// </summary>
    public static class GitExtensions
    {
        //---------------------------------------------------------------------
        // GitRepository extensions

        /// <summary>
        /// Returns a local git repository is changes pending a commit.
        /// </summary>
        /// <param name="localRepo">The local git repository.</param>
        /// <returns><c>true</c> when the repository has pending changes.</returns>
        public static bool IsDirty(this GitRepository localRepo)
        {
            return localRepo.RetrieveStatus().IsDirty;
        }

        /// <summary>
        /// Returns the current checked-out branch for the local git repository.
        /// </summary>
        /// <param name="localRepo">The local git repository.</param>
        /// <returns>The current <see cref="GitBranch"/>.</returns>
        public static GitBranch CurrentBranch(this GitRepository localRepo)
        {
            return localRepo.Branches.SingleOrDefault(branch => branch.IsCurrentRepositoryHead);
        }

        //---------------------------------------------------------------------
        // GitSignature extensions

        /// <summary>
        /// Converts a <see cref="LibGit2Sharp.Signature"/> into a <see cref="LibGit2Sharp.Identity"/>.
        /// </summary>
        /// <param name="signature">The signature being converted.</param>
        /// <returns>The equivalent identity.</returns>
        public static Identity ToIdentity(this LibGit2Sharp.Signature signature)
        {
            Covenant.Requires<ArgumentNullException>(signature != null, nameof(signature));

            return new Identity(name: signature.Name, email: signature.Email);
        }
    }
}
