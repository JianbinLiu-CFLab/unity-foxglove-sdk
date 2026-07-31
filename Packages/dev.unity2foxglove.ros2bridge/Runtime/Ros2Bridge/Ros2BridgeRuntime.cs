// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Background queue and reconnect runtime for the ROS2 Bridge mirror.

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading;
using Components = Unity.FoxgloveSDK.Components;

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
        private readonly int _queueCapacity;
        private readonly int _reconnectIntervalMs;
        private readonly int _sendTimeoutMs;
        private readonly IRos2BridgeSink _ownedSink;
        private readonly Components.FoxRunTransportRetirementReservation _retirement;
        private readonly string _workerIdentity;
        private readonly object _gate = new object();
        private readonly object _retirementGate = new object();
        private readonly Queue<QueuedPublish> _queue;
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
        private long _connectionGeneration;
        private long _nextConnectAttemptUnixMs;
        private QueuedPublish _inFlightPublish;
        private bool _hasInFlightPublish;
        private InFlightPreparation _inFlightPreparation;
        private bool _hasInFlightPreparation;
        private bool _workerExited;
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
            _queueCapacity = queueCapacity;
            _reconnectIntervalMs = reconnectIntervalMs;
            _sendTimeoutMs = sendTimeoutMs;
            _ownedSink = ownedSink ?? throw new ArgumentNullException(nameof(ownedSink));
            _retirement = retirement ?? throw new ArgumentNullException(nameof(retirement));
            _workerIdentity = string.IsNullOrWhiteSpace(workerIdentity)
                ? throw new ArgumentException("Worker identity cannot be empty.", nameof(workerIdentity))
                : workerIdentity;
            _queue = new Queue<QueuedPublish>(queueCapacity);
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
                    if (_preparations.Count >= _queueCapacity)
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
            => TryEnqueueCore(frame, default, requiresPreparation: false, out reason);

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
            return TryEnqueueCore(frame, key, requiresPreparation: true, out reason);
        }

        private bool TryEnqueueCore(
            Ros2BridgeFrame frame,
            PublisherPreparationKey preparationKey,
            bool requiresPreparation,
            out string reason)
        {
            if (!TryValidateFrame(frame, out reason))
                return false;

            lock (_gate)
            {
                if (!_enabled)
                {
                    reason = "ROS2 Bridge is disabled.";
                    return false;
                }
                if (!_autoConnect)
                {
                    reason = "ROS2 Bridge auto-connect is disabled; connect before sending frames.";
                    return false;
                }

                if (requiresPreparation)
                {
                    if (!_preparations.TryGetValue(preparationKey, out var entry))
                    {
                        reason = "ROS2 Bridge publisher preparation was not registered.";
                        return false;
                    }
                    if (entry.Readiness != Ros2BridgePublisherReadiness.Ready
                        || entry.ReadyConnectionGeneration != _connectionGeneration)
                    {
                        reason = entry.Readiness == Ros2BridgePublisherReadiness.Rejected
                            ? entry.Reason
                            : "ROS2 Bridge publisher preparation is pending.";
                        return false;
                    }
                }

                if (_queue.Count >= _queueCapacity)
                {
                    _queue.Dequeue();
                    _droppedFrames++;
                }

                _queue.Enqueue(new QueuedPublish(
                    frame,
                    preparationKey,
                    requiresPreparation,
                    _connectionGeneration));
            }

            _signal.Set();
            return true;
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
            if (frame.PayloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                reason = "ROS2 Bridge payload exceeds the maximum size.";
                return false;
            }
            return true;
        }

        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
        {
            lock (_gate)
            {
                return new Ros2BridgeStatsSnapshot(
                    _enabled,
                    _connected,
                    _connecting,
                    _queue.Count,
                    _sentFrames,
                    _droppedFrames,
                    _failedFrames,
                    _lastError,
                    _lastConnectedUnixMs,
                    _lastDisconnectedUnixMs);
            }
        }

        internal bool StopAndJoin(int joinTimeoutMs)
        {
            Thread worker;
            lock (_gate)
            {
                _enabled = false;
                _stopRequested = true;
                _workerGeneration++;
                if (_queue.Count > 0)
                {
                    _droppedFrames += _queue.Count;
                    _queue.Clear();
                }
                _preparationQueue.Clear();
                _preparations.Clear();
                worker = _worker;
                _sink = null;
                _connected = false;
                _connecting = false;
                _lastDisconnectedUnixMs = NowUnixMs();
            }

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

                    if (!EnsureConnected(generation))
                    {
                        _signal.WaitOne(_reconnectIntervalMs);
                        continue;
                    }

                    if (ProcessNextPreparation(generation))
                        continue;

                    if (!TryDequeueFrame(out var queued))
                    {
                        _signal.WaitOne(50);
                        continue;
                    }

                    try
                    {
                        IRos2BridgeSink sink;
                        lock (_gate)
                        {
                            if (_stopRequested || !_enabled || generation != _workerGeneration)
                                return;
                            sink = _sink;
                        }

                        if (sink == null)
                        {
                            MarkFailure("ROS2 Bridge sink is not connected.", disconnect: true, countFrameFailure: false);
                            continue;
                        }

                        try
                        {
                            sink.Send(queued.Frame, _sendTimeoutMs);
                            lock (_gate)
                            {
                                if (generation != _workerGeneration)
                                    return;
                                _sentFrames++;
                                _lastError = string.Empty;
                            }
                        }
                        catch (Exception ex) when (IsRecoverableRuntimeException(ex))
                        {
                            MarkFailure(ex.Message, disconnect: true);
                        }
                    }
                    finally
                    {
                        ClearInFlightPublish(queued);
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
            lock (_gate)
            {
                if (_stopRequested || !_enabled || generation != _workerGeneration)
                    return false;
                if (_nextConnectAttemptUnixMs > NowUnixMs())
                    return false;
                if (_connected && _sink != null && _sink.IsConnected)
                    return true;
                _connecting = true;
            }

            try
            {
                _ownedSink.Connect(_host, _port, _sendTimeoutMs);
                lock (_gate)
                {
                    if (_stopRequested || !_enabled || generation != _workerGeneration)
                    {
                        _connected = false;
                        _connecting = false;
                        _lastDisconnectedUnixMs = NowUnixMs();
                        return false;
                    }
                    _sink = _ownedSink;
                    _connected = true;
                    _connecting = false;
                    _lastConnectedUnixMs = NowUnixMs();
                    _lastError = string.Empty;
                    _connectionGeneration++;
                    _nextConnectAttemptUnixMs = 0;
                    QueueAllPreparationsLocked();
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
                    DisconnectSink(_ownedSink);
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
                }

                fatal?.Throw();
                return false;
            }
        }

        private bool TryDequeueFrame(out QueuedPublish queued)
        {
            lock (_gate)
            {
                var candidates = _queue.Count;
                while (candidates-- > 0)
                {
                    var candidate = _queue.Dequeue();
                    if (!candidate.RequiresPreparation)
                    {
                        SetInFlightPublishLocked(candidate);
                        queued = candidate;
                        return true;
                    }

                    if (!_preparations.TryGetValue(
                            candidate.PreparationKey,
                            out var entry))
                    {
                        DropRejectedPreparedFrameLocked(
                            "ROS2 Bridge publisher preparation no longer exists.");
                        continue;
                    }
                    if (entry.Readiness == Ros2BridgePublisherReadiness.Rejected)
                    {
                        DropRejectedPreparedFrameLocked(entry.Reason);
                        continue;
                    }
                    if (entry.Readiness == Ros2BridgePublisherReadiness.Ready
                        && entry.ReadyConnectionGeneration == _connectionGeneration
                        && _connectionGeneration >= candidate.EnqueueConnectionGeneration)
                    {
                        SetInFlightPublishLocked(candidate);
                        queued = candidate;
                        return true;
                    }

                    _queue.Enqueue(candidate);
                }

                queued = default;
                return false;
            }
        }

        private void SetInFlightPublishLocked(QueuedPublish publish)
        {
            _inFlightPublish = publish;
            _hasInFlightPublish = true;
        }

        private void ClearInFlightPublish(QueuedPublish publish)
        {
            lock (_gate)
            {
                if (!_hasInFlightPublish
                    || !ReferenceEquals(
                        _inFlightPublish.Frame,
                        publish.Frame))
                {
                    return;
                }

                _inFlightPublish = default;
                _hasInFlightPublish = false;
            }
        }

        private void DropRejectedPreparedFrameLocked(string reason)
        {
            _droppedFrames++;
            _failedFrames++;
            _lastError = BoundRuntimeDiagnostic(
                string.IsNullOrWhiteSpace(reason)
                    ? "ROS2 Bridge publisher preparation was rejected."
                    : reason);
        }

        private bool ProcessNextPreparation(long workerGeneration)
        {
            PublisherPreparationKey key;
            PublisherPreparationEntry entry;
            IRos2BridgeSink sink;
            long connectionGeneration;
            string requestId;
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
                requestId = "u2r2-prepare-" + Guid.NewGuid().ToString("N");
                entry.RequestId = requestId;
                entry.Reason = "ROS2 Bridge publisher preparation is pending.";
            }

            if (!(sink is IRos2BridgePublisherPreparationTransport transport))
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
            try
            {
                request = Ros2BridgePublisherPreparationCodec.WriteRequest(
                    requestId,
                    key.Topic,
                    key.SchemaName,
                    key.Qos);
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
                var responseFrame = transport.ExchangePublisherPreparation(
                    request,
                    _sendTimeoutMs);
                var response = Ros2BridgePublisherPreparationCodec.ParseResponse(
                    responseFrame,
                    requestId);
                lock (_gate)
                {
                    if (!_preparations.TryGetValue(key, out var current)
                        || !ReferenceEquals(current, entry)
                        || connectionGeneration != _connectionGeneration
                        || !ReferenceEquals(sink, _sink)
                        || !string.Equals(current.RequestId, requestId, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (string.Equals(response.Status, "ok", StringComparison.Ordinal))
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Ready;
                        current.ReadyConnectionGeneration = connectionGeneration;
                        current.Reason = string.Empty;
                    }
                    else
                    {
                        current.Readiness = Ros2BridgePublisherReadiness.Rejected;
                        current.ReadyConnectionGeneration = 0;
                        current.Reason = string.IsNullOrWhiteSpace(response.Message)
                            ? "ROS2 Bridge publisher was rejected: " + response.ErrorCode
                            : response.Message;
                    }
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is FormatException
                || exception is OverflowException)
            {
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
                CountRetainedResource(_queue, ref retainedResources);
                CountRetainedResource(
                    _preparationQueue,
                    ref retainedResources);
                CountRetainedResource(
                    _preparations,
                    ref retainedResources);

                foreach (var queued in _queue)
                {
                    retainedResources = checked(retainedResources + 1);
                    retainedBytes = checked(
                        retainedBytes + queued.Frame.PayloadLength);
                }

                if (_hasInFlightPublish
                    && _inFlightPublish.Frame != null)
                {
                    retainedResources = checked(retainedResources + 1);
                    retainedBytes = checked(
                        retainedBytes
                        + _inFlightPublish.Frame.PayloadLength);
                }

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
            lock (_gate)
            {
                _queue.Clear();
                _preparationQueue.Clear();
                _preparations.Clear();
                _inFlightPublish = default;
                _hasInFlightPublish = false;
                _inFlightPreparation = default;
                _hasInFlightPreparation = false;
                _sink = null;
                _worker = null;
            }
            try
            {
                _ownedSink.Dispose();
            }
            catch (Exception exception) when (IsRecoverableRuntimeException(exception))
            {
                // A recoverable final cleanup failure must not leak the token.
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            try
            {
                _signal.Dispose();
            }
            catch (Exception exception) when (IsRecoverableRuntimeException(exception))
            {
                // The resource group is already terminal.
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }
            fatal?.Throw();
        }

        private void MarkFailure(string message, bool disconnect, bool countFrameFailure = true)
        {
            IRos2BridgeSink sink = null;
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
                    _nextConnectAttemptUnixMs = NowUnixMs() + _reconnectIntervalMs;
                    InvalidatePreparationsLocked();
                }
            }

            DisconnectSink(sink);
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

        private sealed class PublisherPreparationEntry
        {
            internal Ros2BridgePublisherReadiness Readiness =
                Ros2BridgePublisherReadiness.Pending;
            internal string RequestId = string.Empty;
            internal string Reason = "ROS2 Bridge publisher preparation is pending.";
            internal long ReadyConnectionGeneration;
            internal bool Queued;
        }

        private readonly struct QueuedPublish
        {
            internal QueuedPublish(
                Ros2BridgeFrame frame,
                PublisherPreparationKey preparationKey,
                bool requiresPreparation,
                long enqueueConnectionGeneration)
            {
                Frame = frame;
                PreparationKey = preparationKey;
                RequiresPreparation = requiresPreparation;
                EnqueueConnectionGeneration = enqueueConnectionGeneration;
            }

            internal Ros2BridgeFrame Frame { get; }
            internal PublisherPreparationKey PreparationKey { get; }
            internal bool RequiresPreparation { get; }
            internal long EnqueueConnectionGeneration { get; }
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
