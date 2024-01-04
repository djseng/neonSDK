//-----------------------------------------------------------------------------
// FILE:        SyncSubscription.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:   Copyright © 2005-2024 by NEONFORGE LLC.  All rights reserved.
// COPYRIGHT:   Copyright (c) 2015-2018 The NATS Authors (method comments)
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
using System.Threading;
using System.Threading.Tasks;

using NATS.Client;
using STAN.Client;

using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.Net;
using Neon.Tasks;

namespace NATS.Client
{
    /// <summary>
    /// Implements an <see cref="ISyncSubscription"/> for typed messages.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    public sealed class SyncSubscription<TMessage> : ISyncSubscription<TMessage>
        where TMessage : class, IRoundtripData, new()
    {
        private ISyncSubscription subscription;

        /// <summary>
        /// Constructs a typed synchronous subscription wrapping a lower level subscription.
        /// </summary>
        /// <param name="subscription">The underlying non-generic subscription returned by NATS.</param>
        internal SyncSubscription(ISyncSubscription subscription)
        {
            Covenant.Requires<ArgumentNullException>(subscription != null, nameof(subscription));

            this.subscription = subscription;
        }

        /// <inheritdoc/>
        public long Sid => subscription.Sid;

        /// <inheritdoc/>
        public string Subject => subscription.Subject;

        /// <inheritdoc/>
        public string Queue => subscription.Queue;

        /// <inheritdoc/>
        public Connection Connection => subscription.Connection;

        /// <inheritdoc/>
        public bool IsValid => subscription.IsValid;

        /// <inheritdoc/>
        public int QueuedMessageCount => subscription.QueuedMessageCount;

        /// <inheritdoc/>
        public long PendingByteLimit
        {
            get => subscription.PendingByteLimit;
            set => subscription.PendingByteLimit = value;
        }

        /// <inheritdoc/>
        public long PendingMessageLimit
        {
            get => subscription.PendingMessageLimit;
            set => subscription.PendingMessageLimit = value;
        }

        /// <inheritdoc/>
        public long PendingBytes => subscription.PendingBytes;

        /// <inheritdoc/>
        public long PendingMessages => subscription.PendingMessages;

        /// <inheritdoc/>
        public long MaxPendingBytes => subscription.MaxPendingBytes;

        /// <inheritdoc/>
        public long MaxPendingMessages => subscription.MaxPendingMessages;

        /// <inheritdoc/>
        public long Delivered => subscription.Delivered;

        /// <inheritdoc/>
        public long Dropped => subscription.Dropped;

        /// <inheritdoc/>
        public void AutoUnsubscribe(int max)
        {
            subscription.AutoUnsubscribe(max);
        }

        /// <inheritdoc/>
        public void ClearMaxPending()
        {
            subscription.ClearMaxPending();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            subscription.Dispose();
        }

        /// <inheritdoc/>
        public void GetMaxPending(out long maxPendingBytes, out long maxPendingMessages)
        {
            subscription.GetMaxPending(out maxPendingBytes, out maxPendingMessages);
        }

        /// <inheritdoc/>
        public void GetPending(out long pendingBytes, out long pendingMessages)
        {
            subscription.GetMaxPending(out pendingBytes, out pendingMessages);
        }

        /// <inheritdoc/>
        public void SetPendingLimits(long messageLimit, long bytesLimit)
        {
            subscription.SetPendingLimits(messageLimit, bytesLimit);
        }

        /// <inheritdoc/>
        public void Unsubscribe()
        {
            subscription.Unsubscribe();
        }

        /// <inheritdoc/>
        public Msg<TMessage> NextMessage()
        {
            return new Msg<TMessage>(subscription.NextMessage());
        }

        /// <inheritdoc/>
        public Msg<TMessage> NextMessage(int timeout)
        {
            return new Msg<TMessage>(subscription.NextMessage(timeout));
        }

        /// <inheritdoc/>
        public void Drain()
        {
            subscription.Drain();
        }

        /// <inheritdoc/>
        public void Drain(int timeout)
        {
            subscription.Drain(timeout);
        }

        /// <inheritdoc/>
        public async Task DrainAsync()
        {
            await SyncContext.Clear;
            await subscription.DrainAsync();
        }

        /// <inheritdoc/>
        public async Task DrainAsync(int timeout)
        {
            await SyncContext.Clear;
            await subscription.DrainAsync(timeout);
        }
    }
}
