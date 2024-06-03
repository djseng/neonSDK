//-----------------------------------------------------------------------------
// FILE:        LinearRetryPolicy.cs
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

using Neon.Diagnostics;
using Neon.Tasks;

namespace Neon.Retry
{
    /// <summary>
    /// Implements a simple <see cref="IRetryPolicy"/> that retries an operation 
    /// at a fixed interval for a specified maximum number of times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// You can enable transient error logging by passing a non-empty <b>logCategory</b>
    /// name to the constructor.  This creates an embedded <see cref="ILogger"/>
    /// using that name and any retried transient errors will then be logged as
    /// warnings including <b>[transient-retry]</b> in the message.
    /// </para>
    /// <note>
    /// Only the retried errors will be logged.  The final exception thrown after
    /// all retries fail will not be logged because it's assumed that these will
    /// be caught and handled upstack by application code.
    /// </note>
    /// <para>
    /// Choose a category name that can be used to easily identify the affected
    /// component.  For example, <b>yugabyte:my-cluster</b> to identify a
    /// specific YugaBYte database cluster.
    /// </para>
    /// </remarks>
    public class LinearRetryPolicy : RetryPolicyBase, IRetryPolicy
    {
        private Func<Exception, bool>   transientDetector;

        /// <summary>
        /// Constructs the retry policy with a specific transitent detection function.
        /// </summary>
        /// <param name="transientDetector">
        /// Optionally specifies the function that determines whether an exception is transient 
        /// (see <see cref="TransientDetector"/>).  You can pass <c>null</c> when all exceptions
        /// are to be considered to be transient.
        /// </param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="retryInterval">Optionally specifies time interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <param name="categoryName">
        /// Optionally customizes the transient error logging source category name (defaults to <see cref="RetryPolicyBase.DefaultCategoryName"/>).
        /// You can disable transient error logging by passing <c>null</c> or by adding an event handler to <see cref="IRetryPolicy.OnTransient"/>
        /// that ignores the event and also indicates that the event was handled.
        /// </param>
        /// <remarks>
        /// <para>
        /// The <paramref name="maxAttempts"/> parameter defaults to <b>-1</b> indicating that the
        /// operation should be attempted up to <b>5</b> times, unless a <see cref="Timeout"/> is
        /// specified.  In this case, <paramref name="maxAttempts"/> will be ignored and the timeout
        /// will be honored.
        /// </para>
        /// <para>
        /// When <paramref name="maxAttempts"/> is greater than or equal to zero and <see cref="Timeout"/> 
        /// is passed, then both <paramref name="maxAttempts"/> and <see cref="Timeout"/> will be honored,
        /// with retries stopping when either are exceeded.
        /// </para>
        /// </remarks>
        public LinearRetryPolicy(
            Func<Exception, bool>   transientDetector = null, 
            int                     maxAttempts       = -1, 
            TimeSpan?               retryInterval     = null, 
            TimeSpan?               timeout           = null, 
            string                  categoryName      = DefaultCategoryName)

            : base(categoryName, timeout)
        {
            Covenant.Requires<ArgumentException>(retryInterval == null || retryInterval >= TimeSpan.Zero, nameof(retryInterval));

            this.transientDetector = transientDetector ?? (e => true);
            this.RetryInterval     = retryInterval ?? TimeSpan.FromSeconds(1);

            if (maxAttempts < 0)
            {
                MaxAttempts = timeout == null ? RetryPolicy.DefaultMaxAttempts : int.MaxValue;
            }
            else
            {
                MaxAttempts = maxAttempts;
            }
        }

        /// <summary>
        /// Constructs the retry policy to handle a specific exception type as transient.
        /// </summary>
        /// <param name="exceptionType">Specifies the exception type to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="retryInterval">Optionally specifies the time interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="categoryName">Optionally enables transient error logging by identifying the source category name (defaults to <c>null</c>).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <remarks>
        /// <para>
        /// The <paramref name="maxAttempts"/> parameter defaults to <b>-1</b> indicating that the
        /// operation should be attempted up to <b>5</b> times, unless a <see cref="Timeout"/> is
        /// specified.  In this case, <paramref name="maxAttempts"/> will be ignored and the timeout
        /// will be honored.
        /// </para>
        /// <para>
        /// When <paramref name="maxAttempts"/> is greater than or equal to zero and <see cref="Timeout"/> 
        /// is passed, then both <paramref name="maxAttempts"/> and <see cref="Timeout"/> will be honored,
        /// with retries stopping when either are exceeded.
        /// </para>
        /// </remarks>
        public LinearRetryPolicy(
            Type        exceptionType, 
            int         maxAttempts   = -1, 
            TimeSpan?   retryInterval = null, 
            TimeSpan?   timeout       = null, 
            string      categoryName  = null)
            : this
            (
                e => TransientDetector.MatchException(e, exceptionType),
                maxAttempts,
                retryInterval,
                timeout,
                categoryName
            )
        {
            Covenant.Requires<ArgumentNullException>(exceptionType != null, nameof(exceptionType));
        }

