// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Narrow owned transport and lifecycle contracts for one U2R2 session.

using System;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal interface IRos2BridgeSessionTransport : IDisposable
    {
        bool IsConnected { get; }

        void BeginV2(
            U2R2ProtocolLimits limits,
            int timeoutMs);

        void WriteV2(
            ReadOnlyMemory<byte> wireBytes,
            U2R2ProtocolLimits limits,
            int timeoutMs);

        byte[] ReadV2(
            U2R2ProtocolLimits limits,
            int timeoutMs);

        void Close();
    }

    internal interface IRos2BridgeContractLease : IDisposable
    {
        Ros2BridgeSessionContract Contract { get; }

        long LeaseIdentity { get; }

        bool IsReleased { get; }
    }

    internal interface IRos2BridgeContractWireController
    {
        Ros2BridgeSessionResult Register(
            Ros2BridgeSessionContract contract);

        Ros2BridgeSessionResult Unregister(
            Ros2BridgeSessionContract contract);
    }

    internal interface IRos2BridgeInboundContractResolver
    {
        Ros2BridgeSessionResult TryResolveInbound(
            U2R2Message message,
            out Ros2BridgeSessionContract contract);
    }

    internal interface IRos2BridgeInboundFrameReceiver
    {
        // Ownership is transferred for every non-null frame, including when
        // admission is rejected.
        Ros2BridgeSessionResult TryAccept(
            Ros2BridgeInboundFrame frame);
    }

    internal enum Ros2BridgeSessionLifecycleState : byte
    {
        Stopped = 0,
        AwaitingHandshake = 1,
        Ready = 2,
        Stopping = 3,
        Faulted = 4,
    }

    internal enum Ros2BridgeSessionResultState : byte
    {
        Accepted = 1,
        Rejected = 2,
        Unavailable = 3,
        Faulted = 4,
    }

    internal readonly struct Ros2BridgeSessionResult
    {
        private const int MaxReasonChars = 512;

        internal Ros2BridgeSessionResult(
            Ros2BridgeSessionResultState state,
            string reason = "")
        {
            if (!Enum.IsDefined(
                    typeof(Ros2BridgeSessionResultState),
                    state))
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }

            State = state;
            Reason = Bound(reason);
        }

        internal Ros2BridgeSessionResultState State { get; }

        internal string Reason { get; }

        internal bool IsAccepted
            => State == Ros2BridgeSessionResultState.Accepted;

        internal static Ros2BridgeSessionResult Accepted()
            => new Ros2BridgeSessionResult(
                Ros2BridgeSessionResultState.Accepted);

        internal static Ros2BridgeSessionResult Reject(string reason)
            => new Ros2BridgeSessionResult(
                Ros2BridgeSessionResultState.Rejected,
                reason);

        internal static Ros2BridgeSessionResult Unavailable(
            string reason)
            => new Ros2BridgeSessionResult(
                Ros2BridgeSessionResultState.Unavailable,
                reason);

        internal static Ros2BridgeSessionResult Fault(string reason)
            => new Ros2BridgeSessionResult(
                Ros2BridgeSessionResultState.Faulted,
                reason);

        private static string Bound(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var normalized = value.Trim();
            return normalized.Length <= MaxReasonChars
                ? normalized
                : normalized.Substring(0, MaxReasonChars);
        }
    }
}
