// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Shared Inspector labels for publisher encoding enums.

using System;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class PublisherEncodingEditorLabels
    {
        private static readonly string[] GlobalEncodingLabels = { "JSON", "Protobuf", "MsgPack" };
        private static readonly string[] PublisherOverrideLabels = { "Use Manager", "JSON", "Protobuf", "MsgPack" };
        private const string MsgPackConsumerNotice =
            "MsgPack is a schemaless raw channel for custom clients. Foxglove Desktop does not currently parse or render live MsgPack panels.";

        static PublisherEncodingEditorLabels()
        {
            AssertLabelCount<GlobalEncoding>(GlobalEncodingLabels, nameof(GlobalEncodingLabels));
            AssertLabelCount<PublisherEncodingOverride>(PublisherOverrideLabels, nameof(PublisherOverrideLabels));
        }

        public static void DrawGlobalEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, GlobalEncodingLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, GlobalEncodingLabels);
            DrawMsgPackConsumerNotice((GlobalEncoding)property.intValue);
        }

        public static void DrawPublisherOverride(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var current = ClampIndex(property.enumValueIndex, PublisherOverrideLabels.Length);
            property.enumValueIndex = EditorGUILayout.Popup(label, current, PublisherOverrideLabels);
            DrawMsgPackConsumerNotice((PublisherEncodingOverride)property.intValue);
        }

        public static void DrawEffectiveEncoding(PublisherEffectiveEncoding encoding, string label)
        {
            EditorGUILayout.TextField(label, PublisherEncodingPolicy.ToDisplayEncoding(encoding));
        }

        private static int ClampIndex(int index, int count)
        {
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        private static void AssertLabelCount<TEnum>(string[] labels, string labelSetName)
            where TEnum : Enum
        {
            var enumCount = Enum.GetValues(typeof(TEnum)).Length;
            if (labels.Length != enumCount)
                throw new InvalidOperationException(
                    labelSetName + " has " + labels.Length + " labels but " + typeof(TEnum).Name + " has " + enumCount + " values.");
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
