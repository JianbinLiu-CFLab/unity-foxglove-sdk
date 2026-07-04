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
        private static readonly string[] GlobalEncodingLabels = { "JSON", "Protobuf", "ROS2", "MsgPack" };
        private static readonly string[] PublisherOverrideLabels = { "Use Manager", "JSON", "Protobuf", "ROS2", "MsgPack" };
        private static readonly string[] BridgeOverrideLabels = { "Use Manager", "Disabled", "Enabled" };
        private static readonly string[] BridgeQosPresetLabels = { "Reliable Default", "Sensor Data", "Transient Local", "Custom" };
        private const string MsgPackConsumerNotice =
            "MsgPack is a schemaless raw channel for custom clients. Foxglove Desktop does not currently parse or render live MsgPack panels.";

        public static void DrawGlobalEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, GlobalEncodingLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, GlobalEncodingLabels);
            DrawMsgPackConsumerNotice((GlobalEncoding)property.enumValueIndex);
        }

        public static void DrawPublisherOverride(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, PublisherOverrideLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, PublisherOverrideLabels);
            DrawMsgPackConsumerNotice((PublisherEncodingOverride)property.enumValueIndex);
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

        public static void DrawRos2BridgeQosPreset(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, BridgeQosPresetLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, BridgeQosPresetLabels);
        }

        private static int ClampIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        private static void DrawMsgPackConsumerNotice(GlobalEncoding encoding)
        {
            if (encoding == GlobalEncoding.MsgPack)
                EditorGUILayout.HelpBox(MsgPackConsumerNotice, MessageType.Info);
        }

        private static void DrawMsgPackConsumerNotice(PublisherEncodingOverride encoding)
        {
            if (encoding == PublisherEncodingOverride.MsgPack)
                EditorGUILayout.HelpBox(MsgPackConsumerNotice, MessageType.Info);
        }
    }
}
