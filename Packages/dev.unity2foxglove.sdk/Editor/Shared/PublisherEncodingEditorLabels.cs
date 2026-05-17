// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Shared Inspector labels for publisher encoding enums.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class PublisherEncodingEditorLabels
    {
        private static readonly string[] GlobalEncodingLabels = { "JSON", "Protobuf", "ROS2" };
        private static readonly string[] PublisherOverrideLabels = { "Use Manager", "JSON", "Protobuf", "ROS2" };
        private static readonly string[] BridgeOverrideLabels = { "Use Manager", "Disabled", "Enabled" };

        public static void DrawGlobalEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, GlobalEncodingLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, GlobalEncodingLabels);
        }

        public static void DrawPublisherOverride(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, PublisherOverrideLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, PublisherOverrideLabels);
        }

        public static void DrawEffectiveEncoding(PublisherEffectiveEncoding encoding, string label)
        {
            EditorGUILayout.TextField(label, PublisherEncodingPolicy.ToDisplayEncoding(encoding));
        }

        public static void DrawRos2BridgeOverride(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, BridgeOverrideLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, BridgeOverrideLabels);
        }

        public static void DrawEffectiveRos2BridgeOutput(Ros2BridgeEffectiveOutput output, string label)
        {
            EditorGUILayout.TextField(label, Ros2BridgeOutputPolicy.ToDisplayLabel(output));
        }

        private static int ClampIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }
    }
}
