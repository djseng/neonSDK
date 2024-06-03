//-----------------------------------------------------------------------------
// FILE:        NoRetryPolicy.cs
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Tasks;

#pragma warning disable CS0067 // Event is never used

namespace Neon.Retry
{
    /// <summary>
    /// Implements an <see cref="IRetryPolicy"/> that does not attempt to retry operations.
    /// </summary>
    public class NoRetryPolicy : IRetryPolicy
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Returns a global invariant instance.
        /// </summary>
        public static NoRetryPolicy Instance { get; private set; } = new NoRetryPolicy();

        //---------------------------------------------------------------------
        // Instance members
        
        /// <summary>
        /// Constructor.
        /// </summary>
        public NoRetryPolicy()
        {
        }

        /// <inheritdoc/>
        public TimeSpan? Timeout => null;

        /// <inheritdoc/>
        public event Action<RetryTransientArgs> OnTransient;

        /// <inheritdoc/>
        public IRetryPolicy Clone(Func<Exception, bool> transientDetector = null)
        {
            // This class is invariant and doesn't use a transient detector
            // so we just return the same instance.
            
            return this;
        }

        /// <inheritdoc/>
        public async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await SyncContext.Clear;
            await action();
        }

        /// <inheritdoc/>
        public async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken  = default)
        {
            await SyncContext.Clear;
            return await action();
        }

        /// <inheritdoc/>
        public void Invoke(Action action, CancellationToken cancellationToken = default)
        {
        }

        /// <inheritdoc/>
        public TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default)
        {
            return default(TResult);
        }
    }
}
