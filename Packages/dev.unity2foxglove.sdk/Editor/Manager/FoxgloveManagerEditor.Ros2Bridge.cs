// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private readonly Ros2BridgeHealthDrawer _ros2BridgeHealthDrawer = new Ros2BridgeHealthDrawer();

        private void DrawRos2BridgeSection()
        {
            DrawProperty("_ros2BridgeEnabled", "Enabled");
            DrawProperty("_ros2BridgeHost", "Host");
            DrawProperty("_ros2BridgePort", "Port");
            DrawProperty("_ros2BridgeAutoConnect", "Auto Connect");
            DrawProperty("_defaultRos2BridgeOutputEnabled", "Default Output");
            DrawProperty("_allowPublisherRos2BridgeOverride", "Allow Publisher Override");
            DrawProperty("_ros2BridgeNamespace", "Bridge Namespace");

            var qosPreset = serializedObject.FindProperty("_ros2BridgeQosPreset");
            PublisherEncodingEditorLabels.DrawRos2BridgeQosPreset(qosPreset, "QoS Preset");
            var custom = qosPreset != null && qosPreset.enumValueIndex == (int)Ros2BridgeQosPreset.Custom;
            if (custom)
            {
                FoxgloveManagerInspectorLayout.Subheader("Advanced QoS");
                DrawProperty("_ros2BridgeCustomReliability", "Reliability");
                DrawProperty("_ros2BridgeCustomDurability", "Durability");
                DrawProperty("_ros2BridgeCustomDepth", "Depth");
            }

            DrawProperty("_ros2BridgeQueueCapacity", "Queue Capacity");
            DrawProperty("_ros2BridgeReconnectIntervalMs", "Reconnect Interval Ms");
            DrawProperty("_ros2BridgeSendTimeoutMs", "Send Timeout Ms");

            EditorGUILayout.HelpBox(
                "ROS2 Bridge is optional, disabled by default, and mirrors supported publisher payloads to a local bridge sidecar. Use loopback hosts only.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Changing QoS for an existing bridge topic requires restarting the sidecar or using a new bridge topic.",
                MessageType.Info);

            var manager = (Components.FoxgloveManager)target;
            if (manager == null)
                return;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Effective QoS", manager.ResolveRos2BridgeQos().DisplaySummary);
            }

            var stats = manager.GetRos2BridgeStatsSnapshot();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Enabled", stats.Enabled);
                EditorGUILayout.Toggle("Connected", stats.Connected);
                EditorGUILayout.Toggle("Connecting", stats.Connecting);
                EditorGUILayout.IntField("Queued Frames", stats.QueuedFrames);
                EditorGUILayout.LongField("Sent Frames", stats.SentFrames);
                EditorGUILayout.LongField("Dropped Frames", stats.DroppedFrames);
                EditorGUILayout.LongField("Failed Frames", stats.FailedFrames);
                EditorGUILayout.TextField("Last Error", stats.LastError);
            }

            EditorGUILayout.Space();
            _ros2BridgeHealthDrawer.Draw(serializedObject);
        }

        private void OnDisable()
        {
            _ros2BridgeHealthDrawer.Dispose();
        }
    }
}
