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
        private readonly System.Collections.Generic.Dictionary<uint, TransportClientLabelCache> _transportClientLabelCache =
            new System.Collections.Generic.Dictionary<uint, TransportClientLabelCache>();

        private void DrawDiagnosticsSection()
        {
            DrawProfilerDiagnostics();
            EditorGUILayout.Space();
            DrawPublishCadenceDiagnostics();
            EditorGUILayout.Space();
            DrawFrameStallDiagnostics();
            EditorGUILayout.Space();
            DrawTransportHealth();
        }

        private void DrawProfilerDiagnostics()
        {
            FoxgloveManagerInspectorLayout.Subheader("Unity Profiler");
            DrawProperty("_profilingEnabled", "Unity Profiler Markers");
            EditorGUILayout.HelpBox(
                "Enables optional SDK ProfilerMarker samples. Marker names are bounded and intended for targeted profiling runs.",
                MessageType.Info);
        }

        private void DrawPublishCadenceDiagnostics()
        {
            FoxgloveManagerInspectorLayout.Subheader("Publish Cadence");
            DrawProperty("_publishCadenceDiagnosticsEnabled", "Publish Cadence Diagnostics");
            using (new EditorGUI.DisabledScope(!GetBool("_publishCadenceDiagnosticsEnabled")))
            {
                DrawFloatProperty(
                    "_publishCadenceDiagnosticsSummaryIntervalSeconds",
                    "Summary Interval Seconds",
                    "Seconds between per-topic publish cadence diagnostic summaries.");
            }
        }

        private void DrawFrameStallDiagnostics()
        {
            FoxgloveManagerInspectorLayout.Subheader("Frame Stalls");
            DrawProperty("_frameStallDiagnosticsEnabled", "Frame Stall Diagnostics");
            using (new EditorGUI.DisabledScope(!GetBool("_frameStallDiagnosticsEnabled")))
            {
                DrawFloatProperty(
                    "_frameStallDiagnosticsThresholdMs",
                    "Stall Threshold Ms",
                    "Main-thread frame time threshold before a frame-stall diagnostic is logged.");
                DrawProperty("_frameStallStageTimingDiagnosticsEnabled", "Stage Timing Diagnostics");
            }

            EditorGUILayout.HelpBox(
                "Logs long Play Mode frame gaps with focus, Play Mode, Editor compile/update, GC-memory delta, transport queue/drop, and optional Manager Update sub-stage timing state.",
                MessageType.Info);
        }

        private void DrawTransportHealth()
        {
            FoxgloveManagerInspectorLayout.Subheader("Transport");
            var manager = (Components.FoxgloveManager)target;
            if (manager == null) return;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Transport stats are available in Play Mode.", MessageType.Info);
                return;
            }

            var stats = GetTransportStatsForRepaint();
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
                    var label = GetTransportClientLabel(c);
                    EditorGUILayout.LabelField(label.Name, label.Value, EditorStyles.miniLabel);
                }
            }
            else
            {
                ClearTransportClientLabelCache();
            }
        }

        private TransportClientLabelCache GetTransportClientLabel(TransportClientStats client)
        {
            if (_transportClientLabelCache.Count > 0 && _transportClientLabelCache.Count > GetTransportStatsForRepaint().ActiveClientCount)
                _transportClientLabelCache.Clear();

            if (_transportClientLabelCache.TryGetValue(client.ClientId, out var cached)
                && cached.Matches(client))
            {
                return cached;
            }

            cached = TransportClientLabelCache.From(client);
            _transportClientLabelCache[client.ClientId] = cached;
            return cached;
        }

        private void ClearTransportClientLabelCache()
        {
            _transportClientLabelCache.Clear();
        }

        private readonly struct TransportClientLabelCache
        {
            private readonly uint _clientId;
            private readonly int _queuedFrames;
            private readonly int _queuedBytes;
            private readonly long _droppedDataFrames;
            private readonly long _sentFrames;
            private readonly long _lastActivityAgeMs;

            private TransportClientLabelCache(TransportClientStats client)
            {
                _clientId = client.ClientId;
                _queuedFrames = client.QueuedFrames;
                _queuedBytes = client.QueuedBytes;
                _droppedDataFrames = client.DroppedDataFrames;
                _sentFrames = client.SentFrames;
                _lastActivityAgeMs = client.LastActivityAgeMs;
                Name = "#" + client.ClientId;
                Value = "queued: " + client.QueuedFrames
                    + " (" + client.QueuedBytes + " B)  dropped: " + client.DroppedDataFrames
                    + "  sent: " + client.SentFrames
                    + "  idle: " + client.LastActivityAgeMs + " ms";
            }

            public string Name { get; }
            public string Value { get; }

            public bool Matches(TransportClientStats client)
                => client != null
                   && _clientId == client.ClientId
                   && _queuedFrames == client.QueuedFrames
                   && _queuedBytes == client.QueuedBytes
                   && _droppedDataFrames == client.DroppedDataFrames
                   && _sentFrames == client.SentFrames
                   && _lastActivityAgeMs == client.LastActivityAgeMs;

            public static TransportClientLabelCache From(TransportClientStats client)
                => new TransportClientLabelCache(client);
        }
    }
}