        /// <summary>
        /// Constructs the retry policy to handle a multiple exception types as transient.
        /// </summary>
        /// <param name="exceptionTypes">Specifies the exception types to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="retryInterval">Optionally specifies the time interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="timeout">Optionally specifies the maximum time the operation will be retried (defaults to unconstrained)</param>
        /// <param name="categoryName">Optionally enables transient error logging by identifying the source category name (defaults to <c>null</c>).</param>
        /// <remarks>
        /// <para>
        /// The <paramref name="maxAttempts"/> parameter defaults to <b>-1</b> indicating that the
        /// operation should be attempted up to <b>5</b> times, unless a <see cref="Timeout"/> is
        /// specified.  In this case, <paramref name="maxAttempts"/> will be ignored and the timeout
        /// will be honored.
        /// </para>
        /// <para>
        /// When <paramref name="maxAttempts"/> is greater than or equal to zero and <see cref="Timeout"/> 
        /// is passed, then both <paramref name="maxAttempts"/> and <see cref="Timeout"/> will be honored,
        /// with retries stopping when either are exceeded.
        /// </para>
        /// </remarks>
        public LinearRetryPolicy(
            Type[]      exceptionTypes, 
            int         maxAttempts   = -1, 
            TimeSpan?   retryInterval = null, 
            TimeSpan?   timeout       = null, 
            string      categoryName  = null)
            : this
            (
                e =>
                {
                    if (exceptionTypes == null)
                    {
                        return false;
                    }

                    foreach (var type in exceptionTypes)
                    {
                        if (TransientDetector.MatchException(e, type))
                        {
                            return true;
                        }
                    }

                    return false;
                },
                maxAttempts,
                retryInterval,
                timeout,
                categoryName
            )
        {
        }

        /// <summary>
        /// Returns the maximum number of times the action should be attempted.
        /// </summary>
        public int MaxAttempts { get; private set; }

        /// <summary>
        /// Returns the fixed interval between action retry attempts.
        /// </summary>
        public TimeSpan RetryInterval { get; private set; }

        /// <inheritdoc/>
        public override IRetryPolicy Clone(Func<Exception, bool> transientDetector = null)
        {
            if (transientDetector == null)
            {
                // The class is invariant so we can safely return ourself
                // when we're retaining the current transient detector.

                return this;
            }
            else
            {
                return new LinearRetryPolicy(transientDetector ?? this.transientDetector, MaxAttempts, RetryInterval, Timeout, CategoryName);
            }
        }

        /// <inheritdoc/>
        public override async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await SyncContext.Clear;

            var attempts    = 0;
            var sysDeadline = base.SysDeadline();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await action();
                    return;
                }
                catch (Exception e)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var adjustedDelay = AdjustDelay(RetryInterval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay, cancellationToken);
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            await SyncContext.Clear;

            var attempts    = 0;
            var sysDeadline = base.SysDeadline();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await action();
                }
                catch (Exception e)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var adjustedDelay = AdjustDelay(RetryInterval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay, cancellationToken);
                }
            }
        }

        /// <inheritdoc/>
        public override void Invoke(Action action, CancellationToken cancellationToken = default)
        {
            var attempts    = 0;
            var sysDeadline = base.SysDeadline();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    action();
                    return;
                }
                catch (Exception e)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var adjustedDelay = AdjustDelay(RetryInterval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(adjustedDelay);
                }
            }
        }

        /// <inheritdoc/>
        public override TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default)
        {
            var attempts    = 0;
            var sysDeadline = base.SysDeadline();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return action();
                }
                catch (Exception e)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var adjustedDelay = AdjustDelay(RetryInterval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(RetryInterval);
                }
            }
        }
    }
}
