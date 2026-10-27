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
        private bool TryAwaitScheduledPreparation(
            Ros2BridgeOutboundWriteLease outboundLease,
            long workerGeneration,
            out string reason)
        {
            reason = string.Empty;
            if (!outboundLease.RequiresPreparation)
                return true;

            var frame = outboundLease.SourceFrame;
            if (frame == null || !frame.Qos.HasValue)
            {
                reason =
                    "Prepared ROS2 Bridge frame lost its exact QoS contract.";
                return false;
            }
            var key = new PublisherPreparationKey(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value);

            while (true)
            {
                var reconnectRequired = false;
                lock (_gate)
                {
                    if (_stopRequested
                        || !_enabled
                        || workerGeneration != _workerGeneration)
                    {
                        reason =
                            "ROS2 Bridge stopped while publisher preparation was pending.";
                        return false;
                    }
                    reconnectRequired = !_connected || _sink == null;
                    if (!_preparations.TryGetValue(key, out var entry))
                    {
                        reason =
                            "ROS2 Bridge publisher preparation no longer exists.";
                        return false;
                    }
                    if (entry.Readiness
                        == Ros2BridgePublisherReadiness.Rejected)
                    {
                        reason = string.IsNullOrWhiteSpace(entry.Reason)
                            ? "ROS2 Bridge publisher preparation was rejected."
                            : entry.Reason;
                        return false;
                    }
                    if (entry.Readiness
                            == Ros2BridgePublisherReadiness.Ready
                        && entry.ReadyConnectionGeneration
                        == _connectionGeneration
                        && _connectionGeneration
                        >= outboundLease.EnqueueConnectionGeneration)
                    {
                        return true;
                    }

                    if (!reconnectRequired)
                        QueuePreparationLocked(key, entry);
                }

                if (reconnectRequired)
                {
                    if (!EnsureConnected(workerGeneration))
                    {
                        if (ShouldStop(workerGeneration))
                        {
                            reason =
                                "ROS2 Bridge stopped while publisher preparation was pending.";
                            return false;
                        }
                        _signal.WaitOne(10);
                    }
                    continue;
                }

                if (!ProcessNextPreparation(workerGeneration))
                    _signal.WaitOne(10);
            }
        }

        private bool ProcessNextPreparation(long workerGeneration)
        {
            PublisherPreparationKey key;
            PublisherPreparationEntry entry;
            IRos2BridgeSink sink;
            long connectionGeneration;
            string requestId;
            Ros2BridgeV2SessionSnapshot v2Session;
            U2R2RequestIdCounter v2RequestIds;
            Ros2BridgeConnection duplexConnection;
            lock (_gate)
            {
                if (_stopRequested
                    || !_enabled
                    || workerGeneration != _workerGeneration
                    || !_connected
                    || _sink == null
                    || _preparationQueue.Count == 0)
                {
                    return false;
                }

                key = _preparationQueue.Dequeue();
                if (!_preparations.TryGetValue(key, out entry))
                    return true;
                entry.Queued = false;
                if (entry.Readiness != Ros2BridgePublisherReadiness.Pending)
                    return true;
                sink = _sink;
                connectionGeneration = _connectionGeneration;
                v2Session = _v2Session;
                v2RequestIds = _v2RequestIds;
                duplexConnection = _duplexConnection;
                requestId = v2Session == null
                            || duplexConnection != null
                    ? "u2r2-prepare-" + Guid.NewGuid().ToString("N")
                    : (v2RequestIds
                       ?? throw new U2R2ProtocolException(
                           "invalid_configuration",
                           "The U2R2 v2 request counter is unavailable."))
                    .Next()
                    .ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                entry.RequestId = requestId;
                entry.Reason = "ROS2 Bridge publisher preparation is pending.";
            }

            var legacyTransport =
                sink as IRos2BridgePublisherPreparationTransport;
            if (v2Session == null && legacyTransport == null)
            {
                lock (_gate)
                {
                    if (_preparations.TryGetValue(key, out var current)
                        && ReferenceEquals(current, entry)
                        && connectionGeneration == _connectionGeneration)
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Rejected;
                        current.Reason =
                            "Connected ROS2 Bridge transport does not support per-publisher preparation.";
                    }
                }
                return true;
            }

            byte[] request = null;
            U2R2ByteLease exchangeTransient = null;
            try
            {
                Ros2BridgeV2Request v2Request = null;
                if (v2Session != null)
                {
                    if (duplexConnection == null
                        && !(sink
                             is IRos2BridgeV2SessionTransport))
                    {
                        throw new U2R2ProtocolException(
                            "invalid_configuration",
                            "The active U2R2 v2 preparation transport is unavailable.");
                    }
                    if (!TryReserveSessionTransientUntilAvailable(
                            workerGeneration,
                            ControlExchangeReservationBytes(
                                v2Session.Limits),
                            out exchangeTransient))
                    {
                        return true;
                    }
                    if (duplexConnection == null)
                    {
                        v2Request =
                            Ros2BridgeV2SessionCodec
                                .CreatePublisherPreparation(
                                    v2Session,
                                    ulong.Parse(
                                        requestId,
                                        System.Globalization
                                            .CultureInfo
                                            .InvariantCulture),
                                    key.Topic,
                                    key.SchemaName,
                                    key.Qos);
                        request = v2Request.WireBytes;
                    }
                    else
                    {
                        request = Array.Empty<byte>();
                    }
                }
                else
                {
                    request =
                        Ros2BridgePublisherPreparationCodec.WriteRequest(
                            requestId,
                            key.Topic,
                            key.SchemaName,
                            key.Qos);
                }
                if (!TrySetInFlightPreparation(
                        workerGeneration,
                        connectionGeneration,
                        sink,
                        key,
                        entry,
                        requestId,
                        request))
                {
                    return true;
                }
                var accepted = false;
                string rejectionReason = null;
                if (v2Session != null)
                {
                    if (duplexConnection != null)
                    {
                        var response =
                            duplexConnection.Exchange(
                                (wireRequestId, snapshot) =>
                                    Ros2BridgeV2SessionCodec
                                        .CreatePublisherPreparation(
                                            snapshot,
                                            wireRequestId,
                                            key.Topic,
                                            key.SchemaName,
                                            key.Qos),
                                _sendTimeoutMs);
                        Ros2BridgeV2SessionCodec
                            .ValidateAcceptedResponse(
                                response);
                    }
                    else
                    {
                        var responseFrame =
                            ((IRos2BridgeV2SessionTransport)sink).ExchangeV2(
                                request,
                                v2Session.Limits,
                                _sendTimeoutMs);
                        Ros2BridgeV2SessionCodec.ValidateResponse(
                            v2Request,
                            responseFrame,
                            v2Session);
                    }
                    accepted = true;
                }
                else
                {
                    var responseFrame =
                        legacyTransport.ExchangePublisherPreparation(
                            request,
                            _sendTimeoutMs);
                    var response =
                        Ros2BridgePublisherPreparationCodec.ParseResponse(
                            responseFrame,
                            requestId);
                    accepted = string.Equals(
                        response.Status,
                        "ok",
                        StringComparison.Ordinal);
                    if (!accepted)
                    {
                        rejectionReason =
                            string.IsNullOrWhiteSpace(response.Message)
                                ? "ROS2 Bridge publisher was rejected: "
                                  + response.ErrorCode
                                : response.Message;
                    }
                }
                exchangeTransient?.Dispose();
                exchangeTransient = null;
                lock (_gate)
                {
                    if (!_preparations.TryGetValue(key, out var current)
                        || !ReferenceEquals(current, entry)
                        || connectionGeneration != _connectionGeneration
                        || !ReferenceEquals(sink, _sink)
                        || !ReferenceEquals(v2Session, _v2Session)
                        || !string.Equals(current.RequestId, requestId, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (accepted)
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Ready;
                        current.ReadyConnectionGeneration = connectionGeneration;
                        current.Reason = string.Empty;
                    }
                    else
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Rejected;
                        current.ReadyConnectionGeneration = 0;
                        current.Reason = rejectionReason;
                    }
                }
            }
            catch (U2R2ProtocolException exception)
                when (v2Session != null && exception.Terminal)
            {
                MarkFailure(
                    "ROS2 Bridge v2 publisher preparation failed: "
                    + exception.Message,
                    disconnect: true,
                    countFrameFailure: false);
            }
            catch (U2R2ProtocolException exception)
                when (v2Session != null && !exception.Terminal)
            {
                lock (_gate)
                {
                    if (_preparations.TryGetValue(key, out var current)
                        && ReferenceEquals(current, entry)
                        && connectionGeneration == _connectionGeneration
                        && ReferenceEquals(sink, _sink)
                        && ReferenceEquals(v2Session, _v2Session))
                    {
                        current.Readiness =
                            Ros2BridgePublisherReadiness.Rejected;
                        current.ReadyConnectionGeneration = 0;
                        current.Reason = BoundRuntimeDiagnostic(
                            string.IsNullOrWhiteSpace(exception.Message)
                                ? "ROS2 Bridge publisher preparation was rejected."
                                : exception.Message);
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is FormatException
                || exception is OverflowException)
            {
                if (v2Session != null)
                {
                    MarkFailure(
                        "ROS2 Bridge v2 publisher preparation protocol failed: "
                        + exception.Message,
                        disconnect: true,
                        countFrameFailure: false);
                    return true;
                }
                lock (_gate)
                {
                    if (_preparations.TryGetValue(key, out var current)
                        && ReferenceEquals(current, entry)
                        && connectionGeneration == _connectionGeneration
                        && ReferenceEquals(sink, _sink))
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Rejected;
                        current.ReadyConnectionGeneration = 0;
                        current.Reason = BoundRuntimeDiagnostic(
                            "ROS2 Bridge publisher preparation protocol was rejected: "
                            + exception.Message);
                    }
                }
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                MarkFailure(
                    "ROS2 Bridge publisher preparation failed: " + exception.Message,
                    disconnect: true,
                    countFrameFailure: false);
            }
            finally
            {
                ClearInFlightPreparation(entry, requestId, request);
                exchangeTransient?.Dispose();
            }

            return true;
        }

        private bool TrySetInFlightPreparation(
            long workerGeneration,
            long connectionGeneration,
            IRos2BridgeSink sink,
            PublisherPreparationKey key,
            PublisherPreparationEntry entry,
            string requestId,
            byte[] request)
        {
            lock (_gate)
            {
                if (_stopRequested
                    || !_enabled
                    || workerGeneration != _workerGeneration
                    || connectionGeneration != _connectionGeneration
                    || !ReferenceEquals(sink, _sink)
                    || !_preparations.TryGetValue(key, out var current)
                    || !ReferenceEquals(current, entry)
                    || !string.Equals(
                        current.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                _inFlightPreparation = new InFlightPreparation(
                    key,
                    entry,
                    requestId,
                    request);
                _hasInFlightPreparation = true;
                return true;
            }
        }

    }
}
