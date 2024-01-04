//-----------------------------------------------------------------------------
// FILE:        SocketMetrics.cs
// CONTRIBUTOR: Marcus Bowyer
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

using Prometheus;

using Yarp.Telemetry.Consumption;

namespace NeonSignalRProxy
{
    /// <inheritdoc/>
    public sealed class SocketMetrics : IMetricsConsumer<SocketsMetrics>
    {
        private static readonly Counter _outgoingConnectionsEstablished = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_outgoing_connections_established_total",
            "Number of outgoing (Connect) Socket connections established");


        private static readonly Counter _incomingConnectionsEstablished = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_incomming_connections_established_total",
            "Number of incoming (Accept) Socket connections established");

        private static readonly Counter _bytesReceived = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_bytes_recieved_total",
            "Number of bytes received");

        private static readonly Counter _bytesSent = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_bytes_sent_total",
            "Number of bytes sent");

        private static readonly Counter _datagramsReceived = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_datagrams_received_total",
            "Number of datagrams received");

        private static readonly Counter _datagramsSent = Metrics.CreateCounter(
            "neonsignalrproxy_sockets_datagrams_sent",
            "Number of datagrams sent");

        /// <inheritdoc/>
        public void OnMetrics(SocketsMetrics previous, SocketsMetrics current)
        {
            _outgoingConnectionsEstablished.IncTo(current.OutgoingConnectionsEstablished);
            _incomingConnectionsEstablished.IncTo(current.IncomingConnectionsEstablished);
            _bytesReceived.IncTo(current.BytesReceived);
            _bytesSent.IncTo(current.BytesSent);
            _datagramsReceived.IncTo(current.DatagramsReceived);
            _datagramsSent.IncTo(current.DatagramsSent);
        }
    }
}
