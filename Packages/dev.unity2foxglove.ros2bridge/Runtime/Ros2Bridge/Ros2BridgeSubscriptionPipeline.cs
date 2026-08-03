// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Optional bounded subscription state owned only by duplex runtimes.

using System;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed class Ros2BridgeSubscriptionPipeline :
        IRos2BridgeContractWireController,
        IDisposable
    {
        private readonly object _gate = new object();
        private readonly Ros2BridgeSessionState _state;
        private readonly Ros2BridgeInboundQueue _queue;
        private readonly Ros2BridgeContractLeaseRegistry _leases;

        private Ros2BridgeConnection _connection;
        private ulong _attemptGeneration;
        private string _sessionId = string.Empty;
        private ulong _connectionGeneration;
        private bool _disposed;

        internal Ros2BridgeSubscriptionPipeline(
            string host,
            int port,
            ulong generation,
            U2R2ProtocolLimits limits)
        {
            if (limits == null)
                throw new ArgumentNullException(nameof(limits));
            _state = new Ros2BridgeSessionState(
                new Ros2BridgeSessionSettings(
                    host,
                    port,
                    generation,
                    limits));
            _queue = new Ros2BridgeInboundQueue(
                new Ros2BridgeInboundQueueLimits(
                    checked((int)limits.MaxPayloadBytes),
                    checked((long)limits.MaxQueuedBytes),
                    checked((int)limits.MaxPerContractQueueDepth),
                    checked((long)limits.MaxPerContractQueueBytes)));
            _leases = new Ros2BridgeContractLeaseRegistry(
                generation,
                checked((int)limits.MaxContracts),
                _state,
                this);
        }

        internal IRos2BridgeInboundContractResolver Resolver => _state;

        internal IRos2BridgeInboundFrameReceiver Receiver => _queue;

        internal Ros2BridgeReconnectSnapshot BeginReconnect()
        {
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                ClearConnectionLocked();
            }
            _queue.Stop();
            return _state.BeginReconnect(_leases.CaptureSnapshot());
        }

        internal void CompleteReconnect(
            Ros2BridgeReconnectSnapshot reconnect,
            Ros2BridgeConnection connection,
            Ros2BridgeV2SessionSnapshot wireSession)
        {
            if (reconnect == null)
                throw new ArgumentNullException(nameof(reconnect));
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));
            if (wireSession == null)
                throw new ArgumentNullException(nameof(wireSession));
            if (!_state.TryCompleteHandshake(
                    reconnect.AttemptGeneration,
                    wireSession,
                    out var reason))
            {
                throw new InvalidOperationException(reason);
            }

            _queue.BeginSession(
                wireSession.SessionId,
                wireSession.ConnectionGeneration,
                reconnect.Contracts);
            var replay = _leases.ReplayCurrent(
                reconnect,
                connection);
            if (replay.Rejected != 0)
            {
                _queue.Stop();
                throw new InvalidOperationException(
                    "ROS2 Bridge rejected one or more subscription contracts during reconnect replay.");
            }

            lock (_gate)
            {
                ThrowIfDisposedLocked();
                _connection = connection;
                _attemptGeneration = reconnect.AttemptGeneration;
                _sessionId = wireSession.SessionId;
                _connectionGeneration =
                    wireSession.ConnectionGeneration;
            }
        }

        internal Ros2BridgeSessionResult TryAcquire(
            Ros2BridgeSessionContract contract,
            out IRos2BridgeContractLease lease)
        {
            lease = null;
            lock (_gate)
            {
                if (_disposed
                    || _connection == null
                    || _attemptGeneration == 0)
                {
                    return Ros2BridgeSessionResult.Unavailable(
                        "The ROS2 Bridge subscription session is not ready.");
                }
            }

            if (_leases.TryAcquire(
                    contract,
                    out lease,
                    out var reason))
            {
                return Ros2BridgeSessionResult.Accepted();
            }
            return reason.IndexOf(
                       "not ready",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || reason.IndexOf(
                       "unavailable",
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || reason.IndexOf(
                       "stopped",
                       StringComparison.OrdinalIgnoreCase) >= 0
                ? Ros2BridgeSessionResult.Unavailable(reason)
                : Ros2BridgeSessionResult.Reject(reason);
        }

        internal bool TryBeginApply(
            out Ros2BridgeInboundApplyLease lease)
            => _queue.TryBeginApply(out lease);

        internal Ros2BridgeInboundStatsSnapshot GetStatsSnapshot()
            => _queue.GetStatsSnapshot();

        internal void Disconnect()
        {
            lock (_gate)
                ClearConnectionLocked();
            _queue.Stop();
        }

        Ros2BridgeSessionResult IRos2BridgeContractWireController.Register(
            Ros2BridgeSessionContract contract)
        {
            Ros2BridgeConnection connection;
            ulong attempt;
            string sessionId;
            ulong connectionGeneration;
            lock (_gate)
            {
                connection = _connection;
                attempt = _attemptGeneration;
                sessionId = _sessionId;
                connectionGeneration = _connectionGeneration;
            }
            if (connection == null
                || attempt == 0
                || string.IsNullOrEmpty(sessionId)
                || connectionGeneration == 0)
            {
                return Ros2BridgeSessionResult.Unavailable(
                    "The ROS2 Bridge subscription session is not ready.");
            }
            if (!_queue.TryActivateContract(
                    contract,
                    sessionId,
                    connectionGeneration,
                    out var activationReason))
            {
                return Ros2BridgeSessionResult.Unavailable(
                    activationReason);
            }

            var result =
                ((IRos2BridgeContractWireController)connection)
                .Register(contract);
            if (!result.IsAccepted)
            {
                _queue.TryRevokeContract(contract, out _);
                return result;
            }
            if (_state.TryMarkSubscriptionReady(
                    attempt,
                    contract,
                    out var readyReason))
            {
                return result;
            }

            ((IRos2BridgeContractWireController)connection)
                .Unregister(contract);
            _queue.TryRevokeContract(contract, out _);
            return Ros2BridgeSessionResult.Fault(readyReason);
        }

        Ros2BridgeSessionResult IRos2BridgeContractWireController.Unregister(
            Ros2BridgeSessionContract contract)
        {
            Ros2BridgeConnection connection;
            lock (_gate)
                connection = _connection;
            try
            {
                return connection == null
                    ? Ros2BridgeSessionResult.Accepted()
                    : ((IRos2BridgeContractWireController)connection)
                        .Unregister(contract);
            }
            finally
            {
                _queue.TryRevokeContract(contract, out _);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                ClearConnectionLocked();
            }
            _queue.Stop();
            _leases.Dispose();
            _state.Stop();
            _queue.Dispose();
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(
                    nameof(Ros2BridgeSubscriptionPipeline));
            }
        }

        private void ClearConnectionLocked()
        {
            _connection = null;
            _attemptGeneration = 0;
            _sessionId = string.Empty;
            _connectionGeneration = 0;
        }
    }
}
