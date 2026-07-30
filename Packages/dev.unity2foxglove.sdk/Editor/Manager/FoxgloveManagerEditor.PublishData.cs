// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Provider-neutral publish controls and Provider editor extensions.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawPublishDataSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("Publish Destinations");
            var destinations =
                FindCachedProperty("_foxRunPublishTransportIds");
            DrawProperty(
                "_foxRunPublishTransportIds",
                "Publish Destinations");

            if (SerializedStringArrayContains(
                    destinations,
                    FoxgloveWebSocketTransport.Id))
            {
                FoxRunEncodingEditorLabels.DrawFoxRunEncoding(
                    FindCachedProperty("_defaultFoxRunPublishEncoding"),
                    "WebSocket Encoding");
            }

            DrawFloatProperty(
                "_defaultPublishRateHz",
                "Default Publish Rate Hz",
                "Default rate used by publishers that choose the Manager default. Use <= 0 to publish every eligible frame.");

            var manager = target as FoxgloveManager;
            if (manager != null
                && manager.ActiveFoxRunPublishSessionPolicy.SessionActive)
            {
                EditorGUILayout.HelpBox(
                    "Publish profile changes apply after this Manager is disabled and re-enabled. The active session retains its captured Provider selection, encoding, and rate.",
                    MessageType.Info);
            }

            FoxgloveManagerInspectorLayout.Subheader("Component Publishers");
            DrawGlobalEncodingProperty(
                "_defaultPublisherEncoding",
                "WebSocket Encoding");
            DrawProperty(
                "_allowPublisherOverride",
                "Allow Component Publisher Override");
            EditorGUILayout.HelpBox(
                "Component publishers and generated FoxRun contracts use independent WebSocket encoding defaults.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Coordinate System");
            DrawProperty("_outputCoordinateMode", "Output Coordinate Mode");
            EditorGUILayout.HelpBox(
                "Defines the coordinate convention of supported data published from Unity. MCAP records the same converted external payload and labels output channels with this mode.",
                MessageType.Info);

            FoxgloveManagerInspectorLayout.Subheader("Assets");
            DrawProperty("_assetRoots");
        }

        private void DrawFoxRunTransportProviderExtensions()
        {
            var manager = target as FoxgloveManager;
            if (manager == null)
                return;

            foreach (var drawer in
                     FoxRunTransportProviderDrawerRegistry.Capture())
            {
                drawer.EnsureProvider(manager);
                drawer.Draw(manager, serializedObject);
            }
        }

        private static bool SerializedStringArrayContains(
            SerializedProperty property,
            string expected)
        {
            if (property == null || !property.isArray)
                return false;

            for (var i = 0; i < property.arraySize; i++)
            {
                if (string.Equals(
                        property.GetArrayElementAtIndex(i).stringValue,
                        expected,
                        System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
