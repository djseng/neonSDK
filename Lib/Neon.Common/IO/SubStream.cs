//-----------------------------------------------------------------------------
// FILE:        SubStream.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Tasks;

namespace Neon.IO
{
    /// <summary>
    /// <para>
    /// Implements a <see cref="Stream"/> that operates on a section of a base stream.
    /// The base stream must be able to <see cref="Stream.CanSeek"/>.
    /// </para>
    /// <note>
    /// Thread safety: multiple <see cref="SubStream"/> instances can perform operations on the
    /// same underlying stream on differents because substreams obtain a lock on the base stream
    /// before actually performing any I/O operations.
    /// </note>
    /// <note>
    /// <b>WARNING:</b> Avoid performing operations on the underlying stream and any substreams
    /// within different threads.  This will likey result in unexpected data corruption.
    /// </note>
    /// </summary>
    /// <remarks>
    /// <note>
    /// This class doesn't currently implement all of the <see cref="Stream"/> methods, especially 
    /// asynchronous methods.  This is something we may add in the future.
    /// </note>
    /// </remarks>
    public class SubStream : Stream
    {
        private Stream      baseStream;
        private long        start;          // Starting position of the substream data within the base stream
        private long        length;         // Length of the subsection
        private long        position;       // Current position relative to the substream data
        private long        orgBasePos;     // Original base position (restored when this is disposed)

        /// <summary>
        /// Constructs a substream that operates on a range of bytes within a base stream.
        /// </summary>
        /// <param name="baseStream">The base stream.</param>
        /// <param name="start">The zero-based index of the first byte within the base stream to be managed by the substream.</param>
        /// <param name="length">The number of bytes to be managed.</param>
        /// <exception cref="IOException">Thrown if the base stream doesn't support seek or the starting position or length is invalid.</exception>
        public SubStream(Stream baseStream, long start, long length)
        {
            Covenant.Requires<ArgumentNullException>(baseStream != null, nameof(baseStream));
            Covenant.Requires<IOException>(baseStream.CanSeek, nameof(baseStream), "Base stream must support seek.");
            Covenant.Requires<IOException>(start >= 0, () => nameof(start), () => $"[start={start}] must be >= 0");
            Covenant.Requires<IOException>(start <= baseStream.Length, () => nameof(start), () => $"[start={start}] is beyond the base stream's EOF.");
            Covenant.Requires<IOException>(length >= 0, () => nameof(length), () => $"[start={length}] must be >= 0");
            Covenant.Requires<IOException>(start + length <= baseStream.Length, () => nameof(length), () => $"[start+length={start + length}] is beyond the base stream's EOF.");

            this.baseStream       = baseStream;
            this.start        = start;
            this.length       = length;
            this.position     = 0;
            this.orgBasePos = baseStream.Position;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            baseStream.Position = orgBasePos;

            base.Dispose(disposing);
        }

        /// <summary>
        /// Ensures that the starting and optional ending position for a stream operation are 
        /// constrained to the substream's data.
        /// </summary>
        /// <param name="operation">Identifies the operation.</param>
        /// <param name="startPos">The logical starting position.</param>
        /// <param name="endPos">Optionally specifies the logical ending position.</param>
        /// <exception cref="IOException">Thrown when the start/end positions are outside of the substream data.</exception>
        private void EnsurePosition(string operation, long startPos, long endPos = -1)
        {
            if (startPos < 0 || startPos > length)
            {
                throw new IOException($"{operation}: Invalid operation starting position: {startPos}");
            }
            else if (endPos != -1 && (endPos < 0 || endPos > this.length))
            {
                throw new IOException($"{operation}: Invalid operation ending position: {endPos}");
            }
        }

        /// <summary>
        /// Saves the base stream position, sets the base position to match the
        /// virtual substream position, executes the operation and then restores the
        /// base stream position.
        /// </summary>
        /// <param name="operation">The operation to be performed.</param>
        private void Execute(Action operation)
        {
            lock (baseStream)
            {
                var basePos = baseStream.Position;

                try
                {
                    baseStream.Position = start + position;

                    operation();
                }
                finally
                {
                    baseStream.Position = basePos;
                }
            }
        }

        /// <summary>
        /// Saves the base stream position, sets the base position to match the
        /// virtual substream position, executes the operation and then restores the
        /// base stream position.
        /// </summary>
        /// <param name="operation">The operation to be performed.</param>
        /// <returns>The operation result.</returns>
        private int ExecuteInt(Func<int> operation)
        {
            lock (baseStream)
            {
                var basePos = baseStream.Position;

                try
                {
                    baseStream.Position = start + position;

                    return operation();
                }
                finally
                {
                    baseStream.Position = basePos;
                }
            }
        }

        /// <inheritdoc/>
        public override bool CanRead => baseStream.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => baseStream.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => baseStream.CanWrite;

        /// <inheritdoc/>
        public override long Length => this.length;

        /// <inheritdoc/>
        public override long Position
        {
            get => this.position;

            set
            {
                EnsurePosition("SEEK", value);
                this.position = value;
            }
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            baseStream.Flush();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ExecuteInt(
                () =>
                {
                    var cb = Math.Min(count, (int)(length - Position));

                    baseStream.Read(buffer, offset, cb);
                    Position += cb;

                    return cb;
                });
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:

                    return Position = offset;

                case SeekOrigin.Current:

                    return Position = Position + offset;

                case SeekOrigin.End:

                    return Position = length + offset;

                default:

                    throw new NotImplementedException();
            }
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsurePosition("WRITE", Position, Position + count);

            Execute(
                () =>
                {
                    baseStream.Write(buffer, offset, count);
                    Position += count;
                });
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            if (position >= length)
            {
                return -1;  // EOF
            }

            return ExecuteInt(
                () =>
                {
                    var result = baseStream.ReadByte();

                    Position += 1;

                    return result;
                });
        }


        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            EnsurePosition("WRITE-BYTE", Position);

            Execute(
                () =>
                {
                    baseStream.WriteByte(value);
                    Position += 1;
                });
        }

        //---------------------------------------------------------------------
        // Async methods:
        
        // $todo(jefflill): We're actually going to implement these synchronously for now.

        /// <inheritdoc/>
        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await SyncContext.Clear;

            CopyTo(destination, bufferSize);
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SyncContext.Clear;

            Flush();
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await SyncContext.Clear;

            return await Task.FromResult(Read(buffer, offset, count));
        }

#if !NETFRAMEWORK && !NETSTANDARD2_0

        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Write(buffer.ToArray(), 0, buffer.Length);
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken);
        }

#endif

        //---------------------------------------------------------------------
        // These methods are not implemented:

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="asyncResult"></param>
        /// <returns></returns>
        public override int EndRead(IAsyncResult asyncResult)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="asyncResult"></param>
        public override void EndWrite(IAsyncResult asyncResult)
        {
            throw new NotImplementedException();
        }

#if NETCOREAPP3_1_OR_GREATER || NET5_0_OR_GREATER

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        public override int Read(Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// <b>Not Implemented</b>
        /// </summary>
        /// <returns></returns>
        public override ValueTask DisposeAsync()
        {
            throw new NotImplementedException();
        }
#endif
    }
}
