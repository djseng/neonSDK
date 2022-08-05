﻿//-----------------------------------------------------------------------------
// FILE:	    ActivityGetHeartbeatDetailsReply.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.ComponentModel;

using Neon.Common;
using Neon.Temporal;

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// <b>proxy --> client:</b> Answers a <see cref="ActivityGetHeartbeatDetailsRequest"/>
    /// </summary>
    [InternalProxyMessage(InternalMessageTypes.ActivityGetHeartbeatDetailsReply)]
    internal class ActivityGetHeartbeatDetailsReply : WorkflowReply
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ActivityGetHeartbeatDetailsReply()
        {
            Type = InternalMessageTypes.ActivityGetHeartbeatDetailsReply;
        }

        /// <summary>
        /// Returns the activity heartbeat details encoded as a byte array.
        /// </summary>
        public byte[] Details
        {
            get => GetBytesProperty(PropertyNames.Details);
            set => SetBytesProperty(PropertyNames.Details, value);
        }

        /// <inheritdoc/>
        internal override ProxyMessage Clone()
        {
            var clone = new ActivityGetHeartbeatDetailsReply();

            CopyTo(clone);

            return clone;
        }

        /// <inheritdoc/>
        protected override void CopyTo(ProxyMessage target)
        {
            base.CopyTo(target);

            var typedTarget = (ActivityGetHeartbeatDetailsReply)target;

            typedTarget.Details = this.Details;
        }
    }
}
