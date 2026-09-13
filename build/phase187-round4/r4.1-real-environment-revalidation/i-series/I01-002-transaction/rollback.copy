// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared
// Purpose: Constrained Inspector labels for FoxRun wire policy.

using Unity.FoxgloveSDK.Components;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class FoxRunEncodingEditorLabels
    {
        private static readonly string[] ManagerDefaultLabels = { "Protobuf", "JSON", "MessagePack" };

        public static void DrawFoxRunEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var selected = property.intValue switch
            {
                (int)FoxRunEncoding.Protobuf => 0,
                (int)FoxRunEncoding.JSON => 1,
                (int)FoxRunEncoding.MessagePack => 2,
                _ => 0
            };
            selected = EditorGUILayout.Popup(label, selected, ManagerDefaultLabels);
            property.intValue = selected switch
            {
                0 => (int)FoxRunEncoding.Protobuf,
                1 => (int)FoxRunEncoding.JSON,
                2 => (int)FoxRunEncoding.MessagePack,
                _ => (int)FoxRunEncoding.Protobuf
            };
        }

        public static string ToDisplayLabel(FoxRunEncoding encoding)
        {
            switch (encoding)
            {
                case (FoxRunEncoding)0: return "Inherit";
                case FoxRunEncoding.Protobuf: return "Protobuf";
                case FoxRunEncoding.JSON: return "JSON";
                case FoxRunEncoding.MessagePack: return "MessagePack";
                default: return "Unknown";
            }
        }
    }
}
