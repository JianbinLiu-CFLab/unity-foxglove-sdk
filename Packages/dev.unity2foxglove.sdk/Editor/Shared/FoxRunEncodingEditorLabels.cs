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
        private static readonly string[] ManagerDefaultLabels = { "Protobuf", "JSON" };

        public static void DrawFoxRunWireEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var selected = property.enumValueIndex == (int)FoxRunWireEncoding.Json ? 1 : 0;
            selected = EditorGUILayout.Popup(label, selected, ManagerDefaultLabels);
            property.enumValueIndex = selected == 0
                ? (int)FoxRunWireEncoding.Protobuf
                : (int)FoxRunWireEncoding.Json;
        }

        public static string ToDisplayLabel(FoxRunWireEncoding encoding)
        {
            switch (encoding)
            {
                case FoxRunWireEncoding.Inherit: return "Inherit";
                case FoxRunWireEncoding.Protobuf: return "Protobuf";
                case FoxRunWireEncoding.Json: return "JSON";
                default: return "Unknown";
            }
        }
    }
}
