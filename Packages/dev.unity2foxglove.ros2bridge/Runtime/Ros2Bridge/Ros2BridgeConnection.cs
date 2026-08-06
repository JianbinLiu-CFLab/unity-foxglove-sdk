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
    internal sealed class Ros2BridgeConnection :
        IDisposable,
        IRos2BridgeContractWireController
    {
        private readonly object _gate = new object();
        private readonly object _requestAdmissionGate = new object();
        private readonly IRos2BridgeSessionTransport _transport;
        private readonly bool _disposeTransport;
        private readonly U2R2ProtocolLimits _limits;
        private readonly bool _requiresSubscription;
        private readonly int _writerCapacity;
        private readonly int _pendingCapacity;
        private readonly int _timeoutMs;
        private readonly IRos2BridgeInboundContractResolver
            _inboundResolver;
        private readonly IRos2BridgeInboundFrameReceiver
            _inboundReceiver;
        private readonly IRos2BridgeBytePool _inboundPool;
        private readonly Components
            .FoxRunTransportRetirementReservation _retirement;
        private readonly int _readerRetirementIndex;
        private readonly int _writerRetirementIndex;
        private readonly string _retirementIdentity;
        private readonly object _retirementGate = new object();
        private readonly bool[] _workerExited = new bool[2];
        private readonly bool[] _workerRetired = new bool[2];
        private readonly bool[] _workerSlotReturned = new bool[2];
        private readonly Queue<PendingRequest> _writerQueue =
            new Queue<PendingRequest>();
        private readonly Dictionary<ulong, PendingRequest> _pending =
            new Dictionary<ulong, PendingRequest>();
        private readonly AutoResetEvent _writerSignal =
            new AutoResetEvent(false);
        private readonly U2R2RequestIdCounter _requestIds =
            new U2R2RequestIdCounter();

        private Thread _reader;
        private Thread _writer;
        private Ros2BridgeV2SessionSnapshot _snapshot;
        private Ros2BridgeSessionLifecycleState _lifecycleState;
        private Exception _lastFault;
        private bool _stopRequested;
        private bool _disposeRequested;
        private int _workersRemaining;
        private int _disposeTeardownReady;
        private int _resourcesDisposed;
        private int _readerManagedThreadId;
        private int _writerManagedThreadId;

        internal Ros2BridgeConnection(
            IRos2BridgeSessionTransport transport,
            U2R2ProtocolLimits limits,
            bool requiresSubscription,
            int writerCapacity,
            int pendingCapacity,
            int timeoutMs,
            IRos2BridgeInboundContractResolver inboundResolver = null,
            IRos2BridgeInboundFrameReceiver inboundReceiver = null,
            IRos2BridgeBytePool inboundPool = null,
            Components.FoxRunTransportRetirementReservation
                retirement = null,
            int readerRetirementIndex = -1,
            int writerRetirementIndex = -1,
            string retirementIdentity = null,
            bool disposeTransport = true)
        {
            _transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            _disposeTransport = disposeTransport;
            _limits = limits
                ?? throw new ArgumentNullException(nameof(limits));
            if (writerCapacity <= 0
                || checked((ulong)writerCapacity)
                > limits.MaxTotalQueueDepth)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(writerCapacity));
            }
            if (pendingCapacity <= 0
                || checked((ulong)pendingCapacity)
                > limits.MaxOutstandingRequests)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pendingCapacity));
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));

            _requiresSubscription = requiresSubscription;
            _writerCapacity = writerCapacity;
            _pendingCapacity = pendingCapacity;
            _timeoutMs = timeoutMs;
            if ((inboundResolver == null)
                != (inboundReceiver == null))
            {
                throw new ArgumentException(
                    "Bridge inbound resolution and ownership must be configured together.");
            }
            _inboundResolver = inboundResolver;
            _inboundReceiver = inboundReceiver;
            _inboundPool = inboundPool
                           ?? Ros2BridgeSharedBytePool.Instance;
            if (retirement == null)
            {
                if (readerRetirementIndex != -1
                    || writerRetirementIndex != -1
                    || !string.IsNullOrEmpty(
                        retirementIdentity))
                {
                    throw new ArgumentException(
                        "Bridge worker retirement indexes require a reservation.");
                }
            }
            else
            {
                if (readerRetirementIndex < 0
                    || writerRetirementIndex < 0
                    || readerRetirementIndex
                    == writerRetirementIndex
                    || readerRetirementIndex
                    >= retirement.WorkerCount
                    || writerRetirementIndex
                    >= retirement.WorkerCount
                    || string.IsNullOrWhiteSpace(
                        retirementIdentity))
                {
                    throw new ArgumentException(
                        "Bridge reader and writer require distinct reserved worker slots.");
                }
            }
            _retirement = retirement;
            _readerRetirementIndex =
                readerRetirementIndex;
            _writerRetirementIndex =
                writerRetirementIndex;
            _retirementIdentity =
                retirementIdentity?.Trim() ?? string.Empty;
            _lifecycleState =
                Ros2BridgeSessionLifecycleState.Stopped;
        }

        internal Ros2BridgeSessionLifecycleState LifecycleState
        {
            get
            {
                lock (_gate)
                    return _lifecycleState;
            }
        }

        internal Exception LastFault
        {
            get
            {
                lock (_gate)
                    return _lastFault;
            }
        }

        internal int ReaderManagedThreadId
            => Volatile.Read(ref _readerManagedThreadId);

        internal int WriterManagedThreadId
            => Volatile.Read(ref _writerManagedThreadId);

        internal bool HasInboundPipeline
            => _inboundResolver != null
               && _inboundReceiver != null;

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
                _writer.Start();
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
                    Fault(timeout);
                    throw timeout;
                }
                return pending.GetResponse();
            }
        }

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
                        if (!heartbeat.Wait(
                                EffectiveTimeout(
                                    _timeoutMs,
                                    _limits.ReadTimeoutMs)))
                        {
                            throw new U2R2ProtocolException(
                                "timeout",
                                "The Bridge health response exceeded its absolute deadline.",
                                terminal: true);
                        }
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

        private void Fault(Exception exception)
        {
            if (exception == null)
            {
                exception = new U2R2ProtocolException(
                    "invalid_frame",
                    "The Bridge connection faulted.",
                    terminal: true);
            }

            PendingRequest[] pending;
            lock (_gate)
            {
                if (_lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopped
                    || _lifecycleState
                    == Ros2BridgeSessionLifecycleState.Stopping
                    || _stopRequested)
                {
                    return;
                }
                _lastFault ??= exception;
                _lifecycleState =
                    Ros2BridgeSessionLifecycleState.Faulted;
                _stopRequested = true;
                _snapshot = null;
                pending = new PendingRequest[_pending.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
                _writerQueue.Clear();
            }

            foreach (var request in pending)
                request.TryFail(_lastFault);
            _writerSignal.Set();
            try
            {
                _transport.Close();
            }
            catch (Exception closeException)
            {
                lock (_gate)
                    _lastFault ??= closeException;
            }
        }

        private void OnWorkerExited(int workerIndex)
        {
            if (Interlocked.Decrement(ref _workersRemaining) == 0)
            {
                lock (_gate)
                {
                    if (_disposeRequested)
                    {
                        _lifecycleState =
                            Ros2BridgeSessionLifecycleState.Stopped;
                    }
                }
                TryDisposeResources();
            }

            // The final exclusive slot must remain occupied until the shared
            // transport and worker resources have actually been released.
            CompleteWorkerOwnershipOnExit(workerIndex);
        }

        public void Dispose()
        {
            PendingRequest[] pending;
            Thread reader;
            Thread writer;
            lock (_gate)
            {
                if (_disposeRequested)
                    return;
                _disposeRequested = true;
                if (_lifecycleState
                    != Ros2BridgeSessionLifecycleState.Stopped
                    && _lifecycleState
                    != Ros2BridgeSessionLifecycleState.Faulted)
                {
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Stopping;
                }
                _stopRequested = true;
                _snapshot = null;
                pending = new PendingRequest[_pending.Count];
                _pending.Values.CopyTo(pending, 0);
                _pending.Clear();
                _writerQueue.Clear();
                reader = _reader;
                writer = _writer;
            }

            var disposed = new ObjectDisposedException(
                nameof(Ros2BridgeConnection));
            foreach (var request in pending)
                request.TryFail(disposed);
            _writerSignal.Set();
            try
            {
                _transport.Close();
            }
            catch (Exception exception)
            {
                lock (_gate)
                    _lastFault ??= exception;
            }
            finally
            {
                Volatile.Write(
                    ref _disposeTeardownReady,
                    1);
            }

            var joinTimeout = EffectiveTimeout(
                _timeoutMs,
                _limits.JoinTimeoutMs);
            var readerJoined = JoinUnlessCurrent(
                reader,
                joinTimeout);
            var writerJoined = JoinUnlessCurrent(
                writer,
                joinTimeout);
            if (!readerJoined || !writerJoined)
            {
                lock (_gate)
                {
                    _lastFault ??= new U2R2ProtocolException(
                        "timeout",
                        "The Bridge connection workers did not stop within the join deadline.",
                        terminal: true);
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Faulted;
                }
            }
            else
            {
                lock (_gate)
                {
                    _lifecycleState =
                        Ros2BridgeSessionLifecycleState.Stopped;
                }
            }
            FinalizeWorkerRetirement(
                workerIndex: 0,
                readerJoined);
            FinalizeWorkerRetirement(
                workerIndex: 1,
                writerJoined);
            TryDisposeResources();
        }

        private void CompleteWorkerOwnershipOnExit(
            int workerIndex)
        {
            if (_retirement == null)
                return;

            var completeRetired = false;
            var returnActive = false;
            lock (_retirementGate)
            {
                _workerExited[workerIndex] = true;
                if (_workerRetired[workerIndex])
                {
                    completeRetired = true;
                }
                else if (_disposeRequested
                         && !_workerSlotReturned[workerIndex])
                {
                    _workerSlotReturned[workerIndex] = true;
                    returnActive = true;
                }
            }

            var reservationIndex =
                RetirementIndex(workerIndex);
            if (completeRetired)
            {
                _retirement.TryCompleteRetired(
                    reservationIndex);
            }
            else if (returnActive)
            {
                _retirement.TryReturn(
                    reservationIndex);
            }
        }

        private void FinalizeWorkerRetirement(
            int workerIndex,
            bool joined)
        {
            if (_retirement == null)
                return;

            WorkerRetirementLease lease = null;
            var returnActive = false;
            lock (_retirementGate)
            {
                if (_workerSlotReturned[workerIndex]
                    || _workerRetired[workerIndex])
                {
                    return;
                }
                if (joined || _workerExited[workerIndex])
                {
                    _workerSlotReturned[workerIndex] = true;
                    returnActive = true;
                }
                else
                {
                    lease = new WorkerRetirementLease(this);
                    var converted =
                        _retirement.TryConvertToRetired(
                            RetirementIndex(workerIndex),
                            lease,
                            _retirementIdentity
                            + (workerIndex == 0
                                ? "/reader"
                                : "/writer"),
                            retainedBytes: 0,
                            retainedResources: 3);
                    if (converted)
                    {
                        _workerRetired[workerIndex] = true;
                        lease = null;
                    }
                    else
                    {
                        _lastFault ??=
                            new InvalidOperationException(
                                "The Bridge worker retirement reservation could not be converted.");
                    }
                }
            }

            lease?.Dispose();
            if (returnActive)
            {
                _retirement.TryReturn(
                    RetirementIndex(workerIndex));
            }
        }

        private int RetirementIndex(int workerIndex)
            => workerIndex == 0
                ? _readerRetirementIndex
                : _writerRetirementIndex;

        private void TryDisposeResources()
        {
            if (!_disposeRequested
                || Volatile.Read(
                    ref _disposeTeardownReady) == 0
                || Volatile.Read(ref _workersRemaining) != 0
                || Interlocked.Exchange(
                    ref _resourcesDisposed,
                    1) != 0)
            {
                return;
            }

            Exception first = null;
            if (_disposeTransport)
            {
                try
                {
                    _transport.Dispose();
                }
                catch (Exception exception)
                {
                    first = exception;
                }
            }
            try
            {
                _writerSignal.Dispose();
            }
            catch (Exception exception)
            {
                first ??= exception;
            }
            if (first != null)
            {
                lock (_gate)
                    _lastFault ??= first;
            }
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposeRequested)
                throw new ObjectDisposedException(
                    nameof(Ros2BridgeConnection));
        }

        private static bool JoinUnlessCurrent(
            Thread thread,
            int timeoutMs)
            => thread == null
               || !thread.IsAlive
               || ReferenceEquals(
                   thread,
                   Thread.CurrentThread)
               || thread.Join(timeoutMs);

        private static int EffectiveTimeout(
            int configuredTimeoutMs,
            ulong protocolTimeoutMs)
            => Math.Min(
                configuredTimeoutMs,
                ProtocolTimeout(protocolTimeoutMs));

        private static int ProtocolTimeout(
            ulong protocolTimeoutMs)
            => checked((int)Math.Min(
                protocolTimeoutMs,
                checked((ulong)int.MaxValue)));

        private static long HeartbeatIntervalTicks(
            ulong readTimeoutMs)
        {
            var intervalMilliseconds = Math.Max(
                1,
                ProtocolTimeout(readTimeoutMs) / 2);
            return Math.Max(
                1L,
                checked((long)Math.Ceiling(
                    intervalMilliseconds
                    * (double)Stopwatch.Frequency
                    / 1000d)));
        }

        private static int HeartbeatWaitMilliseconds(
            long lastWriteTimestamp,
            long heartbeatIntervalTicks)
        {
            var elapsedTicks =
                Stopwatch.GetTimestamp() - lastWriteTimestamp;
            if (elapsedTicks >= heartbeatIntervalTicks)
                return 0;
            var remainingMilliseconds = Math.Ceiling(
                (heartbeatIntervalTicks - elapsedTicks)
                * 1000d
                / Stopwatch.Frequency);
            return Math.Max(
                1,
                Math.Min(
                    50,
                    checked((int)remainingMilliseconds)));
        }

        private static Exception NormalizeTransportFault(
            Exception exception,
            string context)
        {
            if (exception is U2R2ProtocolException)
                return exception;
            if (exception is EndOfStreamException
                || exception is ObjectDisposedException
                || exception is IOException)
            {
                return new U2R2ProtocolException(
                    "peer_closed",
                    context + " " + exception.Message,
                    terminal: true,
                    innerException: exception);
            }
            return exception;
        }

        private sealed class PendingRequest : IDisposable
        {
            private readonly object _gate = new object();
            private readonly ManualResetEventSlim _completed =
                new ManualResetEventSlim(false);
            private byte[] _response;
            private Exception _error;
            private bool _settled;

            internal PendingRequest(Ros2BridgeV2Request request)
            {
                Request = request
                    ?? throw new ArgumentNullException(nameof(request));
            }

            internal Ros2BridgeV2Request Request { get; }

            internal bool Wait(int timeoutMs)
                => _completed.Wait(timeoutMs);

            internal bool TryComplete(byte[] response)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _response = response
                        ?? throw new ArgumentNullException(
                            nameof(response));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal bool TryFail(Exception error)
            {
                lock (_gate)
                {
                    if (_settled)
                        return false;
                    _error = error
                        ?? throw new ArgumentNullException(
                            nameof(error));
                    _settled = true;
                }
                _completed.Set();
                return true;
            }

            internal byte[] GetResponse()
            {
                Exception error;
                byte[] response;
                lock (_gate)
                {
                    if (!_settled)
                    {
                        throw new InvalidOperationException(
                            "The Bridge request has not completed.");
                    }
                    error = _error;
                    response = _response;
                }
                if (error != null)
                    ExceptionDispatchInfo.Capture(error).Throw();
                return response;
            }

            public void Dispose() => _completed.Dispose();
        }

        private sealed class WorkerRetirementLease :
            Components.IFoxRunDetachedRetirementLease
        {
            private Ros2BridgeConnection _owner;

            internal WorkerRetirementLease(
                Ros2BridgeConnection owner)
            {
                _owner = owner
                    ?? throw new ArgumentNullException(
                        nameof(owner));
            }

            public void Dispose()
                => Interlocked.Exchange(
                    ref _owner,
                    null);
        }
    }
}
