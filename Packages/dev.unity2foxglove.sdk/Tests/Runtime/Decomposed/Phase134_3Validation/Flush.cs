// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 134-3 recording shutdown and replay callback hardening.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_3Validation.
    /// </summary>
    public static partial class Phase134_3Validation
    {
        private sealed class ThrowingStream : Stream
        {
            private readonly MemoryStream _inner = new();

            public bool ThrowOnWrite { get; set; }
            public bool ThrowOnFlush { get; set; }

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            /// <summary>
            /// Validation method for Flush.
            /// </summary>
            public override void Flush()
            {
                if (ThrowOnFlush)
                    throw new IOException("phase134 flush failure");
                _inner.Flush();
            }

            /// <summary>
            /// Validation method for Read.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override int Read(byte[] buffer, int offset, int count)
                => _inner.Read(buffer, offset, count);

            /// <summary>
            /// Validation method for Seek.
            /// </summary>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="origin">Reference point for the seek operation.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override long Seek(long offset, SeekOrigin origin)
                => _inner.Seek(offset, origin);

            /// <summary>
            /// Validation method for SetLength.
            /// </summary>
            /// <param name="value">Requested value for the stream operation.</param>
            public override void SetLength(long value)
                => _inner.SetLength(value);

            /// <summary>
            /// Validation method for Write.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase134 write failure");
                _inner.Write(buffer, offset, count);
            }

            /// <summary>
            /// Validation method for Write.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (ThrowOnWrite)
                    throw new IOException("phase134 write failure");
                _inner.Write(buffer);
            }
        }

        private sealed class Phase134_3Clock : IFoxgloveClock
        {
            public ulong NowNs { get; private set; }
            /// <summary>
            /// Validation method for AdvanceNs.
            /// </summary>
            /// <param name="deltaNs">Nanoseconds to advance the fake clock.</param>
            public void AdvanceNs(ulong deltaNs) => NowNs += deltaNs;
        }

        private sealed class Phase134_3Transport : IFoxgloveTransport, IReplayResettableFoxgloveTransport, IClientDataQueueResettableFoxgloveTransport, IFoxgloveTransportStatsProvider
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            /// <summary>
            /// Validation method for Start.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            public void Start(string host, int port) => IsRunning = true;
            /// <summary>
            /// Validation method for Stop.
            /// </summary>
            public void Stop() => IsRunning = false;
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() => Stop();
            /// <summary>
            /// Validation method for BroadcastText.
            /// </summary>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void BroadcastText(string json) { }
            /// <summary>
            /// Validation method for BroadcastBinary.
            /// </summary>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void BroadcastBinary(byte[] data) { }
            /// <summary>
            /// Validation method for SendText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SendText(uint clientId, string json) { }
            /// <summary>
            /// Validation method for SendBinary.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void SendBinary(uint clientId, byte[] data) { }
            /// <summary>
            /// Validation method for ClearDataQueues.
            /// </summary>
            public void ClearDataQueues() { }
            /// <summary>
            /// Validation method for ClearDataQueue.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            public void ClearDataQueue(uint clientId) { }

            /// <summary>
            /// Validation method for GetStatsSnapshot.
            /// </summary>
            /// <returns>The value produced by the validation helper.</returns>
            public TransportStatsSnapshot GetStatsSnapshot()
            {
                return new TransportStatsSnapshot
                {
                    Supported = true,
                    IsRunning = IsRunning,
                    ActiveClientCount = 0,
                    Clients = Array.Empty<TransportClientStats>()
                };
            }
        }
    }
}
