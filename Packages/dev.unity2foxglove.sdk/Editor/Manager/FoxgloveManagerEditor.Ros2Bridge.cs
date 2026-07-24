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
            DrawProperty("_ros2BridgeHost", "Host");
            DrawProperty("_ros2BridgePort", "Port");
            DrawProperty("_ros2BridgeAutoConnect", "Auto Connect");
            DrawProperty("_defaultRos2BridgeOutputEnabled", "Default Output");
            DrawProperty("_allowPublisherRos2BridgeOverride", "Allow Publisher Override");
            DrawProperty("_ros2BridgeNamespace", "Bridge Namespace");

            DrawFoxRunRos2Qos(
                serializedObject.FindProperty("_ros2BridgeQos"),
                "ROS 2 QoS Profile");

            DrawProperty("_ros2BridgeQueueCapacity", "Queue Capacity");
            DrawProperty("_ros2BridgeReconnectIntervalMs", "Reconnect Interval Ms");
            DrawProperty("_ros2BridgeSendTimeoutMs", "Send Timeout Ms");

            EditorGUILayout.HelpBox(
                "ROS2 Bridge is optional, disabled by default, and mirrors supported publisher payloads to a local bridge sidecar. Use loopback hosts only.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Changing ROS 2 Bridge QoS takes effect after disabling and re-enabling the Manager.",
                MessageType.Info);

            var manager = (Components.FoxgloveManager)target;
            if (manager == null)
                return;

            using (new EditorGUI.DisabledScope(true))
            {
                RefreshRos2BridgeStatsForRepaint(manager);
                EditorGUILayout.TextField(
                    "Effective QoS",
                    FoxRunRos2SubscriptionInspectorPresentation.Summary(
                        _ros2BridgeQosThisRepaint));
            }

            var stats = _ros2BridgeStatsThisRepaint;
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

        private void RefreshRos2BridgeStatsForRepaint(Components.FoxgloveManager manager)
        {
            if (_ros2BridgeStatsFrame == Time.frameCount)
                return;

            _ros2BridgeStatsFrame = Time.frameCount;
            _ros2BridgeQosThisRepaint = manager != null
                ? manager.ActiveFoxRunBridgePublishQos
                : Components.FoxRunResolvedQos.Default;
            _ros2BridgeStatsThisRepaint = manager != null
                ? manager.GetRos2BridgeStatsSnapshot()
                : Ros2BridgeStatsSnapshot.Disabled;
        }
    }
}
