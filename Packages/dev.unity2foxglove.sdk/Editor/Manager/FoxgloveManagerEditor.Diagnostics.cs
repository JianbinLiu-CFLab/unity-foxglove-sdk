// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager

using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;
using UnityEngine;
using UnityEditor;

namespace Unity.FoxgloveSDK.Editor
{
    public partial class FoxgloveManagerEditor : UnityEditor.Editor
    {
        private void DrawDiagnosticsSection()
        {
            DrawTransportHealth();
        }

        private void DrawTransportHealth()
        {
            var manager = (Components.FoxgloveManager)target;
            if (manager == null) return;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Transport stats are available in Play Mode.", MessageType.Info);
                return;
            }

            var stats = manager.GetTransportStatsSnapshot();
            if (!stats.Supported)
            {
                EditorGUILayout.HelpBox("Transport stats are not available for this backend.", MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Running", stats.IsRunning);
                EditorGUILayout.IntField("Active Clients", stats.ActiveClientCount);
                EditorGUILayout.LongField("Total Accepted", stats.TotalAcceptedClients);
                EditorGUILayout.LongField("Total Disconnected", stats.TotalDisconnectedClients);
                EditorGUILayout.LongField("Queued Frames", stats.TotalQueuedFrames);
                EditorGUILayout.LongField("Queued Bytes", stats.TotalQueuedBytes);
                EditorGUILayout.LongField("Dropped Data Frames", stats.TotalDroppedDataFrames);
                EditorGUILayout.LongField("Control Overflow Disconnects", stats.ControlOverflowDisconnects);
            }

            if (stats.Clients != null && stats.Clients.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Clients", EditorStyles.boldLabel);
                foreach (var c in stats.Clients)
                {
                    EditorGUILayout.LabelField(
                        $"#{c.ClientId}",
                        $"queued: {c.QueuedFrames} ({c.QueuedBytes} B)  dropped: {c.DroppedDataFrames}  sent: {c.SentFrames}  idle: {c.LastActivityAgeMs} ms",
                        EditorStyles.miniLabel);
                }
            }
        }
    }
}
