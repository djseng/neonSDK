//-----------------------------------------------------------------------------
// FILE:        RetryPolicyBase.cs
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

using Microsoft.Extensions.Logging;

using Neon.Common;
using Neon.Diagnostics;
using Neon.Time;

namespace Neon.Retry
{
    /// <summary>
    /// Base class for used to help implement a <see cref="IRetryPolicy"/>.
    /// </summary>
    public abstract class RetryPolicyBase : IRetryPolicy
    {
        /// <summary>
        /// Specifies the default default category name for logging transient exceptions.
        /// </summary>
        protected const string DefaultCategoryName = "transient-errors";

        private ILogger logger;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="categoryName">
        /// Optionally customizes the transient error logging source category name (defaults to <see cref="RetryPolicyBase.DefaultCategoryName"/>).
        /// You can disable transient error logging by passing <c>null</c> or by adding an event handler to <see cref="IRetryPolicy.OnTransient"/>
        /// that ignores the event and also indicates that the event was handled.
        /// </param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained).</param>
        public RetryPolicyBase(string categoryName = DefaultCategoryName, TimeSpan? timeout = null)
        {
            this.CategoryName = categoryName;
            this.Timeout      = timeout;

            if (!string.IsNullOrEmpty(categoryName))
            {
                this.logger = TelemetryHub.CreateLogger(categoryName);
            }
        }

        /// <inheritdoc/>
        public TimeSpan? Timeout { get; private set; }

        /// <inheritdoc/>
        public event Action<RetryTransientArgs> OnTransient;

        /// <inheritdoc/>
        public abstract IRetryPolicy Clone(Func<Exception, bool> transientDetector = null);

        /// <inheritdoc/>
        public abstract Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract void Invoke(Action action, CancellationToken cancellationToken = default);

        /// <inheritdoc/>
        public abstract TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the associated log source category name (or <c>null)</c>.
        /// </summary>
        protected string CategoryName { get; private set; }

        /// <summary>
        /// Handles logging of transient exceptions by invoking any <see cref="OnTransient"/>
        /// event handlers and then logging the transient exception when none of the handlers
        /// indicated that they handled the event (or there were no handlers).
        /// </summary>
        /// <param name="e">The transient exception.</param>
        protected void LogTransient(Exception e)
        {
            if (OnTransient == null)
            {
                logger?.LogWarningEx(e, $"Transient: {e.Message}", attributes => attributes.Add(LogAttributeNames.NeonTransient, true));
            }
            else
            {
                var args = new RetryTransientArgs(e);

                foreach (var handler in OnTransient.GetInvocationList())
                {
                    handler.DynamicInvoke(args);

                    if (args.Handled)
                    {
                        return;
                    }
                }

                logger?.LogWarningEx(e, $"Transient: {e.Message}", attributes => attributes.Add(LogAttributeNames.NeonTransient, true));
            }
        }

        /// <summary>
        /// Computes the time (SYS) after which the operation should not be retried.
        /// </summary>
        protected DateTime SysDeadline()
        {
            var timeout = Timeout ?? TimeSpan.MaxValue;

            if (timeout >= TimeSpan.Zero)
            {
                this.Timeout = timeout;

                // Compute the SYS deadline, taking care not not to exceed the end-of-time.

                var sysNow = SysTime.Now;

                if (timeout >= DateTime.MaxValue - sysNow)
                {
                    return DateTime.MaxValue;
                }
                else
                {
                    return sysNow + timeout;
                }
            }
            else
            {
                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// Adjusts the delay <see cref="TimeSpan"/> passed to ensure such
        /// that delaying the next retry won't exceed the overall retry
        /// timeout (if specified).
        /// </summary>
        /// <param name="delay">The requested delay.</param>
        /// <param name="sysDeadline">The retry deadline (SYS) computed by <see cref="SysDeadline()"/>.</param>
        /// <returns>The adjusted delay.</returns>
        /// <remarks>
        /// <note>
        /// If the result is <see cref="TimeSpan.Zero"/> or negative, the
        /// calling retry policy should immediately stop retrying.
        /// </note>
        /// </remarks>
        protected TimeSpan AdjustDelay(TimeSpan delay, DateTime sysDeadline)
        {
            Covenant.Requires<ArgumentException>(delay >= TimeSpan.Zero, nameof(delay));

            var sysNow   = SysTime.Now;
            var maxDelay = sysDeadline - sysNow;

            if (delay > maxDelay)
            {
                delay = maxDelay;
            }

            if (delay <= TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }
            else
            {
                return delay;
            }
        }
    }
}
