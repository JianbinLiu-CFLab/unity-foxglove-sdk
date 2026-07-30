// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: Provider-neutral data transport placement.

using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawDataTransportSection()
        {
            DrawDataTransportSubsection(
                "Publish Data",
                "DataTransportPublish",
                ref _dataTransportPublishExpanded,
                DrawPublishDataSection);
            DrawDataTransportSubsection(
                "Subscribe Data",
                "DataTransportSubscribe",
                ref _dataTransportSubscribeExpanded,
                DrawSubscribeDataSection);

            DrawFoxRunTransportProviderExtensions();
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
                    ref expanded,
                    EditorStyles.foldoutHeader))
            {
                return;
            }

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
