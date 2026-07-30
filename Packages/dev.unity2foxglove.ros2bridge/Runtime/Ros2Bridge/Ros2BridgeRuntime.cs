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
    /// <summary>Manager-owned background sender with bounded queueing and reconnect lifecycle for ROS2 Bridge frames.</summary>
    public sealed class Ros2BridgeRuntime : IRos2BridgeSink
    {
        internal const int MaxRuntimeDiagnosticChars = 512;
        private const string PreparationCapacityReason =
            "ROS2 Bridge publisher preparation capacity is exhausted.";
        private readonly string _host;
        private readonly int _port;
        private readonly int _queueCapacity;
        private readonly int _reconnectIntervalMs;
        private readonly int _sendTimeoutMs;
        private readonly Func<IRos2BridgeSink> _sinkFactory;
        private readonly object _gate = new object();
        private readonly object _lifecycleGate = new object();
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
        private bool _disposed;

        public Ros2BridgeRuntime(
            string host,
            int port,
            int queueCapacity,
            int reconnectIntervalMs,
            int sendTimeoutMs,
            Func<IRos2BridgeSink> sinkFactory = null)
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
            _sinkFactory = sinkFactory ?? (() => new Ros2BridgeTcpClient());
            _queue = new Queue<QueuedPublish>(queueCapacity);
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

        public void Start(bool enabled, bool autoConnect)
        {
            lock (_gate)
            {
                _enabled = enabled;
                _autoConnect = autoConnect;
                _stopRequested = false;
                if (_worker != null && !_worker.IsAlive)
                    _worker = null;
                if (!_enabled || !_autoConnect || _worker != null)
                    return;

                var generation = ++_workerGeneration;
                _worker = new Thread(() => WorkerLoop(generation))
                {
                    IsBackground = true,
                    Name = "Unity2Foxglove ROS2 Bridge"
                };
                _worker.Start();
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

        public void Stop()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;
                StopCore();
            }
        }

        private void StopCore()
        {
            Thread worker;
            IRos2BridgeSink sinkToClose;
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
                sinkToClose = _sink;
                _sink = null;
                _connected = false;
                _connecting = false;
                _lastDisconnectedUnixMs = NowUnixMs();
            }

            ExceptionDispatchInfo fatal = null;
            try
            {
                CloseSink(sinkToClose);
            }
            catch (Exception exception)
            {
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            _signal.Set();
            var joinTimeoutMs = Math.Max(1000, _sendTimeoutMs + 250);
            if (worker != null && worker.IsAlive && !worker.Join(joinTimeoutMs))
            {
                lock (_gate)
                {
                    _lastError = "ROS2 Bridge worker did not stop within timeout.";
                    if (_worker == worker)
                        _worker = null;
                }
            }

            lock (_gate)
            {
                if (_worker == worker && (worker == null || !worker.IsAlive))
                    _worker = null;
            }
            fatal?.Throw();
        }

        /// <summary>
        /// Enables the background worker for the configured endpoint. The runtime uses its
        /// constructor timeout for worker connect attempts; <paramref name="timeoutMs"/> is
        /// validated for IRos2BridgeSink compatibility.
        /// </summary>
        public void Connect(string host, int port, int timeoutMs)
        {
            var normalizedHost = NormalizeLoopbackHost(host);
            if (!string.Equals(normalizedHost, _host, StringComparison.OrdinalIgnoreCase) || port != _port)
            {
                throw new InvalidOperationException(
                    "ROS2 Bridge runtime Connect must use the configured host and port; create a new runtime for a different endpoint.");
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS2 Bridge connect timeout must be positive.");
            // The worker uses the constructor timeout; the interface timeout is validated for sink compatibility.
            Start(enabled: true, autoConnect: true);
        }

        /// <summary>
        /// Enqueues <paramref name="frame"/> for asynchronous worker delivery. The runtime
        /// uses its constructor timeout for the actual transport send; <paramref name="timeoutMs"/>
        /// is validated for IRos2BridgeSink compatibility.
        /// </summary>
        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs), "ROS2 Bridge send timeout must be positive.");
            // Transport send timeout is owned by the background worker so this enqueue stays non-blocking.
            if (!TryEnqueue(frame, out var reason))
                throw new InvalidOperationException(reason);
        }

        /// <summary>Stops the background worker and clears queued frames without disposing this reusable runtime.</summary>
        public void Disconnect()
        {
            // Dispose owns the wait handle; Disconnect is a non-terminal sink stop so Connect can start the worker again.
            Stop();
        }

        public void Dispose()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;
                try
                {
                    StopCore();
                }
                finally
                {
                    _disposed = true;
                    _signal.Dispose();
                }
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

            IRos2BridgeSink sink = null;
            try
            {
                sink = _sinkFactory();
                sink.Connect(_host, _port, _sendTimeoutMs);
                IRos2BridgeSink previousSink = null;
                var canInstall = false;
                lock (_gate)
                {
                    if (_stopRequested || !_enabled || generation != _workerGeneration)
                    {
                        _connected = false;
                        _connecting = false;
                        _lastDisconnectedUnixMs = NowUnixMs();
                    }
                    else
                    {
                        previousSink = _sink;
                        _sink = null;
                        _connected = false;
                        canInstall = true;
                    }
                }

                if (!canInstall)
                {
                    var rejectedSink = sink;
                    sink = null;
                    CloseSink(rejectedSink);
                    return false;
                }

                // Retire the old connection before publishing the replacement
                // into shared state. A fatal close therefore cannot leave a
                // new sink reported as connected while the worker exits.
                var retiredSink = previousSink;
                previousSink = null;
                CloseSink(retiredSink);

                lock (_gate)
                {
                    if (_stopRequested || !_enabled || generation != _workerGeneration)
                    {
                        _connected = false;
                        _connecting = false;
                        _lastDisconnectedUnixMs = NowUnixMs();
                    }
                    else
                    {
                        _sink = sink;
                        sink = null;
                        _connected = true;
                        _connecting = false;
                        _lastConnectedUnixMs = NowUnixMs();
                        _lastError = string.Empty;
                        _connectionGeneration++;
                        _nextConnectAttemptUnixMs = 0;
                        QueueAllPreparationsLocked();
                    }
                }

                if (sink == null)
                    return true;

                var supersededSink = sink;
                sink = null;
                CloseSink(supersededSink);
                return false;
            }
            catch (Exception ex)
            {
                ExceptionDispatchInfo fatal = IsRecoverableRuntimeException(ex)
                    ? null
                    : ExceptionDispatchInfo.Capture(ex);
                try
                {
                    var failedCandidate = sink;
                    sink = null;
                    CloseSink(failedCandidate);
                }
                catch (Exception cleanupException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(cleanupException);
                }

                IRos2BridgeSink installedSink;
                lock (_gate)
                {
                    _connected = false;
                    _connecting = false;
                    _lastDisconnectedUnixMs = NowUnixMs();
                    _nextConnectAttemptUnixMs =
                        NowUnixMs() + _reconnectIntervalMs;
                    _lastError = BoundRuntimeDiagnostic(ex.Message);
                    InvalidatePreparationsLocked();
                    installedSink = _sink;
                    _sink = null;
                }
                try
                {
                    CloseSink(installedSink);
                }
                catch (Exception cleanupException)
                {
                    fatal ??= ExceptionDispatchInfo.Capture(cleanupException);
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
                        queued = candidate;
                        return true;
                    }

                    _queue.Enqueue(candidate);
                }

                queued = default;
                return false;
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

            try
            {
                var request = Ros2BridgePublisherPreparationCodec.WriteRequest(
                    requestId,
                    key.Topic,
                    key.SchemaName,
                    key.Qos);
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

            return true;
        }

        private bool ShouldStop(long generation)
        {
            lock (_gate)
                return _stopRequested || generation != _workerGeneration;
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

            CloseSink(sink);
        }

        internal static void CloseSink(IRos2BridgeSink sink)
        {
            if (sink == null)
                return;

            ExceptionDispatchInfo fatal = null;
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
                fatal = ExceptionDispatchInfo.Capture(exception);
            }
            try
            {
                sink.Dispose();
            }
            catch (Exception exception) when (
                IsRecoverableRuntimeException(exception))
            {
                // Shutdown is best-effort; state has already been updated.
            }
            catch (Exception exception)
            {
                fatal ??= ExceptionDispatchInfo.Capture(exception);
            }

            fatal?.Throw();
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
