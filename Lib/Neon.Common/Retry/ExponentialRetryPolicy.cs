//-----------------------------------------------------------------------------
// FILE:        ExponentialRetryPolicy.cs
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
    /// Implements an <see cref="IRetryPolicy"/> that retries an operation 
    /// first at an initial interval and then doubles the interval up to a limit
    /// for a specified maximum number of times.
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
    public class ExponentialRetryPolicy : RetryPolicyBase, IRetryPolicy
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
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
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
        public ExponentialRetryPolicy(
            Func<Exception, bool>   transientDetector    = null, 
            int                     maxAttempts          = -1, 
            TimeSpan?               initialRetryInterval = null, 
            TimeSpan?               maxRetryInterval     = null, 
            TimeSpan?               timeout              = null, 
            string                  categoryName         = DefaultCategoryName)

            : base(categoryName, timeout)
        {
            Covenant.Requires<ArgumentException>(initialRetryInterval == null || initialRetryInterval > TimeSpan.Zero, nameof(initialRetryInterval));

            this.transientDetector    = transientDetector ?? (e => true);
            this.InitialRetryInterval = initialRetryInterval ?? TimeSpan.FromSeconds(1);
            this.MaxRetryInterval     = maxRetryInterval ?? TimeSpan.FromHours(24);

            if (maxAttempts < 0)
            {
                MaxAttempts = timeout == null ? RetryPolicy.DefaultMaxAttempts : int.MaxValue;
            }
            else
            {
                MaxAttempts = maxAttempts;
            }

            if (InitialRetryInterval > MaxRetryInterval)
            {
                InitialRetryInterval = MaxRetryInterval;
            }
        }

        /// <summary>
        /// Constructs the retry policy to handle a specific exception type as transient.
        /// </summary>
        /// <param name="exceptionType">Specifies the exception type to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
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
        public ExponentialRetryPolicy(
            Type        exceptionType, 
            int         maxAttempts          = -1, 
            TimeSpan?   initialRetryInterval = null, 
            TimeSpan?   maxRetryInterval     = null, 
            TimeSpan?   timeout              = null, 
            string      categoryName         = null)

            : this
            (
                e => TransientDetector.MatchException(e, exceptionType),
                maxAttempts,
                initialRetryInterval,
                maxRetryInterval,
                timeout,
                categoryName
            )
        {
            Covenant.Requires<ArgumentNullException>(exceptionType != null, nameof(exceptionType));
        }

        /// <summary>
        /// Constructs the retry policy to handle a multiple exception types as transient.
        /// </summary>
        /// <param name="exceptionTypes">Specifies the exception type to be considered to be transient.</param>
        /// <param name="maxAttempts">Optionally specifies the maximum number of times an action should be retried (defaults to <b>5</b>).</param>
        /// <param name="initialRetryInterval">Optionally specifies the initial retry interval between retry attempts (defaults to <b>1 second</b>).</param>
        /// <param name="maxRetryInterval">Optionally specifies the maximum retry interval (defaults to essentially unlimited: 24 hours).</param>
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
        public ExponentialRetryPolicy(
            Type[]      exceptionTypes, 
            int         maxAttempts          = -1, 
            TimeSpan?   initialRetryInterval = null, 
            TimeSpan?   maxRetryInterval     = null, 
            TimeSpan?   timeout              = null, 
            string      categoryName         = null)

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
                initialRetryInterval,
                maxRetryInterval,
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
        /// Returns the initial interval between action retry attempts.
        /// </summary>
        public TimeSpan InitialRetryInterval { get; private set; }

        /// <summary>
        /// Returns the maximum intervaL between action retry attempts. 
        /// </summary>
        public TimeSpan MaxRetryInterval { get; private set; }

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
                return new ExponentialRetryPolicy(transientDetector ?? this.transientDetector, MaxAttempts, InitialRetryInterval, MaxRetryInterval, Timeout, CategoryName);
            }
        }

        /// <inheritdoc/>
        public override async Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            await SyncContext.Clear;

            var attempts    = 0;
            var sysDeadline = base.SysDeadline();
            var interval    = InitialRetryInterval;

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

                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay, cancellationToken);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override async Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken = default)
        {
            await SyncContext.Clear;

            var attempts    = 0;
            var sysDeadline = base.SysDeadline();
            var interval    = InitialRetryInterval;

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

                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    await Task.Delay(adjustedDelay, cancellationToken);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void Invoke(Action action, CancellationToken cancellationToken = default)
        {
            var attempts    = 0;
            var sysDeadline = base.SysDeadline();
            var interval    = InitialRetryInterval;

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
                    
                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override TResult Invoke<TResult>(Func<TResult> action, CancellationToken cancellationToken = default)
        {
            var attempts    = 0;
            var sysDeadline = base.SysDeadline();
            var interval    = InitialRetryInterval;

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

                    var adjustedDelay = AdjustDelay(interval, sysDeadline);

                    if (++attempts >= MaxAttempts || adjustedDelay <= TimeSpan.Zero || !transientDetector(e))
                    {
                        throw;
                    }

                    LogTransient(e);
                    Thread.Sleep(adjustedDelay);

                    interval = TimeSpan.FromTicks(interval.Ticks * 2);

                    if (interval > MaxRetryInterval)
                    {
                        interval = MaxRetryInterval;
                    }
                }
            }
        }
    }
}
