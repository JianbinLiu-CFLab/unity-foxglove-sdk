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
    internal sealed partial class Ros2BridgeWorkerLease :
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

    }
}
