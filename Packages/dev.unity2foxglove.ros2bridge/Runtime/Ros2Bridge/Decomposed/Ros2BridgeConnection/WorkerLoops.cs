// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: One-reader/one-writer bounded U2R2 v2 connection owner.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Components = Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    internal sealed partial class Ros2BridgeConnection :
        IDisposable,
        IRos2BridgeContractWireController
    {
        private void WriterEntry()
        {
            Volatile.Write(
                ref _writerManagedThreadId,
                Thread.CurrentThread.ManagedThreadId);
            try
            {
                WriterLoop();
            }
            finally
            {
                OnWorkerExited(workerIndex: 1);
            }
        }

        private void WriterLoop()
        {
            var heartbeatIntervalTicks =
                HeartbeatIntervalTicks(_limits.ReadTimeoutMs);
            var lastWriteTimestamp = Stopwatch.GetTimestamp();
            while (true)
            {
                PendingRequest request = null;
                PendingRequest heartbeat = null;
                Ros2BridgeV2SessionSnapshot heartbeatSnapshot = null;
                var waitMilliseconds = 50;
                var heartbeatDue = false;
                try
                {
                    lock (_gate)
                    {
                        if (_stopRequested)
                            return;
                        if (_writerQueue.Count != 0)
                        {
                            request = _writerQueue.Dequeue();
                        }
                        else if (_lifecycleState
                                 == Ros2BridgeSessionLifecycleState.Ready
                                 && _snapshot != null)
                        {
                            waitMilliseconds =
                                HeartbeatWaitMilliseconds(
                                    lastWriteTimestamp,
                                    heartbeatIntervalTicks);
                            heartbeatDue = waitMilliseconds == 0;
                        }
                    }

                    if (request == null && heartbeatDue)
                    {
                        lock (_requestAdmissionGate)
                        {
                            lock (_gate)
                            {
                                if (_stopRequested)
                                    return;
                                if (_writerQueue.Count != 0)
                                {
                                    request = _writerQueue.Dequeue();
                                }
                                else if (_lifecycleState
                                         == Ros2BridgeSessionLifecycleState.Ready
                                         && _snapshot != null)
                                {
                                    waitMilliseconds =
                                        HeartbeatWaitMilliseconds(
                                            lastWriteTimestamp,
                                            heartbeatIntervalTicks);
                                    if (waitMilliseconds == 0
                                        && _pending.Count < _pendingCapacity)
                                    {
                                        heartbeatSnapshot = _snapshot;
                                        var heartbeatRequest =
                                            Ros2BridgeV2SessionCodec
                                                .CreateHealthPing(
                                                    heartbeatSnapshot,
                                                    _requestIds.Next());
                                        heartbeat = new PendingRequest(
                                            heartbeatRequest);
                                        _pending.Add(
                                            heartbeatRequest.Expectation.RequestId,
                                            heartbeat);
                                    }
                                    else if (waitMilliseconds == 0)
                                    {
                                        // Existing correlated work owns the bounded
                                        // request table and takes precedence.
                                        waitMilliseconds = 50;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    heartbeat?.Dispose();
                    Fault(exception);
                    return;
                }
                if (request == null && heartbeat == null)
                {
                    _writerSignal.WaitOne(waitMilliseconds);
                    continue;
                }

                try
                {
                    _transport.WriteV2(
                        (request ?? heartbeat).Request.WireBytes,
                        _limits,
                        EffectiveTimeout(
                            _timeoutMs,
                            _limits.WriteTimeoutMs));
                    lastWriteTimestamp = Stopwatch.GetTimestamp();
                    if (heartbeat != null)
                    {
                        AwaitHeartbeatWhileDrainingWriterQueue(
                            heartbeat,
                            ref lastWriteTimestamp);
                        Ros2BridgeV2SessionCodec.AcceptHealthPong(
                            heartbeat.Request,
                            heartbeat.GetResponse(),
                            heartbeatSnapshot);
                    }
                }
                catch (Exception exception)
                {
                    Fault(NormalizeTransportFault(
                        exception,
                        "The Bridge writer failed."));
                    return;
                }
                finally
                {
                    if (heartbeat != null)
                    {
                        lock (_gate)
                        {
                            var requestId =
                                heartbeat.Request.Expectation.RequestId;
                            if (_pending.TryGetValue(
                                    requestId,
                                    out var current)
                                && ReferenceEquals(current, heartbeat))
                            {
                                _pending.Remove(requestId);
                            }
                        }
                        heartbeat.Dispose();
                    }
                }
            }
        }

        private void AwaitHeartbeatWhileDrainingWriterQueue(
            PendingRequest heartbeat,
            ref long lastWriteTimestamp)
        {
            var timeoutMs = EffectiveTimeout(
                _timeoutMs,
                _limits.ReadTimeoutMs);
            var startedAt = Stopwatch.GetTimestamp();
            var timeoutTicks = Math.Max(
                1L,
                checked((long)Math.Ceiling(
                    timeoutMs
                    * (double)Stopwatch.Frequency
                    / 1000d)));

            while (!heartbeat.Wait(timeoutMs: 0))
            {
                var elapsedTicks =
                    Stopwatch.GetTimestamp() - startedAt;
                if (elapsedTicks >= timeoutTicks)
                {
                    throw new U2R2ProtocolException(
                        "timeout",
                        "The Bridge health response exceeded its absolute deadline.",
                        terminal: true);
                }
                var remainingMilliseconds = Math.Max(
                    1,
                    checked((int)Math.Ceiling(
                        (timeoutTicks - elapsedTicks)
                        * 1000d
                        / Stopwatch.Frequency)));

                PendingRequest queued = null;
                lock (_gate)
                {
                    if (_stopRequested)
                        break;
                    if (_writerQueue.Count != 0)
                        queued = _writerQueue.Dequeue();
                }

                if (queued != null)
                {
                    _transport.WriteV2(
                        queued.Request.WireBytes,
                        _limits,
                        Math.Min(
                            remainingMilliseconds,
                            EffectiveTimeout(
                                _timeoutMs,
                                _limits.WriteTimeoutMs)));
                    lastWriteTimestamp = Stopwatch.GetTimestamp();
                    continue;
                }

                _writerSignal.WaitOne(
                    Math.Min(50, remainingMilliseconds));
            }
        }

        private void ReaderEntry()
        {
            Volatile.Write(
                ref _readerManagedThreadId,
                Thread.CurrentThread.ManagedThreadId);
            try
            {
                ReaderLoop();
            }
            finally
            {
                OnWorkerExited(workerIndex: 0);
            }
        }

        private void ReaderLoop()
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_stopRequested)
                        return;
                }

                byte[] wireBytes;
                try
                {
                    wireBytes = _transport.ReadV2(
                        _limits,
                        ProtocolTimeout(
                            _limits.ReadTimeoutMs));
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        if (_stopRequested)
                            return;
                    }
                    Fault(NormalizeTransportFault(
                        exception,
                        "The Bridge reader failed."));
                    return;
                }

                try
                {
                    var decoded = U2R2ProtocolCodec.DecodeFrame(
                        wireBytes,
                        _limits);
                    var message =
                        U2R2ProtocolCodec.ParseV2(decoded);
                    if (message.Operation
                        == U2R2Operation.Message)
                    {
                        HandleInboundMessage(
                            message,
                            decoded.Payload);
                        continue;
                    }
                    if (!message.IsResponse)
                    {
                        throw new U2R2ProtocolException(
                            "invalid_frame",
                            "The Bridge reader received an unowned non-response frame.",
                            terminal: true);
                    }

                    PendingRequest pending;
                    lock (_gate)
                    {
                        if (!_pending.TryGetValue(
                                message.RequestId,
                                out pending))
                        {
                            throw new U2R2ProtocolException(
                                "response_mismatch",
                                "The Bridge response has no matching outstanding request.",
                                terminal: true);
                        }
                    }
                    U2R2ProtocolCodec.ValidateResponseCorrelation(
                        pending.Request.Expectation,
                        message);
                    if (message.Operation
                            == U2R2Operation.SubscriptionReady
                        && _inboundResolver != null)
                    {
                        var readiness =
                            _inboundResolver
                            .TryAcceptSubscriptionReady(message);
                        if (!readiness.IsAccepted)
                        {
                            throw new U2R2ProtocolException(
                                "response_mismatch",
                                string.IsNullOrWhiteSpace(
                                    readiness.Reason)
                                    ? "The Bridge subscription_ready response could not activate its contract."
                                    : readiness.Reason,
                                terminal: true);
                        }
                    }
                    lock (_gate)
                    {
                        if (!_pending.TryGetValue(
                                message.RequestId,
                                out var current)
                            || !ReferenceEquals(current, pending))
                        {
                            throw new U2R2ProtocolException(
                                "response_mismatch",
                                "The Bridge response raced a completed request.",
                                terminal: true);
                        }
                        _pending.Remove(message.RequestId);
                    }
                    if (!pending.TryComplete(wireBytes))
                    {
                        throw new U2R2ProtocolException(
                            "response_mismatch",
                            "The Bridge response completed a request more than once.",
                            terminal: true);
                    }
                }
                catch (Exception exception)
                {
                    Fault(exception);
                    return;
                }
            }
        }

        private void HandleInboundMessage(
            U2R2Message message,
            ReadOnlyMemory<byte> payload)
        {
            if (_inboundResolver == null
                || _inboundReceiver == null)
            {
                throw new U2R2ProtocolException(
                    "unknown_contract",
                    "The Bridge reader has no inbound contract owner.",
                    terminal: true);
            }
            if (payload.Length < 4
                || payload.Span[0] != 0
                || payload.Span[1] != 1
                || payload.Span[2] != 0
                || payload.Span[3] != 0)
            {
                throw new U2R2ProtocolException(
                    "invalid_frame",
                    "The Bridge inbound payload is not complete XCDR1 little-endian CDR.",
                    terminal: true);
            }

            var resolution =
                _inboundResolver.TryResolveInbound(
                    message,
                    out var contract);
            if (resolution.State
                == Ros2BridgeSessionResultState.Rejected)
            {
                _inboundReceiver.RecordResolutionRejection(
                    resolution.Reason);
                return;
            }
            if (!resolution.IsAccepted || contract == null)
            {
                throw new U2R2ProtocolException(
                    "unknown_contract",
                    string.IsNullOrWhiteSpace(
                        resolution.Reason)
                        ? "The Bridge inbound contract is unavailable."
                        : resolution.Reason,
                    terminal: true);
            }

            Ros2BridgeInboundFrame owned = null;
            var transferred = false;
            try
            {
                owned = Ros2BridgeInboundFrame.CopyOwned(
                    contract,
                    message.SessionId,
                    message.ConnectionGeneration,
                    message.MessageId,
                    message.Sequence,
                    message.ReceiveTimeNs,
                    payload,
                    _inboundPool);
                var admission =
                    _inboundReceiver.TryAccept(owned);
                transferred = true;
                if (admission.State
                    == Ros2BridgeSessionResultState.Faulted)
                {
                    throw new U2R2ProtocolException(
                        "invalid_frame",
                        string.IsNullOrWhiteSpace(
                            admission.Reason)
                            ? "The Bridge inbound queue faulted."
                            : admission.Reason,
                        terminal: true);
                }
            }
            finally
            {
                if (!transferred)
                    owned?.Dispose();
            }
        }

    }
}
