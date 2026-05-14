// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: MCAP replay-file preflight UI used by FoxgloveManagerEditor.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Draws the MCAP Replay preflight controls and summary without bloating
    /// the main FoxgloveManager Inspector.
    /// </summary>
    internal sealed class McapReplayPreflightDrawer
    {
        private string _mcapPreflightSummary;
        private string _mcapPreflightTopics;
        private int _mcapPreflightTopicCount;
        private bool _mcapTopicsExpanded;
        private MessageType _mcapPreflightMessageType = MessageType.Info;

        /// <summary>
        /// Draws latest-recording selection, replay-file analysis, and the
        /// cached summary produced by <see cref="McapIndexedReader"/>.
        /// </summary>
        internal void Draw(SerializedObject serializedObject, UnityEngine.Object targetObject, SerializedProperty replayPath)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("MCAP Indexed Reader Summary", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Latest Recording"))
                {
                    if (FindLatestReadableRecording(out var latestRecording, out var error))
                    {
                        replayPath.stringValue = MakeRelative(latestRecording);
                        serializedObject.ApplyModifiedProperties();
                        EditorUtility.SetDirty(targetObject);
                        AnalyzeReplayMcap(latestRecording);
                    }
                    else
                    {
                        SetMcapPreflightMessage(error, MessageType.Warning);
                    }
                }

                if (GUILayout.Button("Analyze Replay File"))
                {
                    AnalyzeReplayMcap(ResolveProjectPath(replayPath.stringValue));
                }
            }

            if (!string.IsNullOrEmpty(_mcapPreflightSummary))
                EditorGUILayout.HelpBox(_mcapPreflightSummary, _mcapPreflightMessageType);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_mcapPreflightTopics)))
            {
                if (GUILayout.Button("Copy Topics"))
                    EditorGUIUtility.systemCopyBuffer = _mcapPreflightTopics;
            }

            if (!string.IsNullOrEmpty(_mcapPreflightTopics))
            {
                _mcapTopicsExpanded = EditorGUILayout.Foldout(_mcapTopicsExpanded, $"Topics ({_mcapPreflightTopicCount})", true);
                if (_mcapTopicsExpanded)
                {
                    var height = Mathf.Min(180f, 24f + (_mcapPreflightTopicCount * 18f));
                    EditorGUILayout.TextArea(_mcapPreflightTopics, GUILayout.MinHeight(height));
                }
            }
        }

        private void AnalyzeReplayMcap(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                SetMcapPreflightMessage("Select an MCAP replay file first.", MessageType.Warning);
                return;
            }

            if (!File.Exists(path))
            {
                SetMcapPreflightMessage($"MCAP file was not found: {path}", MessageType.Warning);
                return;
            }

            try
            {
                using var indexed = McapIndexedReader.OpenRead(path);
                var statistics = indexed.Summary.Statistics;
                var rawMessageRange = statistics == null
                    ? "unavailable"
                    : $"{statistics.MessageStartTime} - {statistics.MessageEndTime} ns";
                var humanMessageRange = FormatMcapTimeRange(statistics);
                var messageCount = statistics == null ? "unavailable" : statistics.MessageCount.ToString("N0");
                var size = new FileInfo(path).Length;
                var topics = BuildTopicList(indexed.Channels);
                var topicText = string.Join("\n", topics);

                SetMcapPreflightMessage(
                    "Path: " + MakeRelative(path) + "\n"
                    + $"Size: {size:N0} bytes\n"
                    + $"Channels: {indexed.Channels.Count}\n"
                    + $"Chunks: {indexed.Summary.ChunkIndexes.Count}\n"
                    + $"Messages: {messageCount}\n"
                    + $"Time Range (UTC): {humanMessageRange}\n"
                    + $"Raw Time Range: {rawMessageRange}\n"
                    + $"Metadata Indexes: {indexed.MetadataIndexes.Count}\n"
                    + $"Attachment Indexes: {indexed.AttachmentIndexes.Count}\n"
                    + "Topic Preview: " + BuildTopicPreview(topics),
                    MessageType.Info,
                    topicText,
                    topics.Count);
            }
            catch (Exception ex)
            {
                SetMcapPreflightMessage($"MCAP preflight failed: {ex.Message}", MessageType.Error);
            }
        }

        private void SetMcapPreflightMessage(
            string message,
            MessageType messageType,
            string topics = "",
            int topicCount = 0)
        {
            _mcapPreflightSummary = message;
            _mcapPreflightMessageType = messageType;
            _mcapPreflightTopics = topics;
            _mcapPreflightTopicCount = topicCount;
        }

        private static bool FindLatestReadableRecording(out string latestRecording, out string error)
        {
            latestRecording = string.Empty;
            error = string.Empty;

            var recordingsDir = Path.Combine(GetDefaultDir(), "Recordings");
            if (!Directory.Exists(recordingsDir))
            {
                error = $"Recordings directory was not found: {recordingsDir}";
                return false;
            }

            var candidates = Directory.GetFiles(recordingsDir, "*.mcap", SearchOption.AllDirectories);
            Array.Sort(candidates, (left, right) =>
                File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));

            foreach (var candidate in candidates)
            {
                try
                {
                    var info = new FileInfo(candidate);
                    if (info.Length <= 0)
                        continue;

                    using (McapIndexedReader.OpenRead(candidate))
                    {
                    }

                    latestRecording = candidate;
                    return true;
                }
                catch (IOException)
                {
                }
                catch (InvalidDataException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            error = $"No readable MCAP recordings were found under: {recordingsDir}";
            return false;
        }

        private static List<string> BuildTopicList(IReadOnlyList<McapChannel> channels)
        {
            var topics = new List<string>();
            if (channels == null || channels.Count == 0)
                return topics;

            for (var i = 0; i < channels.Count; i++)
            {
                var topic = channels[i].Topic;
                if (string.IsNullOrEmpty(topic) || topics.Contains(topic))
                    continue;

                topics.Add(topic);
            }

            return topics;
        }

        private static string BuildTopicPreview(IReadOnlyList<string> topics)
        {
            if (topics == null || topics.Count == 0)
                return "(none)";

            var preview = new List<string>();
            for (var i = 0; i < topics.Count && preview.Count < 8; i++)
                preview.Add(topics[i]);

            var suffix = topics.Count > preview.Count ? $" (+{topics.Count - preview.Count} more)" : string.Empty;
            return string.Join(", ", preview) + suffix;
        }

        private static string FormatMcapTimeRange(McapStatistics statistics)
        {
            if (statistics == null)
                return "unavailable";

            return $"{FormatUnixNanoseconds(statistics.MessageStartTime)} - {FormatUnixNanoseconds(statistics.MessageEndTime)} UTC";
        }

        private static string FormatUnixNanoseconds(ulong unixNanoseconds)
        {
            const ulong NanosecondsPerSecond = 1_000_000_000UL;
            var seconds = unixNanoseconds / NanosecondsPerSecond;
            var nanoseconds = unixNanoseconds % NanosecondsPerSecond;
            if (seconds > long.MaxValue)
                return $"{unixNanoseconds} ns";

            try
            {
                var utc = DateTimeOffset.FromUnixTimeSeconds((long)seconds).UtcDateTime;
                return $"{utc:yyyy-MM-dd HH:mm:ss}.{nanoseconds:D9}";
            }
            catch (ArgumentOutOfRangeException)
            {
                return $"{unixNanoseconds} ns";
            }
        }

        private static string GetDefaultDir()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private static string MakeRelative(string absolute)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot)) return absolute;
            var normRoot = projectRoot.Replace('\\', '/');
            var normAbs = absolute.Replace('\\', '/');
            if (normAbs.StartsWith(normRoot + "/"))
                return normAbs.Substring(normRoot.Length + 1);
            return normAbs;
        }

        private static string ResolveProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return Path.GetFullPath(Path.Combine(GetDefaultDir(), path));
        }
    }
}
