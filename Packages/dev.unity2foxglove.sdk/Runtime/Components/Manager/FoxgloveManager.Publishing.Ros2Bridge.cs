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
            out Ros2BridgeQosProfile qos,
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

            qos = ResolveRos2BridgeQos();

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
