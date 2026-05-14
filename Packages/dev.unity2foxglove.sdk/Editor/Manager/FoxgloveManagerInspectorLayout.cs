// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Shared IMGUI layout helpers for the FoxgloveManager Inspector.

using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Small Editor-only layout helper for the workflow-oriented
    /// <c>FoxgloveManager</c> Inspector.
    /// </summary>
    internal static class FoxgloveManagerInspectorLayout
    {
        /// <summary>
        /// Draws a top-level workflow section and returns whether it is expanded.
        /// </summary>
        internal static bool WorkflowSection(string title, ref bool expanded)
        {
            EditorGUILayout.Space();
            expanded = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
            return expanded;
        }

        /// <summary>
        /// Draws a compact subsection heading inside a workflow section.
        /// </summary>
        internal static void Subheader(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }
}
