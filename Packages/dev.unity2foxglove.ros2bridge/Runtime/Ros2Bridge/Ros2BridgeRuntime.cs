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
    /// <summary>
    /// Detached worker-reachable resource group. The public runtime shell may
    /// release this object after an in-place retirement transfer.
    /// </summary>
    internal sealed class Ros2BridgeWorkerLease :
        Components.IFoxRunDetachedRetirementLease
    {
        internal const int MaxRuntimeDiagnosticChars = 512;
        private const string PreparationCapacityReason =
            "ROS2 Bridge publisher preparation capacity is exhausted.";
        private readonly string _host;
        private readonly int _port;
        private readonly int _preparationCapacity;
        private readonly int _reconnectIntervalMs;
        private readonly int _sendTimeoutMs;
        private readonly bool _requiresSubscription;
        private readonly bool _enableDuplexSession;
        private readonly IRos2BridgeSink _ownedSink;
        private readonly Components.FoxRunTransportRetirementReservation _retirement;
        private readonly string _workerIdentity;
        private readonly Ros2BridgeStatsSnapshot _initialStats;
        private readonly U2R2ProtocolLimits _protocolLimits =
            U2R2ProtocolLimits.Default;
        private readonly object _gate = new object();
        private readonly object _retirementGate = new object();
        private readonly Ros2BridgeOutboundScheduler _outbound;
        private readonly Ros2BridgeSubscriptionPipeline
            _subscriptionPipeline;
        private readonly Queue<PublisherPreparationKey> _preparationQueue =
            new Queue<PublisherPreparationKey>();
        private readonly Dictionary<PublisherPreparationKey, PublisherPreparationEntry> _preparations =
            new Dictionary<PublisherPreparationKey, PublisherPreparationEntry>();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);

        private Thread _worker;
        private bool _stopRequested;
        private bool _enabled;
        private bool _autoConnect;
        private bool _connected;
        private bool _connecting;
        private long _workerGeneration;
        private long _sentFrames;
        private long _droppedFrames;
        private long _failedFrames;
        private string _lastError = string.Empty;
        private long _lastConnectedUnixMs;
        private long _lastDisconnectedUnixMs;
        private IRos2BridgeSink _sink;
        private Ros2BridgeConnection _duplexConnection;
        private bool _legacyOnly;
        private long _connectionGeneration;
        private U2R2Dialect _wireDialect;
        private Ros2BridgeV2SessionSnapshot _v2Session;
        private U2R2RequestIdCounter _v2RequestIds;
        private U2R2MonotonicCounter _v2MessageIds;
        private long _nextConnectAttemptUnixMs;
        private InFlightPreparation _inFlightPreparation;
        private bool _hasInFlightPreparation;
        private bool _workerExited;
        private bool _outboundTerminalHandled;
        private bool _retired;
        private bool _finalized;
        private int _resourcesDisposed;

        internal Ros2BridgeWorkerLease(
            string host,
            int port,
            int queueCapacity,
            int reconnectIntervalMs,
            int sendTimeoutMs,
            IRos2BridgeSink ownedSink,
            Components.FoxRunTransportRetirementReservation retirement,
            string workerIdentity,
            bool requiresSubscription,
            bool enableDuplexSession,
            ulong sessionGeneration,
            Ros2BridgeStatsSnapshot initialStats)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "ROS2 Bridge port must be in 1..65535.");
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity), "ROS2 Bridge queue capacity must be positive.");
            if (reconnectIntervalMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(reconnectIntervalMs), "ROS2 Bridge reconnect interval must be positive.");
            if (sendTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(sendTimeoutMs), "ROS2 Bridge send timeout must be positive.");

            _host = NormalizeLoopbackHost(host);
            _port = port;
            _preparationCapacity = Math.Min(
                queueCapacity,
                checked((int)U2R2ProtocolLimits.Default.MaxContracts));
            _reconnectIntervalMs = reconnectIntervalMs;
            _sendTimeoutMs = sendTimeoutMs;
            _requiresSubscription = requiresSubscription;
            _enableDuplexSession = enableDuplexSession;
            _ownedSink = ownedSink ?? throw new ArgumentNullException(nameof(ownedSink));
            _retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
            if (_enableDuplexSession
                && !(_ownedSink
                     is IRos2BridgeSessionTransport))
            {
                throw new ArgumentException(
                    "The configured Bridge sink does not expose an owned duplex session transport.",
                    nameof(ownedSink));
            }
            if (_enableDuplexSession
                && _retirement.WorkerCount < 3)
            {
                throw new ArgumentException(
                    "A duplex Bridge runtime requires outer, reader, and writer retirement slots.",
                    nameof(retirement));
            }
            _workerIdentity = string.IsNullOrWhiteSpace(workerIdentity)
                ? throw new ArgumentException("Worker identity cannot be empty.", nameof(workerIdentity))
                : workerIdentity;
            _initialStats = initialStats;
            _outbound = new Ros2BridgeOutboundScheduler(
                CreateOutboundLimits(queueCapacity),
                sessionGeneration);
            if (_requiresSubscription)
            {
                _subscriptionPipeline =
                    new Ros2BridgeSubscriptionPipeline(
                    _host,
                    _port,
                    sessionGeneration,
                    _protocolLimits);
            }
            _sentFrames = initialStats.SentFrames;
            _droppedFrames = initialStats.DroppedFrames;
            _failedFrames = initialStats.FailedFrames;
            _lastError = initialStats.LastError;
            _lastConnectedUnixMs = initialStats.LastConnectedUnixMs;
            _lastDisconnectedUnixMs = initialStats.LastDisconnectedUnixMs;
        }

        public bool IsConnected
        {
            get
            {
                lock (_gate)
                    return _connected;
            }
        }

        internal bool HasInboundPipeline
        {
            get
            {
                lock (_gate)
                    return _duplexConnection?.HasInboundPipeline ?? false;
            }
        }

        internal Ros2BridgeSessionResult TryAcquireSubscription(
            Ros2BridgeSessionContract contract,
            out IRos2BridgeContractLease lease)
        {
            lease = null;
            if (_subscriptionPipeline == null)
            {
                return Ros2BridgeSessionResult.Unavailable(
                    "The ROS2 Bridge runtime has no subscription pipeline.");
            }

            return _subscriptionPipeline.TryAcquire(
                contract,
                out lease);
        }

        internal bool TryBeginInboundApply(
            out Ros2BridgeInboundApplyLease lease)
        {
            if (_subscriptionPipeline == null)
            {
                lease = null;
                return false;
            }
            return _subscriptionPipeline.TryBeginApply(out lease);
        }

        internal Ros2BridgeInboundStatsSnapshot
            GetInboundStatsSnapshot()
            => _subscriptionPipeline?.GetStatsSnapshot();

        /// <summary>
        /// Queue or query the exact sidecar publisher contract. A transport
        /// connection alone never means that typesupport and QoS are ready.
        /// </summary>
        internal Ros2BridgePublisherReadiness PreparePublisher(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos,
            out string reason)
        {
            try
            {
                Ros2BridgePublisherPreparationCodec.ValidateContract(
                    topic,
                    schemaName,
                    qos);
            }
            catch (ArgumentException exception)
            {
                reason = BoundRuntimeDiagnostic(exception.Message);
                return Ros2BridgePublisherReadiness.Rejected;
            }
            var key = new PublisherPreparationKey(topic, schemaName, qos);
            Ros2BridgePublisherReadiness readiness;
            lock (_gate)
            {
                if (!_enabled || !_autoConnect)
                {
                    reason = "ROS2 Bridge runtime is not enabled for automatic connection.";
                    return Ros2BridgePublisherReadiness.Rejected;
                }

                if (!_preparations.TryGetValue(key, out var entry))
                {
                    if (_preparations.Count >= _preparationCapacity)
                    {
                        reason = PreparationCapacityReason;
                        return Ros2BridgePublisherReadiness.Rejected;
                    }
                    if (!Ros2BridgePublisherPreparationCodec.TryValidateCompleteRequest(
                            topic,
                            schemaName,
                            qos,
                            out reason))
                    {
                        return Ros2BridgePublisherReadiness.Rejected;
                    }
                    entry = new PublisherPreparationEntry();
                    _preparations.Add(key, entry);
                    QueuePreparationLocked(key, entry);
                }

                readiness = entry.Readiness;
                reason = readiness == Ros2BridgePublisherReadiness.Ready
                    ? string.Empty
                    : string.IsNullOrWhiteSpace(entry.Reason)
                        ? "ROS2 Bridge publisher preparation is pending."
                        : entry.Reason;
            }

            if (readiness == Ros2BridgePublisherReadiness.Pending)
                _signal.Set();
            return readiness;
        }

        internal void Start()
        {
            lock (_gate)
            {
                _enabled = true;
                _autoConnect = true;
                _stopRequested = false;
                var generation = ++_workerGeneration;
                _worker = new Thread(WorkerEntry)
                {
                    IsBackground = true,
                    Name = _workerIdentity
                };
                _worker.Start(new WorkerStart(this, generation));
            }

            _signal.Set();
        }

        public bool TryEnqueue(Ros2BridgeFrame frame, out string reason)
        {
            if (!(_ownedSink is IRos2BridgePublisherPreparationTransport)
                && !(_ownedSink is IRos2BridgeV2SessionTransport))
            {
                return TryEnqueueCore(
                    frame,
                    default,
                    requiresPreparation: false,
                    requireReadyAtAdmission: false,
                    out reason);
            }
            if (!TryValidateFrame(frame, out reason))
                return false;
            if (!frame.Qos.HasValue)
            {
                reason =
                    "ROS2 Bridge publisher preparation requires an exact QoS contract.";
                return false;
            }

            var readiness = PreparePublisher(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value,
                out reason);
            if (readiness == Ros2BridgePublisherReadiness.Rejected)
                return false;
            var key = new PublisherPreparationKey(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value);
            return TryEnqueueCore(
                frame,
                key,
                requiresPreparation: true,
                requireReadyAtAdmission: false,
                out reason);
        }

        internal bool TryEnqueuePrepared(Ros2BridgeFrame frame, out string reason)
        {
            if (!TryValidateFrame(frame, out reason))
                return false;
            if (!frame.Qos.HasValue)
            {
                reason = "Prepared ROS2 Bridge frame requires an exact QoS contract.";
                return false;
            }
            var key = new PublisherPreparationKey(
                frame.Topic,
                frame.SchemaName,
                frame.Qos.Value);
            return TryEnqueueCore(
                frame,
                key,
                requiresPreparation: true,
                requireReadyAtAdmission: true,
                out reason);
        }

        private bool TryEnqueueCore(
            Ros2BridgeFrame frame,
            PublisherPreparationKey preparationKey,
            bool requiresPreparation,
            bool requireReadyAtAdmission,
            out string reason)
        {
            if (!TryValidateFrame(frame, out reason))
                return false;

            lock (_gate)
            {
                if (!TryValidateEnqueueState(
                        preparationKey,
                        requiresPreparation,
                        requireReadyAtAdmission,
                        out reason))
                    return false;
            }

            var preparationDisposition = _outbound.PrepareEnqueue(
                frame,
                out var prepared);
            if (preparationDisposition
                != Ros2BridgeOutboundEnqueueDisposition.Accepted)
            {
                lock (_gate)
                {
                    return TryApplyEnqueueDisposition(
                        preparationDisposition,
                        out reason);
                }
            }

            using (prepared)
            {
                lock (_gate)
                {
                    if (!TryValidateEnqueueState(
                            preparationKey,
                            requiresPreparation,
                            requireReadyAtAdmission,
                            out reason))
                        return false;

                    var disposition = _outbound.CommitPrepared(
                        prepared,
                        U2R2QueueOverflowPolicy.DropOldest,
                        requiresPreparation,
                        _connectionGeneration);
                    if (!TryApplyEnqueueDisposition(
                            disposition,
                            out reason))
                        return false;
                }
            }

            _signal.Set();
            return true;
        }

        private bool TryValidateEnqueueState(
            PublisherPreparationKey preparationKey,
            bool requiresPreparation,
            bool requireReadyAtAdmission,
            out string reason)
        {
            if (!_enabled)
            {
                reason = "ROS2 Bridge is disabled.";
                return false;
            }
            if (!_autoConnect)
            {
                reason =
                    "ROS2 Bridge auto-connect is disabled; connect before sending frames.";
                return false;
            }

            if (requiresPreparation)
            {
                if (!_preparations.TryGetValue(preparationKey, out var entry))
                {
                    reason =
                        "ROS2 Bridge publisher preparation was not registered.";
                    return false;
                }
                if (entry.Readiness == Ros2BridgePublisherReadiness.Rejected)
                {
                    reason = entry.Reason;
                    return false;
                }
                if (requireReadyAtAdmission
                    && (entry.Readiness
                            != Ros2BridgePublisherReadiness.Ready
                        || entry.ReadyConnectionGeneration
                            != _connectionGeneration))
                {
                    reason = "ROS2 Bridge publisher preparation is pending.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private bool TryApplyEnqueueDisposition(
            Ros2BridgeOutboundEnqueueDisposition disposition,
            out string reason)
        {
            switch (disposition)
            {
                case Ros2BridgeOutboundEnqueueDisposition.Accepted:
                    reason = string.Empty;
                    return true;
                case Ros2BridgeOutboundEnqueueDisposition.DroppedOldest:
                case Ros2BridgeOutboundEnqueueDisposition.ReplacedLatest:
                    _droppedFrames++;
                    reason = string.Empty;
                    return true;
                case Ros2BridgeOutboundEnqueueDisposition.Oversize:
                    reason =
                        "ROS2 Bridge full wire frame exceeds the bounded outbound limits.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.BackpressureRejected:
                    reason =
                        "ROS2 Bridge bounded outbound capacity is exhausted.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.RejectedAfterStop:
                    reason = "ROS2 Bridge is disabled.";
                    return false;
                case Ros2BridgeOutboundEnqueueDisposition.Faulted:
                    reason = "ROS2 Bridge outbound scheduler faulted.";
                    _failedFrames++;
                    _signal.Set();
                    return false;
                default:
                    throw new InvalidOperationException(
                        "ROS2 Bridge outbound scheduler returned an unknown admission result.");
            }
        }

        private static bool TryValidateFrame(
            Ros2BridgeFrame frame,
            out string reason)
        {
            reason = string.Empty;
            if (frame == null)
            {
                reason = "ROS2 Bridge frame is null.";
                return false;
            }
            return true;
        }

        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
        {
            lock (_gate)
            {
                var outboundCounters = _outbound.Counters;
                return new Ros2BridgeStatsSnapshot(
                    _enabled,
                    _connected,
                    _connecting,
                    checked((int)_outbound.DataQueuedDepth),
                    _sentFrames,
                    _droppedFrames,
                    _failedFrames,
                    _lastError,
                    _lastConnectedUnixMs,
                    _lastDisconnectedUnixMs,
                    AddCounter(
                        _initialStats.AcceptedFrames,
                        outboundCounters.Accepted),
                    AddCounter(
                        _initialStats.ReplacedFrames,
                        outboundCounters.Replaced),
                    AddCounter(
                        _initialStats.OversizeFrames,
                        outboundCounters.Oversize),
                    AddCounter(
                        _initialStats.BackpressureRejectedFrames,
                        outboundCounters.BackpressureRejected),
                    AddCounter(
                        _initialStats.RejectedAfterStopFrames,
                        outboundCounters.RejectedAfterStop),
                    AddCounter(
                        _initialStats.FaultedFrames,
                        outboundCounters.Faulted),
                    AddCounter(
                        _initialStats.DisposalFailures,
                        outboundCounters.DisposalFailures),
                    ToCounter(_outbound.QueuedBytes),
                    ToCounter(_outbound.TransientBytes),
                    ToCounter(_outbound.InFlightBytes));
            }
        }

        internal Ros2BridgePublisherObservationSnapshot
            GetPublisherObservationSnapshot()
        {
            lock (_gate)
            {
                var ready = 0;
                var pending = 0;
                var rejected = 0;
                string selectedTopic = null;
                string selectedSchema = null;
                string selectedReason = string.Empty;
                var selectedPriority = 0;
                foreach (var pair in _preparations)
                {
                    var priority = 0;
                    switch (pair.Value.Readiness)
                    {
                        case Ros2BridgePublisherReadiness.Ready:
                            ready++;
                            break;
                        case Ros2BridgePublisherReadiness.Pending:
                            pending++;
                            priority = 1;
                            break;
                        case Ros2BridgePublisherReadiness.Rejected:
                            rejected++;
                            priority = 2;
                            break;
                    }
                    if (priority == 0
                        || priority < selectedPriority
                        || (priority == selectedPriority
                            && ComparePreparationKey(
                                pair.Key,
                                selectedTopic,
                                selectedSchema) >= 0))
                    {
                        continue;
                    }
                    selectedPriority = priority;
                    selectedTopic = pair.Key.Topic;
                    selectedSchema = pair.Key.SchemaName;
                    selectedReason = pair.Value.Reason;
                }
                var schedulerTerminal =
                    _outbound.TryGetTerminalState(
                        out var schedulerFault);
                if (schedulerTerminal)
                {
                    selectedReason = BoundRuntimeDiagnostic(
                        OutboundTerminalReason(schedulerFault));
                }
                return new Ros2BridgePublisherObservationSnapshot(
                    _preparations.Count,
                    ready,
                    pending,
                    rejected,
                    schedulerTerminal,
                    selectedReason);
            }
        }

        private static int ComparePreparationKey(
            PublisherPreparationKey candidate,
            string selectedTopic,
            string selectedSchema)
        {
            if (selectedTopic == null)
                return -1;
            var topic = string.CompareOrdinal(
                candidate.Topic,
                selectedTopic);
            return topic != 0
                ? topic
                : string.CompareOrdinal(
                    candidate.SchemaName,
                    selectedSchema);
        }

        internal bool StopAndJoin(int joinTimeoutMs)
        {
            Thread worker;
            lock (_gate)
            {
                _enabled = false;
                _stopRequested = true;
                _workerGeneration++;
                var close = _outbound.Close();
                _droppedFrames = checked(
                    _droppedFrames
                    + checked((long)close.ClearedDataDepth));
                _preparationQueue.Clear();
                _preparations.Clear();
                worker = _worker;
                _sink = null;
                _connected = false;
                _connecting = false;
                ClearProtocolSessionLocked();
                _lastDisconnectedUnixMs = NowUnixMs();
            }

            _subscriptionPipeline?.Disconnect();
            ExceptionDispatchInfo fatal = null;
            try
            {
                DisconnectSink(_ownedSink);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            _signal.Set();
            var joined = worker == null
                         || !worker.IsAlive
                         || worker.Join(joinTimeoutMs);
            if (!joined)
            {
                lock (_gate)
                {
                    _lastError = "ROS2 Bridge worker did not stop within timeout.";
                }
            }

            var retired = false;
            if (joined)
            {
                FinalizeActive();
            }
            else
            {
                retired = TryRetireAfterTimeout();
            }
            fatal?.Throw();
            return retired;
        }

        /// <summary>
        /// Enables the background worker for the configured endpoint. The runtime uses its
        /// constructor timeout for worker connect attempts; <paramref name="timeoutMs"/> is
        /// validated for IRos2BridgeSink compatibility.
        /// </summary>
        public void Dispose()
        {
            DisposeResources();
        }

        private static void WorkerEntry(object state)
        {
            var start = (WorkerStart)state;
            try
            {
                start.Lease.WorkerLoop(start.Generation);
            }
            finally
            {
                start.Lease.OnWorkerExited();
            }
        }

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
                                    U2R2ByteLease transient = null;
                                    try
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
                                                    var responseReserve =
                                                        U2R2FrameSize.Create(
                                                            snapshot.Limits,
                                                            snapshot.Limits
                                                                .MaxHeaderBytes,
                                                            payloadBytes: 0);
                                                    var transientBytes =
                                                        checked(
                                                            (ulong)measurement
                                                                .TotalWireBytes
                                                            + responseReserve
                                                                .TotalBytes);
                                                    while (!outboundLease
                                                               .TryReserveTransient(
                                                                   transientBytes,
                                                                   out transient))
                                                    {
                                                        if (ShouldStop(
                                                                generation))
                                                        {
                                                            throw new
                                                                ObjectDisposedException(
                                                                    nameof(
                                                                        Ros2BridgeWorkerLease));
                                                        }
                                                        if (TryHandleOutboundTerminal(
                                                                generation))
                                                        {
                                                            throw new
                                                                OutboundTerminalException();
                                                        }
                                                        _signal.WaitOne(10);
                                                    }
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
                                    finally
                                    {
                                        transient?.Dispose();
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
                            _workerIdentity + "/duplex");
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
                    duplexConnection?.Abort(
                        new ObjectDisposedException(
                            nameof(Ros2BridgeWorkerLease)));
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

        private void ClearInFlightPreparation(
            PublisherPreparationEntry entry,
            string requestId,
            byte[] request)
        {
            lock (_gate)
            {
                if (!_hasInFlightPreparation
                    || !ReferenceEquals(
                        _inFlightPreparation.Entry,
                        entry)
                    || !ReferenceEquals(
                        _inFlightPreparation.Request,
                        request)
                    || !string.Equals(
                        _inFlightPreparation.RequestId,
                        requestId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _inFlightPreparation = default;
                _hasInFlightPreparation = false;
            }
        }

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
