// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Exact-slice ownership for one inbound encapsulated-CDR message.

using System;
using System.Buffers;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge
{
    internal interface IRos2BridgeBytePool
    {
        byte[] Rent(int minimumLength);

        void Return(byte[] storage);
    }

    internal sealed class Ros2BridgeSharedBytePool :
        IRos2BridgeBytePool
    {
        internal static readonly Ros2BridgeSharedBytePool Instance =
            new Ros2BridgeSharedBytePool();

        private Ros2BridgeSharedBytePool()
        {
        }

        public byte[] Rent(int minimumLength)
            => ArrayPool<byte>.Shared.Rent(minimumLength);

        public void Return(byte[] storage)
            => ArrayPool<byte>.Shared.Return(
                storage,
                clearArray: false);
    }

    internal sealed class Ros2BridgeInboundFrame : IDisposable
    {
        private byte[] _storage;
        private Action<byte[]> _release;
        private readonly int _payloadOffset;
        private readonly int _payloadLength;

        private Ros2BridgeInboundFrame(
            Ros2BridgeSessionContract contract,
            string sessionId,
            ulong connectionGeneration,
            ulong messageId,
            ulong sequence,
            ulong receiveTimeNs,
            byte[] storage,
            int payloadOffset,
            int payloadLength,
            Action<byte[]> release)
        {
            Contract = contract
                ?? throw new ArgumentNullException(nameof(contract));
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException(
                    "An inbound frame requires a session ID.",
                    nameof(sessionId));
            if (connectionGeneration == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionGeneration));
            }
            if (messageId == 0)
                throw new ArgumentOutOfRangeException(nameof(messageId));
            if (sequence == 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            if (payloadOffset < 0
                || payloadLength < 0
                || payloadOffset > storage.Length - payloadLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(payloadLength),
                    "The inbound payload slice is outside its owned storage.");
            }

            SessionId = sessionId;
            ConnectionGeneration = connectionGeneration;
            MessageId = messageId;
            Sequence = sequence;
            ReceiveTimeNs = receiveTimeNs;
            _storage = storage;
            _payloadOffset = payloadOffset;
            _payloadLength = payloadLength;
            _release = release
                ?? throw new ArgumentNullException(nameof(release));
        }

        internal Ros2BridgeSessionContract Contract { get; }

        internal string SessionId { get; }

        internal ulong ConnectionGeneration { get; }

        internal ulong MessageId { get; }

        internal ulong Sequence { get; }

        internal ulong ReceiveTimeNs { get; }

        internal int PayloadLength => _payloadLength;

        internal ReadOnlyMemory<byte> Payload
        {
            get
            {
                var storage = Volatile.Read(ref _storage);
                if (storage == null)
                {
                    throw new ObjectDisposedException(
                        nameof(Ros2BridgeInboundFrame));
                }
                return new ReadOnlyMemory<byte>(
                    storage,
                    _payloadOffset,
                    _payloadLength);
            }
        }

        internal static Ros2BridgeInboundFrame CreateOwned(
            Ros2BridgeSessionContract contract,
            string sessionId,
            ulong connectionGeneration,
            ulong messageId,
            ulong sequence,
            ulong receiveTimeNs,
            byte[] storage,
            int payloadOffset,
            int payloadLength,
            Action<byte[]> release)
            => new Ros2BridgeInboundFrame(
                contract,
                sessionId,
                connectionGeneration,
                messageId,
                sequence,
                receiveTimeNs,
                storage,
                payloadOffset,
                payloadLength,
                release);

        internal static Ros2BridgeInboundFrame CopyOwned(
            Ros2BridgeSessionContract contract,
            string sessionId,
            ulong connectionGeneration,
            ulong messageId,
            ulong sequence,
            ulong receiveTimeNs,
            ReadOnlyMemory<byte> payload,
            IRos2BridgeBytePool pool = null)
        {
            pool ??= Ros2BridgeSharedBytePool.Instance;
            var storage = pool.Rent(payload.Length);
            if (storage == null || storage.Length < payload.Length)
            {
                if (storage != null)
                    pool.Return(storage);
                throw new InvalidOperationException(
                    "The Bridge byte pool returned insufficient storage.");
            }

            try
            {
                payload.Span.CopyTo(
                    storage.AsSpan(0, payload.Length));
                return CreateOwned(
                    contract,
                    sessionId,
                    connectionGeneration,
                    messageId,
                    sequence,
                    receiveTimeNs,
                    storage,
                    payloadOffset: 0,
                    payloadLength: payload.Length,
                    release: pool.Return);
            }
            catch
            {
                pool.Return(storage);
                throw;
            }
        }

        public void Dispose()
        {
            var storage = Interlocked.Exchange(
                ref _storage,
                null);
            if (storage == null)
                return;
            var release = Interlocked.Exchange(
                ref _release,
                null);
            release?.Invoke(storage);
        }
    }
}
