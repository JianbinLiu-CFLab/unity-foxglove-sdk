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

        public FoxRunTransportCapabilities Capabilities =>
            FoxRunTransportCapabilities.Publish;

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
                _healthDrawer.Draw(providerObject);
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
