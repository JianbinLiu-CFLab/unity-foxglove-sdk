// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private void DrawFoxServicesSection()
        {
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
