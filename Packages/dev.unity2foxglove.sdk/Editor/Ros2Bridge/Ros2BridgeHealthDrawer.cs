// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Ros2Bridge
// Purpose: Inspector drawer for ROS2 Bridge health diagnostics.

using System;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Ros2Bridge;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    internal sealed class Ros2BridgeHealthDrawer
    {
        private readonly object _gate = new object();
        private Task<Ros2BridgeHealthReport> _task;
        private Ros2BridgeHealthReport _lastReport;
        private Ros2BridgeHealthProgress _progress;
        private string _lastError = string.Empty;

        internal void Draw(SerializedObject serializedObject)
        {
            FoxgloveManagerInspectorLayout.Subheader("ROS2 Bridge Health");

            DrawRos2PathControls();

            var running = _task != null && !_task.IsCompleted;
            using (new EditorGUI.DisabledScope(running))
            {
                if (GUILayout.Button("Check ROS2 Bridge"))
                    StartHealthCheck(serializedObject);
            }

            CompleteTaskIfReady();
            DrawProgress(running);
            DrawReport();
        }

        private void DrawRos2PathControls()
        {
            var configured = Ros2BridgeEditorPrefs.Ros2ExecutablePath;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("ROS2 CLI", string.IsNullOrWhiteSpace(configured) ? "PATH: ros2" : configured);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Choose ros2"))
                {
                    var selected = EditorUtility.OpenFilePanel(
                        "Choose ros2 executable",
                        string.IsNullOrWhiteSpace(configured) ? "" : System.IO.Path.GetDirectoryName(configured),
                        Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                    if (!string.IsNullOrEmpty(selected))
                        Ros2BridgeEditorPrefs.Ros2ExecutablePath = selected;
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(configured)))
                {
                    if (GUILayout.Button("Clear ros2"))
                        Ros2BridgeEditorPrefs.ClearRos2ExecutablePath();
                }
            }
        }

        private void StartHealthCheck(SerializedObject serializedObject)
        {
            var host = ReadString(serializedObject, "_ros2BridgeHost", "127.0.0.1");
            var port = ReadInt(serializedObject, "_ros2BridgePort", 8767);
            var timeout = ReadInt(serializedObject, "_ros2BridgeSendTimeoutMs", 1000);
            var ros2 = Ros2BridgeEditorPrefs.Ros2ExecutablePath;
            var pathSource = string.IsNullOrWhiteSpace(ros2)
                ? Ros2BridgeRos2PathSource.Path
                : Ros2BridgeRos2PathSource.EditorPrefs;

            lock (_gate)
            {
                _progress = new Ros2BridgeHealthProgress("start", "Starting ROS2 Bridge health check", 0, 0);
                _lastError = string.Empty;
            }

            var options = new Ros2BridgeHealthOptions(
                liveMode: true,
                host: host,
                port: port,
                ros2ExecutablePath: ros2,
                ros2PathSource: pathSource,
                commandTimeoutMs: Math.Max(1000, timeout),
                sidecarTimeoutMs: Math.Max(1000, timeout),
                unityVersion: Application.unityVersion)
            {
                Progress = progress =>
                {
                    lock (_gate)
                        _progress = progress;
                }
            };

            _task = Task.Run(() => new Ros2BridgeHealthRunner().Run(options));
        }

        private void CompleteTaskIfReady()
        {
            if (_task == null || !_task.IsCompleted)
                return;

            try
            {
                _lastReport = _task.Result;
                _lastError = string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.GetBaseException().Message;
            }
            finally
            {
                _task = null;
            }
        }

        private void DrawProgress(bool running)
        {
            if (!running)
                return;

            Ros2BridgeHealthProgress progress;
            lock (_gate)
                progress = _progress;

            var message = progress == null ? "Checking ROS2 Bridge..." : progress.Message;
            EditorGUILayout.HelpBox(message, MessageType.Info);
            EditorApplication.QueuePlayerLoopUpdate();
        }

        private void DrawReport()
        {
            if (!string.IsNullOrWhiteSpace(_lastError))
            {
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
                return;
            }

            if (_lastReport == null)
            {
                EditorGUILayout.HelpBox(
                    "Runs live checks for ROS2 CLI, foxglove_msgs, bundled interfaces, and the local sidecar health ping.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Summary", SummaryLabel(_lastReport.Summary));
            }

            foreach (var check in _lastReport.Checks)
            {
                var type = MessageType.Info;
                if (check.Status == Ros2BridgeHealthStatus.Fail)
                    type = MessageType.Error;
                else if (check.Status == Ros2BridgeHealthStatus.Warning)
                    type = MessageType.Warning;

                var text = $"{check.Title}: {check.Status}\n{check.Message}";
                if (!string.IsNullOrWhiteSpace(check.Remediation))
                    text += "\n" + check.Remediation;
                if (!string.IsNullOrWhiteSpace(check.Command))
                    text += "\n" + check.Command;
                EditorGUILayout.HelpBox(text, type);

                if (!string.IsNullOrWhiteSpace(check.Command))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Copy Command", GUILayout.Width(140)))
                            EditorGUIUtility.systemCopyBuffer = check.Command;
                    }
                }
            }
        }

        private static string SummaryLabel(Ros2BridgeHealthSummary summary)
        {
            switch (summary)
            {
                case Ros2BridgeHealthSummary.Ready:
                    return "Ready";
                case Ros2BridgeHealthSummary.SidecarNotRunning:
                    return "Sidecar Not Running";
                case Ros2BridgeHealthSummary.Failed:
                    return "Failed";
                case Ros2BridgeHealthSummary.NeedsSetup:
                default:
                    return "Needs Setup";
            }
        }

        private static string ReadString(SerializedObject serializedObject, string propertyName, string fallback)
        {
            var prop = serializedObject.FindProperty(propertyName);
            return prop != null ? prop.stringValue : fallback;
        }

        private static int ReadInt(SerializedObject serializedObject, string propertyName, int fallback)
        {
            var prop = serializedObject.FindProperty(propertyName);
            return prop != null ? prop.intValue : fallback;
        }
    }
}
