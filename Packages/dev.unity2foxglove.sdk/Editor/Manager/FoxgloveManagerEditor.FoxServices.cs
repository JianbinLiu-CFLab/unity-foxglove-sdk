// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private void DrawFoxServicesSection()
        {
            FoxgloveManagerInspectorLayout.Subheader("FoxRun Runtime Topics");
            DrawFoxRunTopicSummary(target as FoxgloveManager);

            EditorGUILayout.Space();
            FoxgloveManagerInspectorLayout.Subheader("Generated Services");
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Generated [FoxService] services register when Play Mode starts. Use Foxglove's Call Service panel to invoke them.",
                    MessageType.Info);
                return;
            }

            if (!Components.FoxgloveServiceHub.TryGetActive(out var hub) || hub == null)
            {
                EditorGUILayout.HelpBox("FoxServiceHub is not active yet.", MessageType.Info);
                return;
            }

            var snapshots = GetServiceSnapshotsForRepaint(hub);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField("Registered Services", snapshots.Count);

            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox("No generated [FoxService] services are currently registered.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("Copy Service List"))
                EditorGUIUtility.systemCopyBuffer = BuildServiceListText(snapshots);

            foreach (var snapshot in snapshots)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.SelectableLabel(snapshot.Name, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        if (GUILayout.Button("Copy", GUILayout.Width(54)))
                            EditorGUIUtility.systemCopyBuffer = snapshot.Name;
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField("Source", snapshot.Source);
                        EditorGUILayout.TextField("Request", snapshot.RequestSchemaName);
                        EditorGUILayout.TextField("Response", snapshot.ResponseSchemaName);
                        EditorGUILayout.LongField("Service Id", snapshot.ServiceId);
                    }
                }
            }
        }

        private static void DrawFoxRunTopicSummary(FoxgloveManager manager)
        {
            var publishDefault = manager != null
                ? manager.ActiveFoxRunPublishEncoding
                : FoxRunWireEncoding.Protobuf;
            var subscriptionDefault = manager != null
                ? manager.ActiveFoxRunSubscriptionEncoding
                : FoxRunWireEncoding.Protobuf;
            var summaries = FoxRunSchemaInfoRegistry.GetTopicSummaries(publishDefault, subscriptionDefault);
            if (summaries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No generated FoxRun topic metadata is registered in this domain.",
                    MessageType.Info);
                return;
            }

            DrawFoxRunTopicGroup("Publish Topics", summaries, "PublishOnly");
            DrawFoxRunTopicGroup("Subscribe Topics", summaries, "SubscribeOnly");
            DrawFoxRunTopicGroup("Publish And Subscribe Topics", summaries, "PublishAndSubscribe");
        }

        private static void DrawFoxRunTopicGroup(
            string title,
            System.Collections.Generic.IReadOnlyList<FoxRunTopicSummary> summaries,
            string direction)
        {
            var hasTopics = false;
            foreach (var summary in summaries)
            {
                if (string.Equals(summary.Direction, direction, System.StringComparison.Ordinal))
                {
                    hasTopics = true;
                    break;
                }
            }

            if (!hasTopics)
                return;

            EditorGUILayout.Space();
            FoxgloveManagerInspectorLayout.Subheader(title);
            DrawFoxRunTopicSummaryHeader();
            foreach (var summary in summaries)
            {
                if (string.Equals(summary.Direction, direction, System.StringComparison.Ordinal))
                    DrawFoxRunTopicSummaryRow(summary);
            }
        }

        private static void DrawFoxRunTopicSummaryHeader()
        {
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetTopicSummaryColumns(row, out var topic, out var declared, out var effective, out _);
            EditorGUI.LabelField(topic, "Topic");
            EditorGUI.LabelField(declared, "Declared");
            EditorGUI.LabelField(effective, "Effective");
        }

        private static void DrawFoxRunTopicSummaryRow(FoxRunTopicSummary summary)
        {
            var schemaName = string.IsNullOrEmpty(summary.SchemaName) ? "(schemaless)" : summary.SchemaName;
            var schemaContent = new GUIContent("Schema: " + schemaName, schemaName);
            var schemaStyle = GetTopicSchemaStyle();
            var row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetTopicSummaryColumns(row, out var topic, out var declared, out var effective, out var copy);
            EditorGUI.SelectableLabel(topic, summary.Topic, EditorStyles.textField);
            EditorGUI.LabelField(declared, FoxRunEncodingEditorLabels.ToDisplayLabel(summary.DeclaredEncoding));
            EditorGUI.LabelField(effective, FoxRunEncodingEditorLabels.ToDisplayLabel(summary.EffectiveEncoding));
            if (GUI.Button(copy, "Copy"))
                EditorGUIUtility.systemCopyBuffer = summary.Topic;

            var schemaRow = EditorGUILayout.GetControlRect(
                false,
                schemaStyle.CalcHeight(schemaContent, GetTopicSchemaLayoutWidth()));
            schemaRow.width = topic.width;
            EditorGUI.LabelField(schemaRow, schemaContent, schemaStyle);
        }

        private const float CopyButtonWidth = 54f;
        private const float EncodingColumnWidth = 78f;
        private const float TopicColumnGap = 4f;
        private const float TopicSchemaChromeWidth = 116f;
        private static GUIStyle _topicSchemaStyle;

        private static GUIStyle GetTopicSchemaStyle()
        {
            if (_topicSchemaStyle == null)
                _topicSchemaStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            return _topicSchemaStyle;
        }

        private static void GetTopicSummaryColumns(
            Rect row,
            out Rect topic,
            out Rect declared,
            out Rect effective,
            out Rect copy)
        {
            copy = new Rect(row.xMax - CopyButtonWidth, row.y, CopyButtonWidth, row.height);
            effective = new Rect(
                copy.x - TopicColumnGap - EncodingColumnWidth,
                row.y,
                EncodingColumnWidth,
                row.height);
            declared = new Rect(
                effective.x - TopicColumnGap - EncodingColumnWidth,
                row.y,
                EncodingColumnWidth,
                row.height);
            topic = new Rect(
                row.x,
                row.y,
                Mathf.Max(1f, declared.x - TopicColumnGap - row.x),
                row.height);
        }

        private static float GetTopicSchemaLayoutWidth()
        {
            return Mathf.Max(
                160f,
                EditorGUIUtility.currentViewWidth
                - (EncodingColumnWidth * 2f)
                - CopyButtonWidth
                - TopicSchemaChromeWidth);
        }

        private static string BuildServiceListText(System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> snapshots)
        {
            var lines = new string[snapshots.Count];
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                lines[i] = snapshot.Name
                           + " | Source: " + snapshot.Source
                           + " | Request: " + snapshot.RequestSchemaName
                           + " | Response: " + snapshot.ResponseSchemaName
                           + " | Service Id: " + snapshot.ServiceId;
            }
            return string.Join("\n", lines);
        }

        private void RefreshTransportStatsForRepaint()
        {
            var manager = target as Components.FoxgloveManager;
            _transportStatsFrame = Time.frameCount;
            _transportStatsThisRepaint = Application.isPlaying && manager != null
                ? manager.GetTransportStatsSnapshot()
                : TransportStatsSnapshot.Unsupported;
        }

        private TransportStatsSnapshot GetTransportStatsForRepaint()
        {
            if (_transportStatsFrame != Time.frameCount)
                RefreshTransportStatsForRepaint();
            return _transportStatsThisRepaint ?? TransportStatsSnapshot.Unsupported;
        }

        private System.Collections.Generic.IReadOnlyList<Components.FoxgloveRegisteredServiceSnapshot> GetServiceSnapshotsForRepaint(
            Components.FoxgloveServiceHub hub)
        {
            if (hub == null)
                return System.Array.Empty<Components.FoxgloveRegisteredServiceSnapshot>();

            var frame = Time.frameCount;
            if (_cachedServiceHub == hub && _cachedServiceSnapshotFrame == frame)
                return _cachedServiceSnapshots;

            _cachedServiceHub = hub;
            _cachedServiceSnapshotFrame = frame;
            _cachedServiceSnapshots = hub.GetRegisteredServiceSnapshots();
            return _cachedServiceSnapshots;
        }
    }
}
