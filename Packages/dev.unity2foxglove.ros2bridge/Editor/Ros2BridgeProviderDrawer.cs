// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Editor
// Purpose: Manager Inspector contribution for the ROS 2 Bridge Provider.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    [InitializeOnLoad]
    internal sealed class Ros2BridgeProviderDrawer :
        IFoxRunTransportProviderDrawer
    {
        private readonly Ros2BridgeHealthDrawer
            _healthDrawer =
                new Ros2BridgeHealthDrawer();

        static Ros2BridgeProviderDrawer()
        {
            FoxRunTransportProviderDrawerRegistry.Register(
                new Ros2BridgeProviderDrawer());
        }

        public string TransportId =>
            Ros2BridgeTransportProvider.ProviderId;

        public string DisplayName =>
            "ROS 2 Bridge";

        public int Order => 300;

        public FoxRunTransportCapabilities Capabilities =>
            FoxRunTransportCapabilities.Publish
            | FoxRunTransportCapabilities.Subscribe;

        public void EnsureProvider(FoxgloveManager manager)
        {
            if (manager == null
                || manager.GetComponent<
                    Ros2BridgeTransportProvider>() != null)
            {
                return;
            }

            var provider =
                Undo.AddComponent<
                    Ros2BridgeTransportProvider>(
                    manager.gameObject);
            PrefabUtility
                .RecordPrefabInstancePropertyModifications(
                    provider);
            if (manager.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(
                    manager.gameObject.scene);
            }
        }

        public void Draw(
            FoxgloveManager manager,
            SerializedObject managerObject)
        {
            _ = managerObject;
            if (manager == null)
                return;

            var provider =
                manager.GetComponent<
                    Ros2BridgeTransportProvider>();
            if (provider == null)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                DisplayName,
                EditorStyles.boldLabel);
            using (var providerObject =
                   new SerializedObject(provider))
            {
                providerObject.Update();
                DrawProperty(
                    providerObject,
                    "_available",
                    "Available");
                DrawProperty(
                    providerObject,
                    "_autoConnect",
                    "Auto Connect");
                DrawProperty(providerObject, "_host", "Host");
                DrawProperty(providerObject, "_port", "Port");
                DrawProperty(
                    providerObject,
                    "_queueCapacity",
                    "Queue Capacity");
                DrawProperty(
                    providerObject,
                    "_reconnectIntervalMs",
                    "Reconnect Interval (ms)");
                DrawProperty(
                    providerObject,
                    "_sendTimeoutMs",
                    "Send Timeout (ms)");
                providerObject.ApplyModifiedProperties();
                if (Application.isPlaying)
                    DrawStats(provider.GetStatsSnapshot());
                _healthDrawer.Draw(providerObject);
            }
        }

        internal static void DrawStats(
            Ros2BridgeStatsSnapshot stats)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "ROS 2 Bridge Session",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(
                    "Connected",
                    stats.Connected);
                EditorGUILayout.IntField(
                    "Queued Frames",
                    stats.QueuedFrames);
                EditorGUILayout.LongField(
                    "Queued Bytes",
                    stats.QueuedBytes);
                EditorGUILayout.LongField(
                    "Transient Bytes",
                    stats.TransientBytes);
                EditorGUILayout.LongField(
                    "In-Flight Bytes",
                    stats.InFlightBytes);
                EditorGUILayout.LongField(
                    "Accepted Frames",
                    stats.AcceptedFrames);
                EditorGUILayout.LongField(
                    "Sent Frames",
                    stats.SentFrames);
                EditorGUILayout.LongField(
                    "Dropped Frames",
                    stats.DroppedFrames);
                EditorGUILayout.LongField(
                    "Replaced Frames",
                    stats.ReplacedFrames);
                EditorGUILayout.LongField(
                    "Oversize Frames",
                    stats.OversizeFrames);
                EditorGUILayout.LongField(
                    "Backpressure Rejections",
                    stats.BackpressureRejectedFrames);
                EditorGUILayout.LongField(
                    "After-Stop Rejections",
                    stats.RejectedAfterStopFrames);
                EditorGUILayout.LongField(
                    "Failed Frames",
                    stats.FailedFrames);
                EditorGUILayout.LongField(
                    "Faulted Frames",
                    stats.FaultedFrames);
                EditorGUILayout.LongField(
                    "Disposal Failures",
                    stats.DisposalFailures);
            }
            if (!string.IsNullOrEmpty(stats.LastError))
            {
                EditorGUILayout.HelpBox(
                    stats.LastError,
                    MessageType.Warning);
            }
        }

        private static void DrawProperty(
            SerializedObject serializedObject,
            string propertyName,
            string label)
        {
            var property =
                serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(
                    property,
                    new GUIContent(label),
                    includeChildren: true);
            }
        }
    }
}
