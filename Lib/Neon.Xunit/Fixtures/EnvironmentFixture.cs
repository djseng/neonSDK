//-----------------------------------------------------------------------------
// FILE:        EnvironmentFixture.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.IO;
using Neon.Retry;

namespace Neon.Xunit
{
    /// <summary>
    /// Used to save environment variables before unit tests run and then restore them afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You may instantiate a single <see cref="EnvironmentFixture"/> within your unit
    /// tests to manage environment variables and test files and folders such as simulated 
    /// service config and secret files.
    /// </para>
    /// <note>
    /// <para>
    /// <b>IMPORTANT:</b> The base Neon <see cref="TestFixture"/> implementation <b>DOES NOT</b>
    /// support parallel test execution.  You need to explicitly disable parallel execution in 
    /// all test assemblies that rely on thesex test fixtures by adding a C# file called 
    /// <c>AssemblyInfo.cs</c> with:
    /// </para>
    /// <code language="csharp">
    /// [assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
    /// </code>
    /// <para>
    /// and then define your test classes like:
    /// </para>
    /// <code language="csharp">
    /// public class MyTests : IClassFixture&lt;EnvironmentFixture&gt;
    /// {
    ///     private EnvironmentFixture fixture;
    ///     
    ///     public MyTests()
    ///     {
    ///         this.fixture = fixture;
    /// 
    ///         if (fixture.Start() == TestFixtureStatus.AlreadyRunning)
    ///         {
    ///             fixture.Restore();
    ///         }
    ///     }
    ///     
    ///     [Collection(TestCollection.NonParallel)]
    ///     [CollectionDefinition(TestCollection.NonParallel, DisableParallelization = true)]
    ///     [Fact]
    ///     public void Test()
    ///     {
    ///     }
    /// }
    /// </code>
    /// </note>
    /// </remarks>
    /// <threadsafety instance="true"/>
    public class EnvironmentFixture : TestFixture
    {
        private readonly object             syncLock = new object();
        private Dictionary<string, string>  orgEnvironment;

        /// <summary>
        /// Constructs the fixture.
        /// </summary>
        public EnvironmentFixture()
        {
            // Grab the environment variables before any tests run so we can
            // restore the environment variable state after the tests complete.

            orgEnvironment = new Dictionary<string, string>();

            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                orgEnvironment[(string)variable.Key] = (string)variable.Value;
            }
        }

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~EnvironmentFixture()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases all associated resources.
        /// </summary>
        /// <param name="disposing">Pass <c>true</c> if we're disposing, <c>false</c> if we're finalizing.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && !base.IsDisposed)
            {
                Reset();
                GC.SuppressFinalize(this);
            }
        }

        /// <summary>
        /// <b>INTERNAL USE ONLY:</b> Resets the fixture state.
        /// </summary>
        public override void Reset()
        {
        }

        /// <summary>
        /// Restores the original environment variables captured at the time the
        /// fixture was instantiated and also removes any temporary test files.
        /// </summary>
        public void Restore()
        {
            lock (syncLock)
            {
                // Remove all existing environment variables.

                foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
                {
                    Environment.SetEnvironmentVariable((string)variable.Key, null);
                }

                // ...and now restore the original variables.

                foreach (var variable in orgEnvironment)
                {
                    Environment.SetEnvironmentVariable(variable.Key, variable.Value);
                }
            }
        }
    }
}
