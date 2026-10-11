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
        internal Ros2BridgeV2SessionSnapshot Start()
        {
            Ros2BridgeV2Request hello;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped)
                {
                    throw new InvalidOperationException(
                        "The Bridge connection has already started.");
                }
                if (!_transport.IsConnected)
                {
                    throw new InvalidOperationException(
                        "The Bridge session transport is not connected.");
                }
                lock (_retirementGate)
                {
                    for (var i = 0; i < _workerExited.Length; i++)
                    {
                        if (_workerRetired[i]
                            || _workerSlotReturned[i])
                        {
                            throw new InvalidOperationException(
                                "A returned Bridge worker slot cannot be reused.");
                        }
                        _workerExited[i] = false;
                    }
                }
                _transport.BeginV2(_limits, _timeoutMs);
                _stopRequested = false;
                _lastFault = null;
                _lifecycleState =
                    Ros2BridgeSessionLifecycleState.AwaitingHandshake;
                _workersRemaining = 2;
                Volatile.Write(ref _readerManagedThreadId, 0);
                Volatile.Write(ref _writerManagedThreadId, 0);
                var readerStarted = false;
                var writerStarted = false;
                try
                {
                    _reader = new Thread(ReaderEntry)
                    {
                        IsBackground = true,
                        Name = "Unity2Foxglove ROS2 Bridge reader",
                    };
                    _writer = new Thread(WriterEntry)
                    {
                        IsBackground = true,
                        Name = "Unity2Foxglove ROS2 Bridge writer",
                    };
                    _reader.Start();
                    readerStarted = true;
                    _writer.Start();
                    writerStarted = true;
                }
                catch
                {
                    // A Thread constructor/Start failure must not leave a
                    // phantom precharged worker blocking reconnect.
                    _workersRemaining = (readerStarted ? 1 : 0)
                        + (writerStarted ? 1 : 0);
                    throw;
                }
                hello = Ros2BridgeV2SessionCodec.CreateHello(
                    _requestIds.Next(),
                    _requiresSubscription,
                    _limits);
            }

            byte[] response;
            try
            {
                response = EnqueueAndWait(
                    hello,
                    EffectiveTimeout(
                        _timeoutMs,
                        _limits.HandshakeTimeoutMs));
                var snapshot =
                    Ros2BridgeV2SessionCodec.AcceptHello(
                        hello,
                        response,
                        _limits);
                lock (_gate)
                {
                    if (_lifecycleState
                        == Ros2BridgeSessionLifecycleState.Faulted)
                    {
                        ExceptionDispatchInfo.Capture(
                            _lastFault
                            ?? new U2R2ProtocolException(
                                "invalid_frame",
                                "The Bridge connection faulted during its handshake."))
                            .Throw();
                    }
                    if (_stopRequested)
                    {
                        throw new ObjectDisposedException(
                            nameof(Ros2BridgeConnection));
                    }
                    _snapshot = snapshot;
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Ready;
                    return snapshot;
                }
            }
            catch (Exception exception)
            {
                Fault(exception);
                throw;
            }
        }

        internal void Abort(Exception reason)
            => Fault(
                reason
                ?? new IOException(
                    "The Bridge connection was aborted for reconnect."));

        internal void PrepareForReconnect()
        {
            var abortActiveConnection = false;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                abortActiveConnection =
                    _lifecycleState
                    != Ros2BridgeSessionLifecycleState.Faulted
                    && _lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped;
            }

            // The transport can report a closed socket before the dedicated
            // reader observes that close and transitions this connection to
            // Faulted. Drive the still-active state through the same bounded
            // abort path before joining and resetting its workers.
            if (abortActiveConnection)
            {
                Abort(
                    new IOException(
                        "The Bridge transport disconnected before the connection observed the fault."));
            }

            ResetAfterFault();
        }

        internal void ResetAfterFault()
        {
            Thread reader;
            Thread writer;
            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Faulted
                    && _lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped)
                {
                    throw new InvalidOperationException(
                        "Only a stopped or faulted Bridge connection can reconnect.");
                }
                reader = _reader;
                writer = _writer;
            }

            var joinTimeout = EffectiveTimeout(
                _timeoutMs,
                _limits.JoinTimeoutMs);
            if (!JoinUnlessCurrent(reader, joinTimeout)
                || !JoinUnlessCurrent(writer, joinTimeout)
                || Volatile.Read(ref _workersRemaining) != 0)
            {
                throw new U2R2ProtocolException(
                    "timeout",
                    "The previous Bridge connection workers did not exit before reconnect.",
                    terminal: true);
            }

            lock (_gate)
            {
                ThrowIfDisposedLocked();
                if (_pending.Count != 0
                    || _writerQueue.Count != 0)
                {
                    throw new U2R2ProtocolException(
                        "invalid_configuration",
                        "The faulted Bridge connection retained pending work.",
                        terminal: true);
                }
                _reader = null;
                _writer = null;
                _snapshot = null;
                _stopRequested = false;
                _lastFault = null;
                _lifecycleState =
                    Ros2BridgeSessionLifecycleState.Stopped;
            }
        }

        internal U2R2Message Exchange(
            Func<
                ulong,
                Ros2BridgeV2SessionSnapshot,
                Ros2BridgeV2Request> requestFactory,
            int timeoutMs)
        {
            if (requestFactory == null)
                throw new ArgumentNullException(nameof(requestFactory));
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            PendingRequest pending;
            lock (_requestAdmissionGate)
            {
                Ros2BridgeV2SessionSnapshot snapshot;
                lock (_gate)
                {
                    ThrowIfDisposedLocked();
                    if (_lifecycleState
                        != Ros2BridgeSessionLifecycleState.Ready
                        || _snapshot == null)
                    {
                        throw new InvalidOperationException(
                            "Normal Bridge work requires a correlated hello_ack.");
                    }
                    snapshot = _snapshot;
                }

                var requestId = _requestIds.Next();
                var request = requestFactory(requestId, snapshot)
                    ?? throw new InvalidOperationException(
                        "The Bridge request factory returned null.");
                if (request.Expectation.RequestId != requestId
                    || request.Expectation.AssignsSessionIdentity
                    || !string.Equals(
                        request.Expectation.SessionId,
                        snapshot.SessionId,
                        StringComparison.Ordinal)
                    || request.Expectation.ConnectionGeneration
                    != snapshot.ConnectionGeneration)
                {
                    throw new U2R2ProtocolException(
                        "invalid_configuration",
                        "The Bridge request does not target the active session.",
                        terminal: true);
                }

                pending = Enqueue(request);
            }

            var response = WaitForResponse(pending, timeoutMs);
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    response,
                    _limits));
        }

        Ros2BridgeSessionResult
            IRos2BridgeContractWireController.Register(
                Ros2BridgeSessionContract contract)
            => ExchangeContract(
                contract,
                U2R2Operation.SubscriptionReady,
                (requestId, snapshot) =>
                    Ros2BridgeV2SessionCodec
                        .CreateSubscriptionRegistration(
                            snapshot,
                            requestId,
                            contract));

        Ros2BridgeSessionResult
            IRos2BridgeContractWireController.Unregister(
                Ros2BridgeSessionContract contract)
            => ExchangeContract(
                contract,
                U2R2Operation.SubscriptionRemoved,
                (requestId, snapshot) =>
                    Ros2BridgeV2SessionCodec
                        .CreateSubscriptionRemoval(
                            snapshot,
                            requestId,
                            contract));

        private Ros2BridgeSessionResult ExchangeContract(
            Ros2BridgeSessionContract contract,
            U2R2Operation expectedResponse,
            Func<
                ulong,
                Ros2BridgeV2SessionSnapshot,
                Ros2BridgeV2Request> requestFactory)
        {
            if (contract == null)
            {
                return Ros2BridgeSessionResult.Reject(
                    "The Bridge subscription contract is null.");
            }
            try
            {
                var response = Exchange(
                    requestFactory,
                    ProtocolTimeout(
                        _limits.WriteTimeoutMs));
                if (response.Operation == expectedResponse
                    && string.Equals(
                        response.Status,
                        "ok",
                        StringComparison.Ordinal))
                {
                    return Ros2BridgeSessionResult.Accepted();
                }
                var reason = string.IsNullOrWhiteSpace(
                    response.ErrorMessage)
                    ? "The Bridge peer rejected the subscription request."
                    : response.ErrorMessage;
                return response.Terminal
                    ? Ros2BridgeSessionResult.Fault(reason)
                    : Ros2BridgeSessionResult.Reject(reason);
            }
            catch (U2R2ProtocolException exception)
            {
                return exception.Terminal
                    ? Ros2BridgeSessionResult.Fault(
                        exception.Message)
                    : Ros2BridgeSessionResult.Reject(
                        exception.Message);
            }
            catch (Exception exception)
            {
                return Ros2BridgeSessionResult.Fault(
                    exception.Message);
            }
        }

        private byte[] EnqueueAndWait(
            Ros2BridgeV2Request request,
            int timeoutMs)
        {
            var pending = Enqueue(request);
            return WaitForResponse(pending, timeoutMs);
        }

        private PendingRequest Enqueue(
            Ros2BridgeV2Request request)
        {
            var pending = new PendingRequest(request);
            try
            {
                lock (_gate)
                {
                    ThrowIfDisposedLocked();
                    if (_stopRequested
                        || _lifecycleState
                        == Ros2BridgeSessionLifecycleState.Faulted
                        || _lifecycleState
                        == Ros2BridgeSessionLifecycleState.Stopping
                        || _lifecycleState
                        == Ros2BridgeSessionLifecycleState.Stopped)
                    {
                        throw new InvalidOperationException(
                            "The Bridge connection is not admitting work.");
                    }
                    if (_pending.Count >= _pendingCapacity)
                    {
                        throw new U2R2ProtocolException(
                            "capacity_exceeded",
                            "The Bridge outstanding-request table is full.",
                            terminal: false);
                    }
                    if (_writerQueue.Count >= _writerCapacity)
                    {
                        throw new U2R2ProtocolException(
                            "capacity_exceeded",
                            "The Bridge writer queue is full.",
                            terminal: false);
                    }
                    if (_pending.ContainsKey(
                            request.Expectation.RequestId))
                    {
                        throw new U2R2ProtocolException(
                            "request_id_conflict",
                            "The Bridge request ID is already in flight.",
                            terminal: true);
                    }

                    _pending.Add(
                        request.Expectation.RequestId,
                        pending);
                    _writerQueue.Enqueue(pending);
                }
            }
            catch
            {
                pending.Dispose();
                throw;
            }
            _writerSignal.Set();
            return pending;
        }

        private byte[] WaitForResponse(
            PendingRequest pending,
            int timeoutMs)
        {
            using (pending)
            {
                if (!pending.Wait(timeoutMs))
                {
                    var timeout = new U2R2ProtocolException(
                        "timeout",
                        "The Bridge request exceeded its absolute deadline.",
                        terminal: true);
                    if (pending.TryFail(timeout))
                    {
                        Fault(timeout);
                        throw timeout;
                    }
                }
                return pending.GetResponse();
            }
        }

    }
}
