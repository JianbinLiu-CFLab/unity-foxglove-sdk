// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Provides FoxgloveManager ROS2 Bridge sidecar publish helpers.

using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private bool _foxRunRos2BridgeRuntimeDemand;

        /// <summary>
        /// Returns the latest ROS2 Bridge runtime stats for Inspector and diagnostics.
        /// </summary>
        public Ros2BridgeStatsSnapshot GetRos2BridgeStatsSnapshot()
        {
            if (_ros2BridgeRuntime != null)
                return _ros2BridgeRuntime.GetStatsSnapshot();

            if (!string.IsNullOrEmpty(_connectionState.Ros2BridgeSetupError))
            {
                return new Ros2BridgeStatsSnapshot(
                    enabled: false,
                    connected: false,
                    connecting: false,
                    queuedFrames: 0,
                    sentFrames: 0,
                    droppedFrames: 0,
                    failedFrames: 0,
                    lastError: _connectionState.Ros2BridgeSetupError,
                    lastConnectedUnixMs: 0,
                    lastDisconnectedUnixMs: 0);
            }

            return Ros2BridgeStatsSnapshot.Disabled;
        }

        /// <summary>
        /// Return whether a publisher should prepare a ROS2 Bridge payload.
        /// This path is independent of the Foxglove WebSocket server and subscriber demand.
        /// </summary>
        /// <param name="topic">ROS 2 topic name.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="reason">Human-readable skip reason when false.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareRos2BridgePublish(string topic, string schemaName, out string reason)
            => TryPrepareRos2BridgePublish(topic, string.Empty, schemaName, out _, out _, out reason);

        /// <summary>
        /// Return whether a publisher should prepare a ROS2 Bridge payload and resolve bridge-only topic/QoS metadata.
        /// </summary>
        /// <param name="topic">Publisher WebSocket topic name.</param>
        /// <param name="topicOverride">Optional absolute ROS 2 bridge topic override.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="effectiveTopic">Resolved ROS 2 bridge topic.</param>
        /// <param name="qos">Resolved ROS 2 bridge QoS profile.</param>
        /// <param name="reason">Human-readable skip reason when false.</param>
        /// <returns>True when payload preparation should continue.</returns>
        public bool TryPrepareRos2BridgePublish(
            string topic,
            string topicOverride,
            string schemaName,
            out string effectiveTopic,
            out FoxRunResolvedQos qos,
            out string reason)
        {
            effectiveTopic = string.Empty;
            qos = default;
            reason = string.Empty;

            if (SuppressLivePublishersForReplay)
            {
                reason = "Replay is suppressing live publishers.";
                return false;
            }

            if (!_ros2BridgeEnabled)
            {
                reason = "ROS2 Bridge is disabled.";
                return false;
            }

            qos = ActiveFoxRunBridgePublishQos;

            if (_ros2BridgeRuntime == null)
            {
                reason = string.IsNullOrEmpty(_connectionState.Ros2BridgeSetupError)
                    ? "ROS2 Bridge runtime is unavailable."
                    : _connectionState.Ros2BridgeSetupError;
                return false;
            }

            if (!TryResolveRos2BridgeTopic(topic, topicOverride, out effectiveTopic, out reason))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(schemaName))
            {
                reason = "ROS2 Bridge schema name is required.";
                return false;
            }

            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out _))
            {
                reason = $"Unknown ROS2 Bridge schema '{schemaName}'.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Phase184 FoxRun readiness gate for an exact canonical ROS 2 type.
        /// Unlike the maintained packaged-message helper, custom generated
        /// interfaces are not required to exist in the bundled serializer
        /// catalog; the connected sidecar owns typesupport lookup.
        /// </summary>
        public bool TryPrepareFoxRunRos2BridgePublish(
            string topic,
            string schemaName,
            FoxRunResolvedQos qos,
            out string effectiveTopic,
            out string reason)
        {
            effectiveTopic = string.Empty;
            reason = string.Empty;
            if (SuppressLivePublishersForReplay)
            {
                reason = "Replay is suppressing live publishers.";
                return false;
            }
            if (!TryResolveRos2BridgeTopic(topic, string.Empty, out effectiveTopic, out reason))
                return false;
            if (!FoxRunRos2InterfaceIdentity.IsValidCanonicalRosMessageType(schemaName))
            {
                reason = "ROS2 Bridge schema must be an exact canonical package/msg/Message type.";
                return false;
            }

            if (!Ros2BridgeFrame.IsValidResolvedQos(qos))
            {
                reason = "ROS2 Bridge QoS must be a fully resolved portable contract.";
                return false;
            }
            if (!EnsureFoxRunRos2BridgeRuntimeDemand(out reason))
                return false;
            try
            {
                var readiness = _ros2BridgeRuntime.PreparePublisher(
                    effectiveTopic,
                    schemaName,
                    qos,
                    out reason);
                return readiness == Ros2BridgePublisherReadiness.Ready;
            }
            catch (System.ArgumentException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        private bool EnsureFoxRunRos2BridgeRuntimeDemand(out string reason)
        {
            // FoxRun's frozen/explicit Bridge target is independent from the
            // legacy component-output master switch.
            if (!ActiveFoxRunPublishSessionPolicy.SessionActive)
            {
                reason = "FoxRun publish session is not active.";
                return false;
            }
            if (!isActiveAndEnabled)
            {
                reason = "FoxgloveManager is not active and enabled.";
                return false;
            }
            if (_ros2BridgeRuntime == null)
                CreateRos2BridgeRuntime();
            if (_ros2BridgeRuntime == null)
            {
                reason = string.IsNullOrEmpty(_connectionState.Ros2BridgeSetupError)
                    ? "ROS2 Bridge runtime is unavailable."
                    : _connectionState.Ros2BridgeSetupError;
                return false;
            }

            _ros2BridgeRuntime.Start(
                enabled: true,
                autoConnect: _ros2BridgeAutoConnect);
            _foxRunRos2BridgeRuntimeDemand = true;
            reason = string.Empty;
            return true;
        }

        private void ReleaseFoxRunRos2BridgeRuntimeDemand()
        {
            _foxRunRos2BridgeRuntimeDemand = false;
            if (!_ros2BridgeEnabled)
                _ros2BridgeRuntime?.Stop();
        }

        /// <summary>Publish one already generated XCDR1 payload to the selected Bridge target.</summary>
        public bool TryPublishFoxRunRos2BridgeCdr(
            string topic,
            string schemaName,
            byte[] payload,
            ulong logTimeNs,
            FoxRunResolvedQos qos,
            out string reason)
        {
            if (!TryPrepareFoxRunRos2BridgePublish(
                    topic,
                    schemaName,
                    qos,
                    out var effectiveTopic,
                    out reason))
            {
                return false;
            }

            try
            {
                Ros2CdrPayloadValidator.Validate(payload);
                var frame = Ros2BridgeFrame.CreateValidated(
                    effectiveTopic,
                    schemaName,
                    CdrEncoding,
                    logTimeNs,
                    _connectionState.NextRos2BridgeSequence(),
                    payload,
                    qos);
                return _ros2BridgeRuntime.TryEnqueuePrepared(frame, out reason);
            }
            catch (System.ArgumentException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Prepare a hidden MCAP CDR channel for a ROS-only FoxRun declaration.
        /// Recording demand is independent from live Foxglove target selection.
        /// </summary>
        public bool TryPrepareFoxRunRos2Recording(
            string topic,
            string schemaName,
            string schemaContent,
            out uint channelId,
            out string reason)
        {
            channelId = 0;
            reason = string.Empty;
            if (SuppressLivePublishersForReplay)
            {
                reason = "Replay is suppressing live publishers.";
                return false;
            }
            if (_runtime == null || !_runtime.IsRunning || !_runtime.RecordingEnabled)
            {
                // No active recorder is the normal case. Returning an empty
                // reason keeps the target-aware hub silent while preserving
                // diagnostics for actual configuration and publish failures.
                return false;
            }
            if (!IsValidPublishTopic(topic))
            {
                reason = "FoxRun recording topic is invalid.";
                return false;
            }
            if (!FoxRunRos2InterfaceIdentity.IsValidCanonicalRosMessageType(schemaName))
            {
                reason = "FoxRun recording schema must be an exact canonical package/msg/Message type.";
                return false;
            }
            if (string.IsNullOrEmpty(schemaContent))
            {
                if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out var packaged))
                {
                    reason = "FoxRun recording schema content is unavailable.";
                    return false;
                }
                schemaContent = packaged.Content;
            }

            var key = (topic, schemaName);
            if (!_foxRunRecordingChannelCache.TryGetValue(key, out channelId))
            {
                channelId = (uint)_connectionState.NextChannelId++;
                _foxRunRecordingChannelCache[key] = channelId;
            }

            // Reassert the immutable descriptor on every preparation. The
            // session makes this idempotent for the current recorder, while a
            // replacement recorder or a newly-allowing MCAP filter receives
            // the cached channel before demand is reported.
            _runtime.RegisterRecordingOnlyChannel(new Protocol.AdvertiseChannel
            {
                Id = channelId,
                Topic = topic,
                Encoding = CdrEncoding,
                SchemaName = schemaName,
                SchemaEncoding = Ros2MsgSchemaEncoding,
                Schema = schemaContent
            });

            if (_runtime.HasRecordingDemand(channelId))
                return true;
            reason = "MCAP recorder or recording filter rejected the FoxRun channel.";
            return false;
        }

        public bool TryPublishFoxRunRos2Recording(
            string topic,
            string schemaName,
            string schemaContent,
            byte[] payload,
            ulong logTimeNs,
            out string reason)
        {
            if (!TryPrepareFoxRunRos2Recording(
                    topic,
                    schemaName,
                    schemaContent,
                    out var channelId,
                    out reason))
            {
                return false;
            }

            try
            {
                Ros2CdrPayloadValidator.Validate(payload);
                if (_runtime.PublishRecordingOnlyRos2Cdr(channelId, payload, logTimeNs))
                    return true;
                reason = "MCAP recorder stopped accepting the FoxRun channel.";
                return false;
            }
            catch (System.ArgumentException exception)
            {
                reason = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Mirrors an already serialized ROS 2 CDR payload to the optional ROS2 Bridge sidecar.
        /// </summary>
        /// <param name="topic">ROS 2 topic name.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="payload">Serialized CDR payload, including little-endian encapsulation header.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishRos2BridgeCdr(string topic, string schemaName, byte[] payload, ulong logTimeNs)
            => PublishRos2BridgeCdr(topic, string.Empty, schemaName, payload, logTimeNs);

        /// <summary>
        /// Mirrors an already serialized ROS 2 CDR payload to the optional ROS2 Bridge sidecar.
        /// </summary>
        /// <param name="topic">Publisher WebSocket topic name.</param>
        /// <param name="topicOverride">Optional absolute ROS 2 bridge topic override.</param>
        /// <param name="schemaName">ROS 2 interface schema name.</param>
        /// <param name="payload">Serialized CDR payload, including little-endian encapsulation header.</param>
        /// <param name="logTimeNs">Nanosecond log timestamp.</param>
        public void PublishRos2BridgeCdr(string topic, string topicOverride, string schemaName, byte[] payload, ulong logTimeNs)
        {
            if (!TryPrepareRos2BridgePublish(topic, topicOverride, schemaName, out var effectiveTopic, out var qos, out var reason))
            {
                if (!string.IsNullOrWhiteSpace(reason))
                    WarnRos2BridgePublishSkipped(reason);
                return;
            }

            Ros2CdrPayloadValidator.Validate(payload);

            var frame = Ros2BridgeFrame.CreateValidated(
                effectiveTopic,
                schemaName,
                CdrEncoding,
                logTimeNs,
                _connectionState.NextRos2BridgeSequence(),
                payload,
                qos);

            if (!_ros2BridgeRuntime.TryEnqueue(frame, out var enqueueReason))
                WarnRos2BridgePublishSkipped(enqueueReason);
        }

        private void WarnRos2BridgePublishSkipped(string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? "unknown reason" : reason;
            var nowTicks = System.DateTime.UtcNow.Ticks;
            var key = "ros2-bridge:" + reason;
            lock (_warningDebounceState.Ros2BridgePublishWarningGate)
            {
                if (!WarningDebouncer.ShouldEmitKeyedCooldown(
                        key,
                        _warningDebounceState.LastRos2BridgePublishWarningKey,
                        _warningDebounceState.LastRos2BridgePublishWarningTicks,
                        nowTicks,
                        ClientEventOverflowWarningIntervalTicks))
                {
                    return;
                }

                _warningDebounceState.LastRos2BridgePublishWarningKey = key;
                _warningDebounceState.LastRos2BridgePublishWarningTicks = nowTicks;
            }
            Debug.LogWarning("[Foxglove] ROS2 Bridge publish skipped: " + reason);
        }
    }
}
