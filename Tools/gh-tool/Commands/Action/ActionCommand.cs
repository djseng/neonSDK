//-----------------------------------------------------------------------------
// FILE:        ActionCommand.cs
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;

using Neon.Common;

namespace GHTool
{
    /// <summary>
    /// Implements the <b>action</b> command.
    /// </summary>
    [Command]
    public class ActionCommand : CommandBase
    {
        private const string usage = @"
Commands to manage GitHub Action workflows.

USAGE:

    neon action run delete REPO [WORKFLOW-NAME] [--max-age-days=AGE]
";

        /// <inheritdoc/>
        public override string[] Words => new string[] { "workflow" };

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override async Task RunAsync(CommandLine commandLine)
        {
            Help();
            await Task.CompletedTask;
        }
    }
}
