// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove.Ros2Bridge/Protocol
// Purpose: Bounded U2R2 resource accounting and session lifecycle.

using System;
using System.Threading;

namespace Unity2Foxglove.Ros2Bridge.Protocol
{
    public static class U2R2CheckedArithmetic
    {
        public static ulong Add(
            ulong current,
            ulong increment,
            ulong limit,
            string budgetName)
        {
            if (current > limit
                || increment > limit - current)
            {
                throw new U2R2ProtocolException(
                    "capacity_exceeded",
                    "The U2R2 " + (budgetName ?? "budget") + " is exhausted.",
                    terminal: false);
            }
            return current + increment;
        }
    }

    public readonly struct U2R2FrameSize
    {
        private U2R2FrameSize(
            ulong headerBytes,
            ulong payloadBytes,
            ulong totalBytes)
        {
            HeaderBytes = headerBytes;
            PayloadBytes = payloadBytes;
            TotalBytes = totalBytes;
        }

        public ulong HeaderBytes { get; }
        public ulong PayloadBytes { get; }
        public ulong TotalBytes { get; }

        public static U2R2FrameSize Create(
            U2R2ProtocolLimits limits,
            ulong headerBytes,
            ulong payloadBytes)
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            if (headerBytes > limits.MaxHeaderBytes
                || payloadBytes > limits.MaxPayloadBytes)
            {
                throw new U2R2ProtocolException(
                    "capacity_exceeded",
                    "The U2R2 frame exceeds its header or payload budget.",
                    terminal: false);
            }
            var variable = U2R2CheckedArithmetic.Add(
                headerBytes,
                payloadBytes,
                ulong.MaxValue,
                "frame");
            var total = U2R2CheckedArithmetic.Add(
                limits.FixedFrameBytes,
                variable,
                ulong.MaxValue,
                "frame");
            return new U2R2FrameSize(headerBytes, payloadBytes, total);
        }
    }

    public sealed class U2R2CapacityCounter
    {
        private readonly object _gate = new();
        private readonly ulong _capacity;
        private ulong _count;

        public U2R2CapacityCounter(ulong capacity)
        {
            if (capacity == 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public ulong Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        public bool TryAcquire()
        {
            lock (_gate)
            {
                if (_count == _capacity)
                    return false;
                _count++;
                return true;
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                if (_count == 0)
                    throw new InvalidOperationException(
                        "The U2R2 capacity counter is already empty.");
                _count--;
            }
        }
    }

    public enum U2R2ConnectionRole
    {
        DataSession = 1,
        Probe = 2,
    }

    public sealed class U2R2ResourceLease : IDisposable
    {
        private U2R2SessionResourceAuthority _owner;
        private readonly U2R2ConnectionRole _role;

        internal U2R2ResourceLease(
            U2R2SessionResourceAuthority owner,
            U2R2ConnectionRole role)
        {
            _owner = owner;
            _role = role;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null)
                return;
            owner.Release(_role);
        }
    }

    public sealed class U2R2SessionResourceAuthority
    {
        private readonly object _gate = new();
        private readonly U2R2ProtocolLimits _limits;
        private ulong _connections;
        private ulong _dataSessions;
        private ulong _probes;

        public U2R2SessionResourceAuthority(U2R2ProtocolLimits limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        public ulong ConnectionCount
        {
            get
            {
                lock (_gate)
                    return _connections;
            }
        }

        public bool TryAcquire(
            U2R2ConnectionRole role,
            out U2R2ResourceLease lease)
        {
            lock (_gate)
            {
                lease = null;
                if (_connections == _limits.MaxConnections)
                    return false;
                switch (role)
                {
                    case U2R2ConnectionRole.DataSession:
                        if (_dataSessions == _limits.MaxDataSessions)
                            return false;
                        _dataSessions++;
                        break;
                    case U2R2ConnectionRole.Probe:
                        if (_probes == _limits.MaxProbes)
                            return false;
                        _probes++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(role));
                }
                _connections++;
                lease = new U2R2ResourceLease(this, role);
                return true;
            }
        }

        internal void Release(U2R2ConnectionRole role)
        {
            lock (_gate)
            {
                if (_connections == 0)
                    throw new InvalidOperationException("No U2R2 connection lease is active.");
                if (role == U2R2ConnectionRole.DataSession)
                {
                    if (_dataSessions == 0)
                        throw new InvalidOperationException("No U2R2 data lease is active.");
                    _dataSessions--;
                }
                else if (role == U2R2ConnectionRole.Probe)
                {
                    if (_probes == 0)
                        throw new InvalidOperationException("No U2R2 probe lease is active.");
                    _probes--;
                }
                else
                {
                    throw new ArgumentOutOfRangeException(nameof(role));
                }
                _connections--;
            }
        }
    }

    public sealed class U2R2RequestIdCounter
    {
        private readonly object _gate = new object();
        private ulong _current;
        private bool _isFaulted;

        public U2R2RequestIdCounter(ulong current = 0)
        {
            _current = current;
        }

        public bool IsFaulted
        {
            get
            {
                lock (_gate)
                    return _isFaulted;
            }
        }

        public ulong Next()
        {
            lock (_gate)
            {
                if (_isFaulted || _current >= ulong.MaxValue - 1)
                {
                    _isFaulted = true;
                    throw new U2R2ProtocolException(
                        "request_id_exhausted",
                        "The U2R2 request ID counter is exhausted.",
                        terminal: true);
                }
                _current++;
                return _current;
            }
        }
    }

    public enum U2R2PureSessionState
    {
        Active = 1,
        Closed = 2,
    }

    public enum U2R2TimeoutKind
    {
        Handshake = 1,
        PartialFrame = 2,
        Read = 3,
        Write = 4,
        Join = 5,
        Shutdown = 6,
    }

    public sealed class U2R2PureSessionLifecycle
    {
        private readonly U2R2ProtocolLimits _limits;

        public U2R2PureSessionLifecycle(U2R2ProtocolLimits limits = null)
        {
            _limits = limits;
        }

        public U2R2PureSessionState State { get; private set; }
            = U2R2PureSessionState.Active;

        public ulong LimitFor(U2R2TimeoutKind kind)
        {
            if (_limits == null)
                throw new InvalidOperationException("This lifecycle has no limit snapshot.");
            switch (kind)
            {
                case U2R2TimeoutKind.Handshake:
                    return _limits.HandshakeTimeoutMs;
                case U2R2TimeoutKind.PartialFrame:
                    return _limits.PartialFrameTimeoutMs;
                case U2R2TimeoutKind.Read:
                    return _limits.ReadTimeoutMs;
                case U2R2TimeoutKind.Write:
                    return _limits.WriteTimeoutMs;
                case U2R2TimeoutKind.Join:
                    return _limits.JoinTimeoutMs;
                case U2R2TimeoutKind.Shutdown:
                    return _limits.ShutdownTimeoutMs;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public void PeerClosed()
        {
            State = U2R2PureSessionState.Closed;
            throw new U2R2ProtocolException(
                "peer_closed",
                "The U2R2 peer closed the session.",
                terminal: true);
        }

        public void Timeout(U2R2TimeoutKind kind, ulong elapsedMs)
        {
            var limit = LimitFor(kind);
            if (!HasTimedOut(kind, elapsedMs))
                return;
            State = U2R2PureSessionState.Closed;
            throw new U2R2ProtocolException(
                "timeout",
                "The U2R2 session exceeded the " + kind + " timeout.",
                terminal: true);
        }

        public bool HasTimedOut(U2R2TimeoutKind kind, ulong elapsedMs)
            => elapsedMs >= LimitFor(kind);
    }
}
