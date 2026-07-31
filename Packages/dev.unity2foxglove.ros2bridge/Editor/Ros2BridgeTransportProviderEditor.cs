// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor
// Purpose: Provider-owned inspector and undoable companion creation.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    [CustomEditor(typeof(Ros2BridgeTransportProvider))]
    internal sealed class Ros2BridgeTransportProviderEditor : UnityEditor.Editor
    {
        private const string AddMenu =
            "Tools/Unity2Foxglove/Providers/Add ROS2 Bridge Provider";

        [MenuItem(AddMenu, priority = 1810)]
        private static void AddProvider()
        {
            var target = Selection.activeGameObject;
            if (target == null || target.GetComponent<FoxgloveManager>() == null)
                return;

            var provider = target.GetComponent<Ros2BridgeTransportProvider>();
            if (provider == null)
                provider = Undo.AddComponent<Ros2BridgeTransportProvider>(target);

            PrefabUtility.RecordPrefabInstancePropertyModifications(provider);
            if (target.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(target.scene);
            Selection.activeObject = provider;
        }

        [MenuItem(AddMenu, validate = true)]
        private static bool CanAddProvider()
        {
            var target = Selection.activeGameObject;
            return target != null
                   && target.GetComponent<FoxgloveManager>() != null
                   && target.GetComponent<Ros2BridgeTransportProvider>() == null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "This hidden companion contributes the "
                + Ros2BridgeTransportProvider.ProviderId
                + " transport to the FoxgloveManager on the same GameObject.",
                MessageType.Info);

            Draw("_available", "Available");
            Draw("_autoConnect", "Auto Connect");
            Draw("_host", "Host");
            Draw("_port", "Port");
            Draw("_queueCapacity", "Queue Capacity");
            Draw("_reconnectIntervalMs", "Reconnect Interval (ms)");
            Draw("_sendTimeoutMs", "Send Timeout (ms)");
            serializedObject.ApplyModifiedProperties();

            if (!Application.isPlaying)
                return;

            var stats =
                ((Ros2BridgeTransportProvider)target).GetStatsSnapshot();
            Ros2BridgeProviderDrawer.DrawStats(stats);
        }

        private void Draw(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
