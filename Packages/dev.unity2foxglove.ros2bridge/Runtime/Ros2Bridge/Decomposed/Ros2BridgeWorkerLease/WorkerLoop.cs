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
        private void WorkerLoop(long generation)
        {
            while (true)
            {
                try
                {
                    if (ShouldStop(generation))
                        return;

                    if (TryHandleOutboundTerminal(generation))
                        return;

                    if (!EnsureConnected(generation))
                    {
                        if (TryHandleOutboundTerminal(generation))
                            return;
                        _signal.WaitOne(_reconnectIntervalMs);
                        continue;
                    }

                    if (ProcessNextPreparation(generation))
                        continue;

                    if (!_outbound.TryBeginWrite(out var outboundLease))
                    {
                        if (TryHandleOutboundTerminal(generation))
                            return;
                        _signal.WaitOne(50);
                        continue;
                    }

                    using (outboundLease)
                    {
                        IRos2BridgeSink sink;
                        lock (_gate)
                        {
                            if (_stopRequested || !_enabled || generation != _workerGeneration)
                            {
                                if (!outboundLease.IsControl)
                                {
                                    outboundLease.Drop();
                                    _droppedFrames++;
                                }
                                return;
                            }
                            sink = _sink;
                        }

                        if (!TryAwaitScheduledPreparation(
                                outboundLease,
                                generation,
                                out var preparationFailure))
                        {
                            outboundLease.Drop();
                            lock (_gate)
                            {
                                _droppedFrames++;
                                _failedFrames++;
                                _lastError = BoundRuntimeDiagnostic(
                                    preparationFailure);
                            }
                            continue;
                        }

                        if (sink == null)
                        {
                            outboundLease.Fault(
                                new InvalidOperationException(
                                    "ROS2 Bridge sink is not connected."));
                            MarkFailure("ROS2 Bridge sink is not connected.", disconnect: true, countFrameFailure: false);
                            continue;
                        }

                        try
                        {
                            Ros2BridgeV2SessionSnapshot v2Session;
                            U2R2RequestIdCounter v2RequestIds;
                            U2R2MonotonicCounter v2MessageIds;
                            Ros2BridgeConnection duplexConnection;
                            U2R2Dialect wireDialect;
                            lock (_gate)
                            {
                                v2Session = _v2Session;
                                v2RequestIds = _v2RequestIds;
                                v2MessageIds = _v2MessageIds;
                                duplexConnection = _duplexConnection;
                                wireDialect = _wireDialect;
                            }

                            if (v2Session != null)
                            {
                                if (wireDialect != U2R2Dialect.V2)
                                {
                                    throw new U2R2ProtocolException(
                                        "dialect_downgrade",
                                        "The active ROS2 Bridge socket lost its v2 dialect latch.");
                                }
                                if (outboundLease.SourceFrame == null
                                    || v2MessageIds == null
                                    || duplexConnection == null
                                    && (!(sink
                                          is IRos2BridgeV2SessionTransport)
                                        || v2RequestIds == null))
                                {
                                    throw new U2R2ProtocolException(
                                        "invalid_configuration",
                                        "The active U2R2 v2 publish session is incomplete.");
                                }

                                var messageId = v2MessageIds.Next();
                                if (duplexConnection != null)
                                {
                                    var publishReserve = U2R2FrameSize.Create(
                                        v2Session.Limits,
                                        v2Session.Limits.MaxHeaderBytes,
                                        checked((ulong)outboundLease
                                            .SourceFrame.PayloadLength));
                                    var responseReserve = U2R2FrameSize.Create(
                                        v2Session.Limits,
                                        v2Session.Limits.MaxHeaderBytes,
                                        payloadBytes: 0);
                                    var transientBytes = checked(
                                        publishReserve.TotalBytes
                                        + responseReserve.TotalBytes);
                                    U2R2ByteLease transient;
                                    while (!outboundLease.TryReserveTransient(
                                               transientBytes,
                                               out transient))
                                    {
                                        if (ShouldStop(generation))
                                            return;
                                        if (TryHandleOutboundTerminal(
                                                generation))
                                        {
                                            return;
                                        }
                                        _signal.WaitOne(10);
                                    }

                                    using (transient)
                                    {
                                        var response =
                                            duplexConnection.Exchange(
                                                (requestId, snapshot) =>
                                                {
                                                    var measurement =
                                                        Ros2BridgeV2SessionCodec
                                                            .MeasurePublish(
                                                                outboundLease
                                                                    .SourceFrame,
                                                                snapshot,
                                                                requestId,
                                                                messageId);
                                                    return
                                                        Ros2BridgeV2SessionCodec
                                                            .EncodePublish(
                                                                outboundLease
                                                                    .SourceFrame,
                                                                snapshot,
                                                                requestId,
                                                                messageId,
                                                                measurement);
                                                },
                                                _sendTimeoutMs);
                                        Ros2BridgeV2SessionCodec
                                            .ValidateAcceptedResponse(
                                                response);
                                    }
                                }
                                else
                                {
                                    var v2Transport =
                                        (IRos2BridgeV2SessionTransport)sink;
                                    var requestId = v2RequestIds.Next();
                                    var measurement =
                                        Ros2BridgeV2SessionCodec.MeasurePublish(
                                            outboundLease.SourceFrame,
                                            v2Session,
                                            requestId,
                                            messageId);
                                    var responseReserve = U2R2FrameSize.Create(
                                        v2Session.Limits,
                                        v2Session.Limits.MaxHeaderBytes,
                                        payloadBytes: 0);
                                    var transientBytes = checked(
                                        (ulong)measurement.TotalWireBytes
                                        + responseReserve.TotalBytes);
                                    U2R2ByteLease transient;
                                    while (!outboundLease.TryReserveTransient(
                                               transientBytes,
                                               out transient))
                                    {
                                        if (ShouldStop(generation))
                                            return;
                                        if (TryHandleOutboundTerminal(
                                                generation))
                                        {
                                            return;
                                        }
                                        _signal.WaitOne(10);
                                    }

                                    using (transient)
                                    {
                                    var request =
                                        Ros2BridgeV2SessionCodec.EncodePublish(
                                            outboundLease.SourceFrame,
                                            v2Session,
                                            requestId,
                                            messageId,
                                            measurement);
                                    var response = v2Transport.ExchangeV2(
                                        request.WireBytes,
                                        v2Session.Limits,
                                        _sendTimeoutMs);
                                    Ros2BridgeV2SessionCodec.ValidateResponse(
                                        request,
                                        response,
                                        v2Session);
                                    }
                                }
                            }
                            else
                            {
                                if (wireDialect != U2R2Dialect.V1)
                                {
                                    throw new U2R2ProtocolException(
                                        "dialect_downgrade",
                                        "The active ROS2 Bridge socket lost its v1 dialect latch.");
                                }
                                if (sink is IRos2BridgeRawWireSink rawWireSink)
                                {
                                    rawWireSink.SendWire(
                                        outboundLease.WireBytes,
                                        _sendTimeoutMs);
                                }
                                else if (outboundLease.SourceFrame != null)
                                {
                                    sink.Send(
                                        outboundLease.SourceFrame,
                                        _sendTimeoutMs);
                                }
                                else
                                {
                                    throw new InvalidOperationException(
                                        "ROS2 Bridge control frames require a raw-wire sink.");
                                }
                            }

                            outboundLease.Complete();
                            lock (_gate)
                            {
                                _sentFrames++;
                                if (generation != _workerGeneration)
                                    return;
                                _lastError = string.Empty;
                            }
                        }
                        catch (OutboundTerminalException)
                        {
                            return;
                        }
                        catch (U2R2ProtocolException ex)
                            when (!ex.Terminal)
                        {
                            if (!outboundLease.IsControl)
                                outboundLease.Fault(ex);
                            MarkFailure(
                                ex.Message,
                                disconnect: false);
                        }
                        catch (Exception ex) when (IsRecoverableRuntimeException(ex))
                        {
                            if (!outboundLease.IsControl)
                            {
                                try
                                {
                                    outboundLease.Fault(ex);
                                }
                                catch (InvalidOperationException)
                                {
                                    // Complete already settled the lease before
                                    // lifecycle state changed.
                                }
                            }
                            MarkFailure(ex.Message, disconnect: true);
                        }
                    }
                }
                catch (ObjectDisposedException) when (ShouldStop(generation))
                {
                    return;
                }
                catch (Exception ex) when (IsRecoverableRuntimeException(ex))
                {
                    if (ShouldStop(generation))
                        return;
                    MarkFailure(ex.Message, disconnect: true);
                }
            }
        }

    }
}
