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
        private bool EnsureConnected(long generation)
        {
            Ros2BridgeConnection duplexConnection;
            Ros2BridgeReconnectSnapshot reconnect = null;
            var useDuplex =
                _enableDuplexSession
                && !_legacyOnly
                && _ownedSink
                is IRos2BridgeSessionTransport;
            lock (_gate)
            {
                if (_stopRequested || !_enabled || generation != _workerGeneration)
                    return false;
                if (_nextConnectAttemptUnixMs > NowUnixMs())
                    return false;
                duplexConnection = _duplexConnection;
                if (_connected && _sink != null)
                {
                    // A duplex reader owns peer-close detection. Polling the
                    // same socket here races that reader between Poll and
                    // Available and can misclassify a consumed response as
                    // EOF. Once the handshake is complete, the connection
                    // lifecycle is the authoritative liveness state.
                    if (useDuplex && duplexConnection != null)
                    {
                        if (duplexConnection.LifecycleState
                            == Ros2BridgeSessionLifecycleState.Ready)
                        {
                            return true;
                        }
                    }
                    else if (_sink.IsConnected)
                    {
                        return true;
                    }
                }
                _connecting = true;
            }

            try
            {
                if (useDuplex && duplexConnection != null)
                    duplexConnection.PrepareForReconnect();
                if (useDuplex && _requiresSubscription)
                {
                    reconnect =
                        _subscriptionPipeline.BeginReconnect();
                }
                _ownedSink.Connect(_host, _port, _sendTimeoutMs);
                var dialect = U2R2Dialect.V1;
                Ros2BridgeV2SessionSnapshot v2Session = null;
                U2R2RequestIdCounter v2RequestIds = null;
                U2R2MonotonicCounter v2MessageIds = null;
                if (useDuplex)
                {
                    duplexConnection ??= new Ros2BridgeConnection(
                        (IRos2BridgeSessionTransport)_ownedSink,
                        _protocolLimits,
                        _requiresSubscription,
                        writerCapacity: _preparationCapacity,
                        pendingCapacity: checked((int)Math.Min(
                            checked((ulong)_preparationCapacity),
                            _protocolLimits.MaxOutstandingRequests)),
                        timeoutMs: _sendTimeoutMs,
                        inboundResolver:
                            _subscriptionPipeline?.Resolver,
                        inboundReceiver:
                            _subscriptionPipeline?.Receiver,
                        retirement: _retirement,
                        readerRetirementIndex: 1,
                        writerRetirementIndex: 2,
                        retirementIdentity:
                            _workerIdentity + "/duplex",
                        disposeTransport: false);
                    try
                    {
                        v2Session = duplexConnection.Start();
                        if (_requiresSubscription)
                        {
                            _subscriptionPipeline.CompleteReconnect(
                                reconnect,
                                duplexConnection,
                                v2Session);
                        }
                        v2RequestIds = new U2R2RequestIdCounter();
                        v2MessageIds = new U2R2MonotonicCounter();
                        dialect = U2R2Dialect.V2;
                    }
                    catch (Exception exception)
                        when (!_requiresSubscription
                              && IsExplicitV2Incompatibility(exception))
                    {
                        duplexConnection.Dispose();
                        duplexConnection = null;
                        lock (_gate)
                        {
                            _duplexConnection = null;
                            _legacyOnly = true;
                        }
                        DisconnectSink(_ownedSink);
                        if (ShouldStop(generation))
                            return false;
                        _ownedSink.Connect(
                            _host,
                            _port,
                            _sendTimeoutMs);
                    }
                }
                else if (_ownedSink
                         is IRos2BridgeV2SessionTransport v2Transport
                         && !_legacyOnly)
                {
                    v2RequestIds = new U2R2RequestIdCounter();
                    v2MessageIds = new U2R2MonotonicCounter();
                    try
                    {
                        if (!TryReserveSessionTransientUntilAvailable(
                                generation,
                                ControlExchangeReservationBytes(
                                    _protocolLimits),
                                out var helloTransient))
                        {
                            return false;
                        }
                        using (helloTransient)
                        {
                            var hello =
                                Ros2BridgeV2SessionCodec.CreateHello(
                                    v2RequestIds.Next(),
                                    _requiresSubscription,
                                    _protocolLimits);
                            var response = v2Transport.ExchangeV2(
                                hello.WireBytes,
                                _protocolLimits,
                                EffectiveTimeout(
                                    _sendTimeoutMs,
                                    _protocolLimits.HandshakeTimeoutMs));
                            v2Session =
                                Ros2BridgeV2SessionCodec.AcceptHello(
                                    hello,
                                    response,
                                    _protocolLimits);
                        }
                        dialect = U2R2Dialect.V2;
                    }
                    catch (Exception exception)
                        when (!_requiresSubscription
                              && IsExplicitV2Incompatibility(exception))
                    {
                        DisconnectSink(_ownedSink);
                        if (ShouldStop(generation))
                            return false;
                        _ownedSink.Connect(
                            _host,
                            _port,
                            _sendTimeoutMs);
                        v2RequestIds = null;
                        v2MessageIds = null;
                        v2Session = null;
                        dialect = U2R2Dialect.V1;
                    }
                }

                var abandon = false;
                lock (_gate)
                {
                    if (_stopRequested || !_enabled || generation != _workerGeneration)
                    {
                        _connected = false;
                        _connecting = false;
                        _lastDisconnectedUnixMs = NowUnixMs();
                        abandon = true;
                    }
                    else
                    {
                        _sink = _ownedSink;
                        _duplexConnection = duplexConnection;
                        _connected = true;
                        _connecting = false;
                        _lastConnectedUnixMs = NowUnixMs();
                        _lastError = string.Empty;
                        _connectionGeneration++;
                        _wireDialect = dialect;
                        _v2Session = v2Session;
                        _v2RequestIds = v2RequestIds;
                        _v2MessageIds = v2MessageIds;
                        _nextConnectAttemptUnixMs = 0;
                        QueueAllPreparationsLocked();
                    }
                }
                if (abandon)
                {
                    _subscriptionPipeline?.Disconnect();
                    duplexConnection?.Dispose();
                    DisconnectSink(_ownedSink);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                ExceptionDispatchInfo fatal = IsRecoverableRuntimeException(ex)
                    ? null
                    : ExceptionDispatchInfo.Capture(ex);
                try
                {
                    if (duplexConnection != null)
                    {
                        duplexConnection.Abort(ex);
                    }
                    else
                    {
                        DisconnectSink(_ownedSink);
                    }
                }
                catch (Exception cleanupException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(cleanupException);
                }

                lock (_gate)
                {
                    _connected = false;
                    _connecting = false;
                    _lastDisconnectedUnixMs = NowUnixMs();
                    _nextConnectAttemptUnixMs =
                        NowUnixMs() + _reconnectIntervalMs;
                    _lastError = BoundRuntimeDiagnostic(ex.Message);
                    InvalidatePreparationsLocked();
                    _sink = null;
                    if (!_legacyOnly)
                        _duplexConnection = duplexConnection;
                    ClearProtocolSessionLocked();
                }
                _subscriptionPipeline?.Disconnect();

                fatal?.Throw();
                return false;
            }
        }

    }
}
