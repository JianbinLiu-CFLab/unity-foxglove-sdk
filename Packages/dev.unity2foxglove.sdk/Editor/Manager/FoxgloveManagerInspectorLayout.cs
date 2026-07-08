// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Shared IMGUI layout helpers for the FoxgloveManager Inspector.

using UnityEditor;
using UnityEngine;

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
        /// Draws a top-level workflow section and persists its expanded state
        /// for the current Editor session.
        /// </summary>
        internal static bool WorkflowSection(string title, string sessionStateKey, ref bool expanded)
        {
            EditorGUILayout.Space();
            expanded = PersistedFoldout(expanded, title, sessionStateKey, EditorStyles.foldoutHeader);
            return expanded;
        }

        /// <summary>
        /// Draws a nested workflow subsection inside a larger Inspector group.
        /// </summary>
        internal static bool WorkflowSubsection(string title, ref bool expanded)
        {
            EditorGUILayout.Space();
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            return expanded;
        }

        /// <summary>
        /// Draws a nested workflow subsection and persists its expanded state
        /// for the current Editor session.
        /// </summary>
        internal static bool WorkflowSubsection(string title, string sessionStateKey, ref bool expanded)
        {
            EditorGUILayout.Space();
            expanded = PersistedFoldout(expanded, title, sessionStateKey, EditorStyles.foldout);
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

        private static bool PersistedFoldout(bool expanded, string title, string sessionStateKey, GUIStyle style)
        {
            var next = EditorGUILayout.Foldout(expanded, title, true, style);
            if (next != expanded)
                SessionState.SetBool(sessionStateKey, next);
            return next;
        }
    }
}
