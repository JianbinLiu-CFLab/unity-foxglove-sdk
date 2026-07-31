// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge
// Purpose: Serialized Manager-facing shell for detached Bridge worker leases.

using System;
using System.Net;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2Bridge
{
    public enum Ros2BridgeRuntimeLifecycleState : byte
    {
        Stopped = 0,
        Starting = 1,
        Ready = 2,
        Stopping = 3
    }

    /// <summary>
    /// Manager-owned lifecycle shell for the bounded queue and reconnect
    /// worker. A timed-out lease is detached from this object and transferred
    /// to the generic retirement owner, so it cannot retain its Manager or
    /// Provider.
    /// </summary>
    public sealed class Ros2BridgeRuntime : IRos2BridgeSink
    {
        internal const int MaxRuntimeDiagnosticChars =
            Ros2BridgeWorkerLease.MaxRuntimeDiagnosticChars;
        private const string RetirementUnavailableReason =
            "ROS2 Bridge exclusive retirement ownership is unavailable.";
        private static long _nextStandaloneIdentity;

        private readonly string _host;
        private readonly int _port;
        private readonly int _queueCapacity;
        private readonly int _reconnectIntervalMs;
        private readonly int _sendTimeoutMs;
        private readonly int _joinTimeoutMs;
        private readonly FoxRunTransportRetirementOwner _retirementOwner;
        private readonly FoxRunTransportId _providerId;
        private readonly FoxRunTransportDirection _direction;
        private readonly ulong _generation;
        private readonly bool _enableDuplexSession;
        private readonly object _lifecycleGate = new object();

        private Func<IRos2BridgeSink> _sinkFactory;
        private Ros2BridgeWorkerLease _run;
        private Ros2BridgeStatsSnapshot _lastSnapshot =
            Ros2BridgeStatsSnapshot.Disabled;
        private volatile Ros2BridgeRuntimeLifecycleState _lifecycleState;
        private bool _configuredEnabled;
        private bool _configuredAutoConnect;
        private bool _hasStarted;
        private bool _disposed;

        public Ros2BridgeRuntime(
            string host,
            int port,
            int queueCapacity,
            int reconnectIntervalMs,
            int sendTimeoutMs,
            Func<IRos2BridgeSink> sinkFactory = null)
            : this(
                host,
                port,
                queueCapacity,
                reconnectIntervalMs,
                sendTimeoutMs,
                sinkFactory,
                FoxRunTransportRetirementOwner.Shared,
                StandaloneProviderId(),
                FoxRunTransportDirection.Publish,
                generation: 1,
                joinTimeoutMs: Math.Max(1000, sendTimeoutMs + 250))
        {
        }

        internal Ros2BridgeRuntime(
            string host,
            int port,
            int queueCapacity,
            int reconnectIntervalMs,
            int sendTimeoutMs,
            Func<IRos2BridgeSink> sinkFactory,
            FoxRunTransportRetirementOwner retirementOwner,
            FoxRunTransportId providerId,
            FoxRunTransportDirection direction,
            ulong generation,
            int joinTimeoutMs,
            bool enableDuplexSession = false)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (port <= 0 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            if (queueCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (reconnectIntervalMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(reconnectIntervalMs));
            if (sendTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(sendTimeoutMs));
            if (joinTimeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(joinTimeoutMs));
            if (direction != FoxRunTransportDirection.Publish
                && direction != FoxRunTransportDirection.Subscribe)
                throw new ArgumentOutOfRangeException(nameof(direction));

            _host = NormalizeLoopbackHost(host);
            _port = port;
            _queueCapacity = queueCapacity;
            _reconnectIntervalMs = reconnectIntervalMs;
            _sendTimeoutMs = sendTimeoutMs;
            _joinTimeoutMs = joinTimeoutMs;
            _enableDuplexSession =
                sinkFactory == null
                || enableDuplexSession;
            _sinkFactory = sinkFactory ?? CreateTcpSink;
            _retirementOwner = retirementOwner
                               ?? throw new ArgumentNullException(nameof(retirementOwner));
            _providerId = providerId;
            _direction = direction;
            _generation = generation;
        }

        public Ros2BridgeRuntimeLifecycleState LifecycleState
            => _lifecycleState;

        public bool IsConnected
        {
            get
            {
                lock (_lifecycleGate)
                    return _run?.IsConnected ?? false;
            }
        }

        internal bool HasInboundPipeline
        {
            get
            {
                lock (_lifecycleGate)
                    return _run?.HasInboundPipeline ?? false;
            }
        }

        public void Start(bool enabled, bool autoConnect)
        {
            if (!TryStart(enabled, autoConnect, out var reason))
                throw new InvalidOperationException(reason);
        }

        internal bool TryStart(
            bool enabled,
            bool autoConnect,
            out string reason)
        {
            lock (_lifecycleGate)
            {
                ThrowIfDisposed();
                _configuredEnabled = enabled;
                _configuredAutoConnect = autoConnect;
                if (!enabled || !autoConnect)
                {
                    if (_run != null)
                        StopCore();
                    _lastSnapshot = CreateIdleSnapshot(_lastSnapshot, enabled);
                    _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopped;
                    reason = string.Empty;
                    return true;
                }
                if (_lifecycleState == Ros2BridgeRuntimeLifecycleState.Ready
                    || _lifecycleState == Ros2BridgeRuntimeLifecycleState.Starting)
                {
                    reason = string.Empty;
                    return true;
                }

                _lifecycleState = Ros2BridgeRuntimeLifecycleState.Starting;
                if (!_retirementOwner.TryReserveExclusive(
                        _providerId,
                        _direction,
                        _generation,
                        workerCount:
                            _enableDuplexSession
                                ? 3
                                : 1,
                        out var reservation))
                {
                    _lifecycleState =
                        Ros2BridgeRuntimeLifecycleState.Stopped;
                    reason = RetirementUnavailableReason;
                    return false;
                }

                IRos2BridgeSink sink = null;
                Ros2BridgeWorkerLease run = null;
                try
                {
                    sink = _sinkFactory();
                    if (sink == null)
                        throw new InvalidOperationException(
                            "ROS2 Bridge sink factory returned null.");
                    reservation.WarmUpTimeoutConversionForCurrentThread();
                    var workerIdentity = _providerId.Value
                                         + "/"
                                         + _direction.ToString().ToLowerInvariant()
                                         + "/"
                                         + _generation;
                    run = new Ros2BridgeWorkerLease(
                        _host,
                        _port,
                        _queueCapacity,
                        _reconnectIntervalMs,
                        _sendTimeoutMs,
                        sink,
                        reservation,
                        workerIdentity,
                        requiresSubscription:
                            _direction
                            == FoxRunTransportDirection.Subscribe,
                        enableDuplexSession:
                            _enableDuplexSession,
                        _generation,
                        _lastSnapshot);
                    sink = null;
                    run.Start();
                    _hasStarted = true;
                    _run = run;
                    _lastSnapshot = run.GetStatsSnapshot();
                    _lifecycleState = Ros2BridgeRuntimeLifecycleState.Ready;
                    reason = string.Empty;
                    return true;
                }
                catch
                {
                    try
                    {
                        if (run != null)
                            run.Dispose();
                        else
                            sink?.Dispose();
                    }
                    finally
                    {
                        reservation.Dispose();
                        _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopped;
                    }
                    throw;
                }
            }
        }

        internal Ros2BridgePublisherReadiness PreparePublisher(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos,
            out string reason)
        {
            lock (_lifecycleGate)
            {
                if (_run == null)
                {
                    reason = GetUnavailableReason();
                    return Ros2BridgePublisherReadiness.Rejected;
                }
                return _run.PreparePublisher(topic, schemaName, qos, out reason);
            }
        }

        public bool TryEnqueue(Ros2BridgeFrame frame, out string reason)
        {
            lock (_lifecycleGate)
            {
                if (_run == null)
                {
                    RecordRejectedAfterStop();
                    reason = GetUnavailableReason();
                    return false;
                }
                return _run.TryEnqueue(frame, out reason);
            }
        }

        internal bool TryEnqueuePrepared(
            Ros2BridgeFrame frame,
            out string reason)
        {
            lock (_lifecycleGate)
            {
                if (_run == null)
                {
                    RecordRejectedAfterStop();
                    reason = GetUnavailableReason();
                    return false;
                }
                return _run.TryEnqueuePrepared(frame, out reason);
            }
        }

        public Ros2BridgeStatsSnapshot GetStatsSnapshot()
        {
            lock (_lifecycleGate)
                return _run?.GetStatsSnapshot() ?? _lastSnapshot;
        }

        public void Stop()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;
                if (_run != null)
                    StopCore();
                _configuredEnabled = false;
                _configuredAutoConnect = false;
                _lastSnapshot = CreateIdleSnapshot(_lastSnapshot, enabled: false);
                _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopped;
            }
        }

        private void StopCore()
        {
            var run = _run;
            _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopping;
            try
            {
                run.StopAndJoin(_joinTimeoutMs);
            }
            finally
            {
                _lastSnapshot = run.GetStatsSnapshot();
                _run = null;
                _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopped;
            }
        }

        public void Connect(string host, int port, int timeoutMs)
        {
            var normalizedHost = NormalizeLoopbackHost(host);
            if (!string.Equals(
                    normalizedHost,
                    _host,
                    StringComparison.OrdinalIgnoreCase)
                || port != _port)
            {
                throw new InvalidOperationException(
                    "ROS2 Bridge runtime Connect must use the configured endpoint.");
            }
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            Start(enabled: true, autoConnect: true);
        }

        public void Send(Ros2BridgeFrame frame, int timeoutMs)
        {
            if (timeoutMs <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (!TryEnqueue(frame, out var reason))
                throw new InvalidOperationException(reason);
        }

        public void Disconnect() => Stop();

        public void Dispose()
        {
            lock (_lifecycleGate)
            {
                if (_disposed)
                    return;
                try
                {
                    if (_run != null)
                        StopCore();
                }
                finally
                {
                    _configuredEnabled = false;
                    _configuredAutoConnect = false;
                    _lastSnapshot = CreateIdleSnapshot(
                        _lastSnapshot,
                        enabled: false);
                    _disposed = true;
                    _sinkFactory = null;
                    _lifecycleState = Ros2BridgeRuntimeLifecycleState.Stopped;
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Ros2BridgeRuntime));
        }

        private string GetUnavailableReason()
        {
            if (!_configuredEnabled)
                return "ROS2 Bridge is disabled.";
            if (!_configuredAutoConnect)
            {
                return "ROS2 Bridge auto-connect is disabled; "
                       + "connect before sending frames.";
            }
            return "ROS2 Bridge runtime is not ready.";
        }

        private static Ros2BridgeStatsSnapshot CreateIdleSnapshot(
            Ros2BridgeStatsSnapshot source,
            bool enabled)
        {
            return new Ros2BridgeStatsSnapshot(
                enabled,
                connected: false,
                connecting: false,
                queuedFrames: 0,
                source.SentFrames,
                source.DroppedFrames,
                source.FailedFrames,
                source.LastError,
                source.LastConnectedUnixMs,
                source.LastDisconnectedUnixMs,
                source.AcceptedFrames,
                source.ReplacedFrames,
                source.OversizeFrames,
                source.BackpressureRejectedFrames,
                source.RejectedAfterStopFrames,
                source.FaultedFrames,
                source.DisposalFailures,
                queuedBytes: 0,
                transientBytes: 0,
                inFlightBytes: 0);
        }

        private void RecordRejectedAfterStop()
        {
            if (!_hasStarted)
                return;
            _lastSnapshot = new Ros2BridgeStatsSnapshot(
                _lastSnapshot.Enabled,
                _lastSnapshot.Connected,
                _lastSnapshot.Connecting,
                _lastSnapshot.QueuedFrames,
                _lastSnapshot.SentFrames,
                _lastSnapshot.DroppedFrames,
                _lastSnapshot.FailedFrames,
                _lastSnapshot.LastError,
                _lastSnapshot.LastConnectedUnixMs,
                _lastSnapshot.LastDisconnectedUnixMs,
                _lastSnapshot.AcceptedFrames,
                _lastSnapshot.ReplacedFrames,
                _lastSnapshot.OversizeFrames,
                _lastSnapshot.BackpressureRejectedFrames,
                _lastSnapshot.RejectedAfterStopFrames == long.MaxValue
                    ? long.MaxValue
                    : _lastSnapshot.RejectedAfterStopFrames + 1,
                _lastSnapshot.FaultedFrames,
                _lastSnapshot.DisposalFailures,
                _lastSnapshot.QueuedBytes,
                _lastSnapshot.TransientBytes,
                _lastSnapshot.InFlightBytes);
        }

        private static IRos2BridgeSink CreateTcpSink()
            => new Ros2BridgeTcpClient();

        private static FoxRunTransportId StandaloneProviderId()
            => new FoxRunTransportId(
                "unity2foxglove.ros2bridge.runtime-"
                + Interlocked.Increment(ref _nextStandaloneIdentity));

        private static string NormalizeLoopbackHost(string host)
        {
            Ros2BridgeTcpClient.ValidateLoopbackHost(host);
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                return "127.0.0.1";
            return IPAddress.TryParse(host, out var address)
                ? address.ToString()
                : host.Trim();
        }
    }
}
