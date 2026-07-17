// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Data Transport placement and persisted nested Inspector foldouts.

using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawDataTransportSection()
        {
            DrawDataTransportSubsection(
                "Publish",
                "DataTransportPublish",
                ref _dataTransportPublishExpanded,
                DrawPublishDataSection);
            DrawDataTransportSubsection(
                "Subscribe",
                "DataTransportSubscribe",
                ref _dataTransportSubscribeExpanded,
                DrawSubscribeDataSection);

            if (HasR2fuNativeRuntimeDemand())
            {
                DrawDataTransportSubsection(
                    "ROS 2 Native Runtime (R2FU)",
                    "DataTransportNativeRuntime",
                    ref _dataTransportNativeRuntimeExpanded,
                    DrawR2fuRuntimeSection);
            }
        }

        private static void DrawDataTransportSubsection(
            string title,
            string sessionStateName,
            ref bool expanded,
            System.Action drawContents)
        {
            if (!FoxgloveManagerInspectorLayout.WorkflowSubsection(
                    title,
                    InspectorFoldoutKey(sessionStateName),
                    ref expanded))
                return;

            EditorGUI.indentLevel++;
            try
            {
                drawContents();
            }
            finally
            {
                EditorGUI.indentLevel--;
            }
        }
    }
}
