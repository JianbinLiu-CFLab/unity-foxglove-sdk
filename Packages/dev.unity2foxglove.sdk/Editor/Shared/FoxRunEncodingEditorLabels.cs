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

        public static void DrawFoxRunEncoding(SerializedProperty property, string label)
        {
            if (property == null)
                return;

            var selected = property.enumValueIndex == (int)FoxRunEncoding.JSON ? 1 : 0;
            selected = EditorGUILayout.Popup(label, selected, ManagerDefaultLabels);
            property.enumValueIndex = selected == 0
                ? (int)FoxRunEncoding.Protobuf
                : (int)FoxRunEncoding.JSON;
        }

        public static string ToDisplayLabel(FoxRunEncoding encoding)
        {
            switch (encoding)
            {
                case (FoxRunEncoding)0: return "Inherit";
                case FoxRunEncoding.Protobuf: return "Protobuf";
                case FoxRunEncoding.JSON: return "JSON";
                default: return "Unknown";
            }
        }
    }
}
