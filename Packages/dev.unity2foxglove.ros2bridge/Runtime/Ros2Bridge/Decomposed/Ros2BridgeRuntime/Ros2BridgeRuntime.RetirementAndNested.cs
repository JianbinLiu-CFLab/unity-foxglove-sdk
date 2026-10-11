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
    internal sealed partial class Ros2BridgeWorkerLease
    {
        private bool ShouldStop(long generation)
        {
            lock (_gate)
                return _stopRequested || generation != _workerGeneration;
        }

        private bool TryRetireAfterTimeout()
        {
            var finalizeDirect = false;
            lock (_retirementGate)
            {
                if (_finalized || _retired)
                    return _retired;
                if (_workerExited)
                {
                    _finalized = true;
                    finalizeDirect = true;
                }
                else
                {
                    CaptureRetainedOwnership(
                        out var retainedBytes,
                        out var retainedResources);
                    if (!_retirement.TryConvertToRetired(
                            workerIndex: 0,
                            this,
                            _workerIdentity,
                            retainedBytes,
                            retainedResources))
                    {
                        throw new InvalidOperationException(
                            "ROS2 Bridge failed to convert its pre-reserved retirement slot.");
                    }
                    _retired = true;
                    return true;
                }
            }

            if (finalizeDirect)
            {
                try
                {
                    DisposeResources();
                }
                finally
                {
                    _retirement.TryReturn(0);
                }
            }
            return false;
        }

        private void CaptureRetainedOwnership(
            out long retainedBytes,
            out int retainedResources)
        {
            lock (_gate)
            {
                retainedBytes = 0;
                retainedResources = 0;
                CountRetainedResource(_ownedSink, ref retainedResources);
                CountRetainedResource(_worker, ref retainedResources);
                CountRetainedResource(_signal, ref retainedResources);
                CountRetainedResource(_outbound, ref retainedResources);
                CountRetainedResource(
                    _preparationQueue,
                    ref retainedResources);
                CountRetainedResource(
                    _preparations,
                    ref retainedResources);
                CountRetainedResource(
                    _v2Session,
                    ref retainedResources);
                CountRetainedResource(
                    _v2RequestIds,
                    ref retainedResources);
                CountRetainedResource(
                    _v2MessageIds,
                    ref retainedResources);
                CountRetainedResource(
                    _duplexConnection,
                    ref retainedResources);

                var queuedDepth = _outbound.TotalQueuedDepth;
                var queuedBytes = _outbound.QueuedBytes;
                var transientBytes = _outbound.TransientBytes;
                var inFlightBytes = _outbound.InFlightBytes;
                retainedResources = checked(
                    retainedResources + checked((int)queuedDepth));
                if (transientBytes != 0)
                    retainedResources = checked(retainedResources + 1);
                if (inFlightBytes != 0)
                    retainedResources = checked(retainedResources + 1);
                retainedBytes = checked(
                    retainedBytes
                    + checked((long)queuedBytes)
                    + checked((long)transientBytes)
                    + checked((long)inFlightBytes));

                if (_hasInFlightPreparation)
                {
                    retainedResources = checked(retainedResources + 1);
                    if (_inFlightPreparation.Request != null)
                    {
                        retainedResources = checked(retainedResources + 1);
                        retainedBytes = checked(
                            retainedBytes
                            + _inFlightPreparation.Request.Length);
                    }
                    CountRetainedResource(
                        _inFlightPreparation.Entry,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.RequestId,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.Key.Topic,
                        ref retainedResources);
                    CountRetainedResource(
                        _inFlightPreparation.Key.SchemaName,
                        ref retainedResources);
                }
            }
        }

        private static void CountRetainedResource(
            object resource,
            ref int retainedResources)
        {
            if (resource != null)
                retainedResources = checked(retainedResources + 1);
        }

        private static long AddCounter(long baseline, ulong value)
        {
            var bounded = ToCounter(value);
            return baseline >= long.MaxValue - bounded
                ? long.MaxValue
                : baseline + bounded;
        }

        private static long ToCounter(ulong value)
            => value >= long.MaxValue
                ? long.MaxValue
                : checked((long)value);

        private static U2R2ProtocolLimits CreateOutboundLimits(
            int queueCapacity)
        {
            var defaults = U2R2ProtocolLimits.Default;
            var dataDepth = checked((ulong)queueCapacity);
            var perContractDepth = Math.Min(
                dataDepth,
                defaults.MaxPerContractQueueDepth);
            return defaults.With(
                (
                    "maxTotalQueueDepth",
                    checked(
                        defaults.ReservedControlQueueDepth
                        + dataDepth)),
                (
                    "maxPerContractQueueDepth",
                    perContractDepth));
        }

        private void FinalizeActive()
        {
            lock (_retirementGate)
            {
                if (_finalized || _retired)
                    return;
                _finalized = true;
            }

            try
            {
                DisposeResources();
            }
            finally
            {
                _retirement.TryReturn(0);
            }
        }

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

        private readonly struct WorkerStart
        {
            internal WorkerStart(
                Ros2BridgeWorkerLease lease,
                long generation)
            {
                Lease = lease;
                Generation = generation;
            }

            internal Ros2BridgeWorkerLease Lease { get; }
            internal long Generation { get; }
        }

        private sealed class OutboundTerminalException : Exception
        {
        }

        private sealed class PublisherPreparationEntry
        {
            internal Ros2BridgePublisherReadiness Readiness =
                Ros2BridgePublisherReadiness.Pending;
            internal string RequestId = string.Empty;
            internal string Reason = "ROS2 Bridge publisher preparation is pending.";
            internal long ReadyConnectionGeneration;
            internal bool Queued;
        }

        private readonly struct InFlightPreparation
        {
            internal InFlightPreparation(
                PublisherPreparationKey key,
                PublisherPreparationEntry entry,
                string requestId,
                byte[] request)
            {
                Key = key;
                Entry = entry;
                RequestId = requestId;
                Request = request;
            }

            internal PublisherPreparationKey Key { get; }
            internal PublisherPreparationEntry Entry { get; }
            internal string RequestId { get; }
            internal byte[] Request { get; }
        }

        private readonly struct PublisherPreparationKey :
            IEquatable<PublisherPreparationKey>
        {
            internal PublisherPreparationKey(
                string topic,
                string schemaName,
                FoxRunResolvedQos qos)
            {
                Topic = topic;
                SchemaName = schemaName;
                Qos = qos;
            }

            internal string Topic { get; }
            internal string SchemaName { get; }
            internal FoxRunResolvedQos Qos { get; }

            public bool Equals(PublisherPreparationKey other)
                => string.Equals(Topic, other.Topic, StringComparison.Ordinal)
                   && string.Equals(SchemaName, other.SchemaName, StringComparison.Ordinal)
                   && Qos.Equals(other.Qos);

            public override bool Equals(object obj)
                => obj is PublisherPreparationKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Topic);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SchemaName);
                    return (hash * 397) ^ Qos.GetHashCode();
                }
            }
        }

        private static string BoundRuntimeDiagnostic(string value)
        {
            value = string.IsNullOrWhiteSpace(value)
                ? "ROS2 Bridge runtime failure."
                : value;
            return value.Length <= MaxRuntimeDiagnosticChars
                ? value
                : value.Substring(0, MaxRuntimeDiagnosticChars);
        }
    }
}
