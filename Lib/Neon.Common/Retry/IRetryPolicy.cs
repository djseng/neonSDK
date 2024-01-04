//-----------------------------------------------------------------------------
// FILE:        IRetryPolicy.cs
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

#pragma warning disable CS0067 // Event is never used

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Neon.Retry
{
    /// <summary>
    /// Describes the behavior of an operation retry policy.  These are used
    /// to retry operations that have failed due to transient errors.
    /// </summary>
    [ContractClass(typeof(IRetryPolicyContract))]
    public interface IRetryPolicy
    {
        /// <summary>
        /// Returns the optional policy timeout.  When present, this specifies the
        /// maximum time the policy will continue retrying the operation.
        /// </summary>
        TimeSpan? Timeout { get; }

        /// <summary>
        /// Returns a copy of the retry policy.
        /// </summary>
        /// <param name="transientDetector">
        /// Optionally specifies a replacement transient detector function 
        /// that will be set in the cloned policy.
        /// </param>
        /// <returns>The policy copy.</returns>
        IRetryPolicy Clone(Func<Exception, bool> transientDetector = null);

        /// <summary>
        /// Retries an asynchronous action that returns no result when it throws exceptions due to 
        /// transient errors.  The classification of what is a transient error, the interval
        /// between the retries as well as the number of times the operation are retried are
        /// determined by the policy implementation.
        /// </summary>
        /// <param name="action">The asynchronous action to be performed.</param>
        /// <param name="cancellationToken">Optionally specifies a cancellation token.</param>
        Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retries an asynchronous action that returns <typeparamref name="TResult"/> when it throws exceptions
        /// due to transient errors.  he classification of what is a transient error, the interval 
        /// between the retries as well as the number of times the operation are retried are 
        /// determined by the policy implementation. 
        /// </summary>
        /// <typeparam name="TResult">The action result type.</typeparam>
        /// <param name="action">The asynchronous action to be performed.</param>
        /// <param name="cancellationToken">Optionally specifies a cancellation token.</param>
        /// <returns>The action result.</returns>
        Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retries a synchronous action that returns no result when it throws exceptions due to 
        /// transient errors.  The classification of what is a transient error, the interval
        /// between the retries as well as the number of times the operation are retried are
        /// determined by the policy implementation.
        /// </summary>
        /// <param name="action">The synchronous action to be performed.</param>
        /// <param name="cancellationToken">Optionally specifies a cancellation token.</param>
        void Invoke(Action action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retries a synchronous action that returns a result when it throws exceptions due to 
        /// transient errors.  The classification of what is a transient error, the interval
        /// between the retries as well as the number of times the operation are retried are
        /// determined by the policy implementation.
        /// </summary>
        /// <typeparam name="TResult">The action result type.</typeparam>
        /// <param name="action">The synchronous action to be performed.</param>
        /// <param name="cancellationToken">Optionally specifies a cancellation token.</param>
        /// <returns>The action result.</returns>
        TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// <para>
        /// Used to intercept and handle logging for transient exceptions detected by
        /// a retry policy.  Handlers can set <see cref="RetryTransientArgs.Handled"/>
        /// in the argument passed to prevent subsequent handlers from being invoked
        /// and also prevent the transient exception from being logged.
        /// </para>
        /// <para>
        /// When no handlers are added to this event, the default behavior is to log
        /// all transient failures.
        /// </para>
        /// </summary>
        event Action<RetryTransientArgs> OnTransient;
    }

    [ContractClassFor(typeof(IRetryPolicy))]
    internal abstract class IRetryPolicyContract : IRetryPolicy
    {
        public TimeSpan? Timeout => null;

        public IRetryPolicy Clone(Func<Exception, bool> transientFunc = null)
        {
            return null;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            Covenant.Requires<ArgumentNullException>(action != null, nameof(action));

            return null;
        }

        public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            Covenant.Requires<ArgumentNullException>(action != null, nameof(action));

            return null;
        }

        public void Invoke(Action action, CancellationToken cancellationToken = default)
        {
            Covenant.Requires<ArgumentNullException>(action != null, nameof(action));
        }

        public TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default)
        {
            Covenant.Requires<ArgumentNullException>(action != null, nameof(action));

            return default(TResult);
        }

        public event Action<RetryTransientArgs> OnTransient;
    }
}
