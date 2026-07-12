// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: FoxRun wire-policy and inbound-control Inspector section.

using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor
    {
        private void DrawFoxRunSection()
        {
            var manager = target as FoxgloveManager;

            FoxgloveManagerInspectorLayout.Subheader("Wire Policy");
            FoxRunEncodingEditorLabels.DrawFoxRunWireEncoding(
                FindCachedProperty("_defaultFoxRunWireEncoding"),
                "Default Wire Encoding");
            if (Application.isPlaying && manager != null && manager.IsRunning)
            {
                EditorGUILayout.HelpBox(
                    "Wire-policy changes apply after the server is restarted or re-enabled. The active FoxRun session keeps its captured encoding.",
                    MessageType.Info);
            }

            FoxgloveManagerInspectorLayout.Subheader("Inbound Control");
            DrawProperty("_enableFoxRunInbound");
            using (new EditorGUI.DisabledScope(!GetBool("_enableFoxRunInbound")))
            {
                DrawProperty("_allowRemoteFoxRunInboundWithSharedToken");
                DrawProperty("_foxRunInboundMaxPayloadBytes", "Inbound Max Payload");
                DrawProperty("_foxRunInboundMaxMessagesPerSecondPerTopic", "Inbound Max Rate");
            }
            if (GetBool("_enableFoxRunInbound")
                && !FoxgloveManager.IsLoopbackHost(GetString("_host", "127.0.0.1"))
                && (!GetBool("_allowRemoteFoxRunInboundWithSharedToken")
                    || string.IsNullOrWhiteSpace(GetString("_sharedToken", ""))))
            {
                EditorGUILayout.HelpBox(
                    "FoxRun inbound is fail-closed for non-loopback hosts. Enable remote inbound explicitly and configure a shared token.",
                    MessageType.Warning);
            }

            FoxgloveManagerInspectorLayout.Subheader("Runtime Topics");
            DrawFoxRunTopicSummary(manager);
        }

        private static void DrawFoxRunTopicSummary(FoxgloveManager manager)
        {
            var defaultEncoding = manager != null
                ? manager.ActiveFoxRunDefaultWireEncoding
                : FoxRunWireEncoding.Protobuf;
            var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(defaultEncoding);
            if (summaries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No generated FoxRun topic metadata is registered in this domain.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField("Topic", "Direction | Declared | Effective | Schema", EditorStyles.miniBoldLabel);
                foreach (var summary in summaries)
                {
                    var details = summary.Direction + " | "
                        + FoxRunEncodingEditorLabels.ToDisplayLabel(summary.DeclaredEncoding) + " | "
                        + FoxRunEncodingEditorLabels.ToDisplayLabel(summary.EffectiveEncoding) + " | "
                        + (string.IsNullOrEmpty(summary.SchemaName) ? "(schemaless)" : summary.SchemaName);
                    EditorGUILayout.TextField(summary.Topic, details);
                }
            }
        }
    }
}
