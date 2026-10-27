// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using Components = Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge.Protocol;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeWorkerLease :
        Components.IFoxRunDetachedRetirementLease
    {
        private void OnWorkerExited()
        {
            var completeRetired = false;
            lock (_retirementGate)
            {
                _workerExited = true;
                if (_retired && !_finalized)
                {
                    _finalized = true;
                    completeRetired = true;
                }
            }

            if (completeRetired)
                _retirement.TryCompleteRetired(0);
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
                return;

            ExceptionDispatchInfo fatal = null;
            Ros2BridgeConnection duplexConnection;
            lock (_gate)
            {
                _preparationQueue.Clear();
                _preparations.Clear();
                _inFlightPreparation = default;
                _hasInFlightPreparation = false;
                _sink = null;
                duplexConnection = _duplexConnection;
                _duplexConnection = null;
                _worker = null;
            }
            try
            {
                _subscriptionPipeline?.Dispose();
                duplexConnection?.Dispose();
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                RecordOutboundDisposalFailure(ref fatal);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
                RecordOutboundDisposalFailure(ref fatal);
            }
            try
            {
                _outbound.Dispose();
            }
            catch (Exception exception) when (IsRecoverableRuntimeException(exception))
            {
                RecordOutboundDisposalFailure(ref fatal);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
                RecordOutboundDisposalFailure(ref fatal);
            }
            try
            {
                _ownedSink.Dispose();
            }
            catch (Exception exception) when (IsRecoverableRuntimeException(exception))
            {
                RecordOutboundDisposalFailure(ref fatal);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
                RecordOutboundDisposalFailure(ref fatal);
            }
            try
            {
                _signal.Dispose();
            }
            catch (Exception exception) when (IsRecoverableRuntimeException(exception))
            {
                RecordOutboundDisposalFailure(ref fatal);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
                RecordOutboundDisposalFailure(ref fatal);
            }
            try
            {
                _retirement.Dispose();
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                RecordOutboundDisposalFailure(ref fatal);
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
                RecordOutboundDisposalFailure(ref fatal);
            }
            fatal?.Throw();
        }

        private void RecordOutboundDisposalFailure(
            ref ExceptionDispatchInfo fatal)
        {
            try
            {
                _outbound.RecordDisposalFailure();
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                // Cleanup diagnostics cannot interrupt the remaining cleanup.
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        private void MarkFailure(string message, bool disconnect, bool countFrameFailure = true)
        {
            IRos2BridgeSink sink = null;
            Ros2BridgeConnection duplexConnection = null;
            lock (_gate)
            {
                if (countFrameFailure)
                    _failedFrames++;
                _lastError = BoundRuntimeDiagnostic(
                    string.IsNullOrWhiteSpace(message)
                        ? "ROS2 Bridge send failed."
                        : message);
                _connecting = false;
                if (disconnect)
                {
                    _connected = false;
                    _lastDisconnectedUnixMs = NowUnixMs();
                    sink = _sink;
                    _sink = null;
                    duplexConnection = _duplexConnection;
                    _nextConnectAttemptUnixMs = NowUnixMs() + _reconnectIntervalMs;
                    InvalidatePreparationsLocked();
                    ClearProtocolSessionLocked();
                }
            }

            if (disconnect)
                _subscriptionPipeline?.Disconnect();

            if (duplexConnection != null)
            {
                duplexConnection.Abort(
                    new IOException(
                        string.IsNullOrWhiteSpace(message)
                            ? "ROS2 Bridge connection failed."
                            : message));
            }
            else
            {
                DisconnectSink(sink);
            }
        }

        private bool TryHandleOutboundTerminal(long generation)
        {
            if (!_outbound.TryGetTerminalState(
                    out var terminalFault))
            {
                return false;
            }

            IRos2BridgeSink sink = null;
            Ros2BridgeConnection duplexConnection = null;
            string reason = null;
            var transition = false;
            lock (_gate)
            {
                if (_stopRequested
                    || !_enabled
                    || generation != _workerGeneration)
                {
                    return true;
                }
                if (!_outboundTerminalHandled)
                {
                    _outboundTerminalHandled = true;
                    transition = true;
                    reason = BoundRuntimeDiagnostic(
                        OutboundTerminalReason(terminalFault));
                    _lastError = reason;
                    _connected = false;
                    _connecting = false;
                    _lastDisconnectedUnixMs = NowUnixMs();
                    _nextConnectAttemptUnixMs = long.MaxValue;
                    sink = _sink ?? _ownedSink;
                    _sink = null;
                    duplexConnection = _duplexConnection;
                    InvalidatePreparationsLocked();
                    ClearProtocolSessionLocked();
                }
            }

            if (!transition)
                return true;

            _subscriptionPipeline?.Disconnect();
            if (duplexConnection != null)
            {
                duplexConnection.Abort(
                    new IOException(reason));
            }
            else
            {
                DisconnectSink(sink);
            }
            return true;
        }

        private static string OutboundTerminalReason(
            Exception terminalFault)
        {
            if (terminalFault == null)
            {
                return
                    "ROS2 Bridge outbound scheduler closed unexpectedly.";
            }
            return string.IsNullOrWhiteSpace(terminalFault.Message)
                ? "ROS2 Bridge outbound scheduler faulted."
                : "ROS2 Bridge outbound scheduler faulted: "
                  + terminalFault.Message;
        }

        private static void DisconnectSink(IRos2BridgeSink sink)
        {
            if (sink == null)
                return;

            try
            {
                sink.Disconnect();
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                // Shutdown is best-effort; state has already been updated.
            }
            catch (Exception exception)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }

        private static bool IsRecoverableRuntimeException(Exception exception)
            => Components.FoxRunExceptionPolicy.IsRecoverable(exception);

        private static bool IsExplicitV2Incompatibility(
            Exception exception)
            => exception is Ros2BridgeV2IncompatibilityException
               || exception is U2R2ProtocolException protocol
               && string.Equals(
                   protocol.ErrorCode,
                   "unsupported_protocol",
                   StringComparison.Ordinal);

        private bool TryReserveSessionTransientUntilAvailable(
            long workerGeneration,
            ulong bytes,
            out U2R2ByteLease lease)
        {
            while (!_outbound.TryReserveSessionTransient(
                       bytes,
                       out lease))
            {
                if (ShouldStop(workerGeneration))
                {
                    lease = null;
                    return false;
                }
                if (_outbound.TryGetTerminalState(out _))
                {
                    lease = null;
                    return false;
                }
                _signal.WaitOne(10);
            }
            return true;
        }

        private static ulong ControlExchangeReservationBytes(
            U2R2ProtocolLimits limits)
        {
            var controlFrame = checked(
                limits.FixedFrameBytes + limits.MaxHeaderBytes);
            return checked(controlFrame * 2UL);
        }

        private static int EffectiveTimeout(
            int configuredTimeoutMs,
            ulong protocolTimeoutMs)
            => checked((int)Math.Min(
                checked((ulong)configuredTimeoutMs),
                Math.Min(
                    protocolTimeoutMs,
                    checked((ulong)int.MaxValue))));

        private void ClearProtocolSessionLocked()
        {
            _wireDialect = U2R2Dialect.None;
            _v2Session = null;
            _v2RequestIds = null;
            _v2MessageIds = null;
        }

        private static long NowUnixMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string NormalizeLoopbackHost(string host)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "127.0.0.1";
            return IPAddress.TryParse(host, out var address) ? address.ToString() : host.Trim();
        }

        private void QueueAllPreparationsLocked()
        {
            _preparationQueue.Clear();
            foreach (var pair in _preparations)
            {
                pair.Value.Readiness = Ros2BridgePublisherReadiness.Pending;
                pair.Value.Reason = "ROS2 Bridge publisher preparation is pending.";
                pair.Value.RequestId = string.Empty;
                pair.Value.ReadyConnectionGeneration = 0;
                pair.Value.Queued = false;
                QueuePreparationLocked(pair.Key, pair.Value);
            }
        }

        private void InvalidatePreparationsLocked()
        {
            _preparationQueue.Clear();
            foreach (var entry in _preparations.Values)
            {
                entry.Readiness = Ros2BridgePublisherReadiness.Pending;
                entry.Reason = "ROS2 Bridge connection changed; publisher preparation is pending.";
                entry.RequestId = string.Empty;
                entry.ReadyConnectionGeneration = 0;
                entry.Queued = false;
            }
        }

        private void QueuePreparationLocked(
            PublisherPreparationKey key,
            PublisherPreparationEntry entry)
        {
            if (entry.Queued)
                return;
            entry.Queued = true;
            _preparationQueue.Enqueue(key);
        }

    }
}
