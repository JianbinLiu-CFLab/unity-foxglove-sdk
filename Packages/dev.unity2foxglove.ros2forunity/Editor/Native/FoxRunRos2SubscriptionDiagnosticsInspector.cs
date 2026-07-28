// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native.Editor
// Purpose: Read-only Inspector surface for bounded FoxRun native subscription diagnostics.

#if UNITY_EDITOR && UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity2Foxglove.Ros2ForUnity.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native.Editor
{
    /// <summary>
    /// Optional editor-only renderer for immutable native subscription snapshots.
    /// It never initializes ROS2; the runtime hub is solely responsible for
    /// producing snapshots after it has safely acquired the native runtime.
    /// </summary>
    public static class FoxRunRos2SubscriptionDiagnosticsInspector
    {
        private static int _cachedFrame = -1;
        private static IReadOnlyList<FoxRunRos2SubscriptionDiagnosticSnapshot> _cachedSnapshots =
            Array.Empty<FoxRunRos2SubscriptionDiagnosticSnapshot>();

        public static void DrawFoxRunNativeSubscriptionDiagnostics()
        {
            var snapshots = GetSnapshotsForCurrentFrame();
            if (snapshots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No active ROS2 Native subscription contracts have reported diagnostics yet. "
                    + "WaitingForRuntime and Unsupported contracts appear here once discovery runs.",
                    MessageType.Info);
                return;
            }

            var selectedRuntime = Ros2ForUnityRuntimeSelectorInspector.GetSelectedRuntimeDisplayName();
            for (var i = 0; i < snapshots.Count; i++)
                DrawSnapshot(snapshots[i], selectedRuntime);
        }

        private static IReadOnlyList<FoxRunRos2SubscriptionDiagnosticSnapshot> GetSnapshotsForCurrentFrame()
        {
            var currentFrame = Time.frameCount;
            if (_cachedFrame == currentFrame)
                return _cachedSnapshots;

            _cachedFrame = currentFrame;
            _cachedSnapshots = FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots()
                ?? Array.Empty<FoxRunRos2SubscriptionDiagnosticSnapshot>();
            return _cachedSnapshots;
        }

        private static void DrawSnapshot(
            FoxRunRos2SubscriptionDiagnosticSnapshot snapshot,
            string selectedRuntime)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(snapshot.TransportLabel, EditorStyles.boldLabel);
                DrawCopyableField("Topic", snapshot.Topic);
                DrawCopyableField("Canonical ROS Type", snapshot.CanonicalRosType);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("State", snapshot.State.ToString());
                    EditorGUILayout.TextField("CLR Type", snapshot.DeclaringType);
                    EditorGUILayout.TextField("Member", snapshot.MemberName);
                    EditorGUILayout.TextField("QoS Profile", snapshot.Qos.Profile.ToString());
                    EditorGUILayout.TextField("Reliability", snapshot.Qos.Reliability.ToString());
                    EditorGUILayout.TextField("Durability", snapshot.Qos.Durability.ToString());
                    EditorGUILayout.TextField("History", snapshot.Qos.History.ToString());
                    EditorGUILayout.IntField("Depth", snapshot.Qos.Depth);
                    EditorGUILayout.TextField("Selected Runtime", selectedRuntime);
                    EditorGUILayout.TextField("Active ROS Distro", DisplayOrWaiting(snapshot.RosDistro));
                    EditorGUILayout.TextField("Active RMW", DisplayOrWaiting(snapshot.RmwImplementation));
                    EditorGUILayout.TextField("Communication Mode", DisplayOrWaiting(snapshot.CommunicationMode));
                    EditorGUILayout.TextField("Session Generation", snapshot.SessionGeneration.ToString());
                    EditorGUILayout.LongField("Received", snapshot.Received);
                    EditorGUILayout.LongField("Applied", snapshot.Applied);
                    EditorGUILayout.LongField("Replaced", snapshot.Replaced);
                    EditorGUILayout.LongField("Rejected After Stop", snapshot.RejectedAfterStop);
                    EditorGUILayout.LongField("Copy Failed", snapshot.CopyFailed);
                    EditorGUILayout.LongField("Stale Callbacks", snapshot.StaleCallbacks);
                    EditorGUILayout.IntField("Pending", snapshot.Pending);
                    EditorGUILayout.TextField("Last Receive", FormatAge(snapshot.LastReceiveStopwatchTimestamp));
                    EditorGUILayout.TextField("Last Apply", FormatAge(snapshot.LastApplyStopwatchTimestamp));
                    EditorGUILayout.TextField("Last Error Code", snapshot.LastErrorCode.ToString());
                    EditorGUILayout.LabelField("Last Error");
                    EditorGUILayout.TextArea(snapshot.LastErrorMessage ?? string.Empty);
                }
            }
        }

        private static void DrawCopyableField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(label, value ?? string.Empty);
                if (GUILayout.Button("Copy", GUILayout.Width(54)))
                    EditorGUIUtility.systemCopyBuffer = value ?? string.Empty;
            }
        }

        private static string DisplayOrWaiting(string value)
            => string.IsNullOrWhiteSpace(value) ? "Awaiting native runtime readiness" : value;

        private static string FormatAge(long timestamp)
        {
            if (timestamp <= 0)
                return "not observed";

            var elapsed = (Stopwatch.GetTimestamp() - timestamp) / (double)Stopwatch.Frequency;
            if (elapsed < 0d)
                elapsed = 0d;
            return elapsed.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " s ago";
        }
    }
}
#endif
