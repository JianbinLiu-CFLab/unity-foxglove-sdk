// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Strict RFC 6455 frame validation and restart-generation ownership.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Transport
{


public sealed partial class ManagedWebSocketStrictnessTests
    {
        private sealed class BlockingRejectBackend : ManagedWsBackend
        {
            internal readonly ManualResetEventSlim RejectionEntered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim ReleaseRejection = new ManualResetEventSlim();

            internal BlockingRejectBackend(ManagedWebSocketOptions options)
                : base(options) { }

            protected override void RejectPendingClient(TcpClient tcpClient)
            {
                RejectionEntered.Set();
                ReleaseRejection.Wait(TimeSpan.FromSeconds(5));
                base.RejectPendingClient(tcpClient);
            }

            public override void Dispose()
            {
                ReleaseRejection.Set();
                base.Dispose();
                RejectionEntered.Dispose();
                ReleaseRejection.Dispose();
            }
        }

        private sealed class ProbeWssBackend : ManagedWssBackend
        {
            internal ProbeWssBackend()
                : base(new FoxgloveTlsOptions()) { }

            internal bool PlaintextCapacityResponseSupported
                => SupportsPlaintextCapacityResponse;
        }

        private sealed class BlockingHandshakeBackend : ManagedWsBackend
        {
            internal readonly BlockingHandshakeStream HandshakeStream = new BlockingHandshakeStream();

            protected override Stream CreateClientStream(TcpClient tcpClient)
                => HandshakeStream;
        }

        private sealed class BlockingHandshakeStream : Stream
        {
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();

            internal bool IsDisposed { get; private set; }

            internal void Release() => _release.Set();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override bool CanTimeout => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _release.Wait(TimeSpan.FromSeconds(30));
                return 0;
            }

            public override int ReadByte()
            {
                _release.Wait(TimeSpan.FromSeconds(30));
                return -1;
            }

            public override void Write(byte[] buffer, int offset, int count) { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                _release.Set();
                _release.Dispose();
                base.Dispose(disposing);
            }
        }

        private static void InvokeReceiveLoop(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "ReceiveLoop",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection, CancellationToken.None });
        }

        private static void InvokeHandleClient(
            ManagedWsBackend backend,
            TcpClient tcpClient,
            CancellationToken cancellationToken)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "HandleClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { tcpClient, cancellationToken });
        }

        private static void InvokeDisconnect(
            ManagedWsBackend backend,
            uint clientId,
            WsConnection connection)
        {
            var method = typeof(ManagedWsBackend).GetMethod(
                "DisconnectClient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method.Invoke(backend, new object[] { clientId, connection, false });
        }

        private static byte[] BuildMaskedFrame(byte opcode, byte[] payload, bool fin = true)
        {
            payload ??= Array.Empty<byte>();
            if (payload.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var extendedLengthBytes = payload.Length <= 125 ? 0 : 2;
            var frame = new byte[2 + extendedLengthBytes + 4 + payload.Length];
            frame[0] = (byte)((fin ? 0x80 : 0x00) | opcode);
            if (extendedLengthBytes == 0)
            {
                frame[1] = (byte)(0x80 | payload.Length);
            }
            else
            {
                frame[1] = 0xFE;
                frame[2] = (byte)(payload.Length >> 8);
                frame[3] = (byte)payload.Length;
            }
            WriteMaskedPayload(frame, 2 + extendedLengthBytes, payload);
            return frame;
        }

        private static byte[] JoinFrames(IReadOnlyCollection<byte[]> frames)
        {
            var totalLength = 0;
            foreach (var frame in frames)
                totalLength = checked(totalLength + frame.Length);

            var joined = new byte[totalLength];
            var offset = 0;
            foreach (var frame in frames)
            {
                Buffer.BlockCopy(frame, 0, joined, offset, frame.Length);
                offset += frame.Length;
            }
            return joined;
        }

        private static byte[] BuildNonMinimalMaskedFrame(byte opcode, byte marker, ulong payloadLength)
        {
            var extendedLengthBytes = marker == 126 ? 2 : 8;
            var payload = new byte[checked((int)payloadLength)];
            var frame = new byte[2 + extendedLengthBytes + 4 + payload.Length];
            frame[0] = (byte)(0x80 | opcode);
            frame[1] = (byte)(0x80 | marker);
            for (var i = 0; i < extendedLengthBytes; i++)
            {
                var shift = (extendedLengthBytes - i - 1) * 8;
                frame[2 + i] = (byte)(payloadLength >> shift);
            }

            WriteMaskedPayload(frame, 2 + extendedLengthBytes, payload);
            return frame;
        }

        private static void WriteMaskedPayload(byte[] frame, int maskOffset, byte[] payload)
        {
            var mask = new byte[] { 0x12, 0x34, 0x56, 0x78 };
            Buffer.BlockCopy(mask, 0, frame, maskOffset, mask.Length);
            for (var i = 0; i < payload.Length; i++)
                frame[maskOffset + mask.Length + i] = (byte)(payload[i] ^ mask[i % mask.Length]);
        }

        private sealed class DuplexBufferStream : Stream
        {
            private readonly MemoryStream _input;
            private readonly MemoryStream _output = new MemoryStream();
            private readonly object _outputLock = new object();

            public DuplexBufferStream(byte[] input)
            {
                _input = new MemoryStream(input ?? Array.Empty<byte>(), writable: false);
            }

            public byte[] WrittenBytes
            {
                get
                {
                    lock (_outputLock)
                    {
                        return _output.ToArray();
                    }
                }
            }

            public bool IsDisposed { get; private set; }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
                => _input.Read(buffer, offset, count);

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_outputLock)
                {
                    _output.Write(buffer, offset, count);
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
