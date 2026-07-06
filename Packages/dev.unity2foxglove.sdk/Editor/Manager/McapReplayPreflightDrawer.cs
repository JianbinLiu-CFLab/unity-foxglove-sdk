// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Manager
// Purpose: MCAP replay-file preflight UI used by FoxgloveManagerEditor.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Draws advisory MCAP Replay preflight controls before Play Mode starts,
    /// keeping summaries out of the main FoxgloveManager Inspector.
    /// </summary>
    internal sealed class McapReplayPreflightDrawer : IDisposable
    {
        private string _mcapPreflightSummary;
        private string _mcapPreflightTopics;
        private string _identitySummary;
        private string _selectedReplayPath;
        private string _selectedSidecarDirectory;
        private int _mcapPreflightTopicCount;
        private bool _mcapTopicsExpanded;
        private MessageType _mcapPreflightMessageType = MessageType.Info;
        private MessageType _identityMessageType = MessageType.Info;
        private Task<McapReplayAnalysisResult> _analyzeReplayTask;
        private Task<LatestRecordingResult> _findLatestRecordingTask;
        private CancellationTokenSource _pendingWorkCts;
        private SerializedObject _pendingLatestSerializedObject;
        private UnityEngine.Object _pendingLatestTargetObject;
        private SerializedProperty _pendingLatestReplayPath;

        public McapReplayPreflightDrawer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CancelPendingWork;
        }

        public void Dispose()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= CancelPendingWork;
            CancelPendingWork();
        }

        /// <summary>
        /// Draws latest-recording selection, replay-file analysis, and the
        /// cached summary produced by <see cref="McapIndexedReader"/>.
        /// </summary>
        internal void Draw(SerializedObject serializedObject, UnityEngine.Object targetObject, SerializedProperty replayPath)
        {
            CompleteAnalyzeReplayMcapIfReady();
            CompleteFindLatestRecordingIfReady();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Replay Identity Preflight", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Latest Recording"))
                {
                    StartFindLatestReadableRecording(serializedObject, targetObject, replayPath);
                }

                if (GUILayout.Button("Compare With Current"))
                {
                    StartAnalyzeReplayMcap(ResolveProjectPath(replayPath.stringValue), refreshCurrentEvidence: true);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_selectedSidecarDirectory)))
                {
                    if (GUILayout.Button("Open Recording Evidence"))
                        OpenRecordingEvidence();
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_identitySummary)))
                {
                    if (GUILayout.Button("Copy Identity Summary"))
                        EditorGUIUtility.systemCopyBuffer = _identitySummary;
                }
            }

            if (!string.IsNullOrEmpty(_identitySummary))
                EditorGUILayout.HelpBox(_identitySummary, _identityMessageType);

            EditorGUILayout.LabelField("MCAP Indexed Reader Summary", EditorStyles.boldLabel);

            if (GUILayout.Button("Analyze Replay File"))
                StartAnalyzeReplayMcap(ResolveProjectPath(replayPath.stringValue));

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

        private void StartAnalyzeReplayMcap(string path, bool refreshCurrentEvidence = false)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                SetMcapPreflightMessage("Select an MCAP replay file first.", MessageType.Warning);
                SetIdentityMessage("Select an MCAP replay file first.", MessageType.Warning);
                return;
            }

            if (!File.Exists(path))
            {
                SetMcapPreflightMessage($"MCAP file was not found: {path}", MessageType.Warning);
                SetIdentityMessage($"MCAP file was not found: {path}", MessageType.Warning);
                return;
            }

            AnalyzeReplayIdentity(path, refreshCurrentEvidence);
            SetMcapPreflightMessage("Analyzing replay file: " + MakeRelative(path), MessageType.Info);
            var token = StartNewPendingWork();
            _analyzeReplayTask = Task.Run(() => AnalyzeReplayMcapWorker(path, token), token);
            EditorApplication.update -= CompleteAnalyzeReplayMcapIfReady;
            EditorApplication.update += CompleteAnalyzeReplayMcapIfReady;
        }

        private void CancelPendingWork()
        {
            EditorApplication.update -= CompleteAnalyzeReplayMcapIfReady;
            EditorApplication.update -= CompleteFindLatestRecordingIfReady;
            if (_pendingWorkCts != null)
            {
                _pendingWorkCts.Cancel();
                _pendingWorkCts.Dispose();
                _pendingWorkCts = null;
            }

            _analyzeReplayTask = null;
            _findLatestRecordingTask = null;
            _pendingLatestSerializedObject = null;
            _pendingLatestTargetObject = null;
            _pendingLatestReplayPath = null;
        }

        private CancellationToken StartNewPendingWork()
        {
            CancelPendingWork();
            _pendingWorkCts = new CancellationTokenSource();
            return _pendingWorkCts.Token;
        }

        private void DisposePendingWorkToken()
        {
            if (_pendingWorkCts == null)
                return;

            _pendingWorkCts.Dispose();
            _pendingWorkCts = null;
        }

        private void CompleteAnalyzeReplayMcapIfReady()
        {
            if (_analyzeReplayTask == null || !_analyzeReplayTask.IsCompleted)
                return;

            EditorApplication.update -= CompleteAnalyzeReplayMcapIfReady;
            var task = _analyzeReplayTask;
            _analyzeReplayTask = null;

            McapReplayAnalysisResult result;
            try
            {
                result = task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                DisposePendingWorkToken();
                return;
            }
            catch (Exception ex)
            {
                DisposePendingWorkToken();
                SetMcapPreflightMessage($"MCAP preflight failed: {ex.Message}", MessageType.Error);
                return;
            }

            DisposePendingWorkToken();
            if (!result.Success)
            {
                SetMcapPreflightMessage($"MCAP preflight failed: {result.ErrorMessage}", MessageType.Error);
                return;
            }

            var topicText = string.Join("\n", result.Topics);
            SetMcapPreflightMessage(
                "Path: " + MakeRelative(result.Path) + "\n"
                + $"Size: {result.SizeBytes:N0} bytes\n"
                + $"Channels: {result.ChannelCount}\n"
                + $"Chunks: {result.ChunkCount}\n"
                + $"Messages: {result.MessageCount}\n"
                + $"Time Range (UTC): {result.HumanMessageRange}\n"
                + $"Raw Time Range: {result.RawMessageRange}\n"
                + $"Metadata Indexes: {result.MetadataIndexCount}\n"
                + $"Attachment Indexes: {result.AttachmentIndexCount}\n"
                + "Topic Preview: " + BuildTopicPreview(result.Topics),
                MessageType.Info,
                topicText,
                result.Topics.Count);
        }

        private static McapReplayAnalysisResult AnalyzeReplayMcapWorker(string path, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var indexed = McapIndexedReader.OpenRead(path);
                cancellationToken.ThrowIfCancellationRequested();
                var statistics = indexed.Summary.Statistics;
                return new McapReplayAnalysisResult(
                    path,
                    new FileInfo(path).Length,
                    indexed.Channels.Count,
                    indexed.Summary.ChunkIndexes.Count,
                    statistics == null ? "unavailable" : statistics.MessageCount.ToString("N0"),
                    statistics == null ? "unavailable" : $"{statistics.MessageStartTime} - {statistics.MessageEndTime} ns",
                    FormatMcapTimeRange(statistics),
                    indexed.MetadataIndexes.Count,
                    indexed.AttachmentIndexes.Count,
                    BuildTopicList(indexed.Channels));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return McapReplayAnalysisResult.Failed(path, ex.Message);
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

        private void SetIdentityMessage(string message, MessageType messageType)
        {
            _identitySummary = message;
            _identityMessageType = messageType;
        }

        private void AnalyzeReplayIdentity(string path, bool refreshCurrentEvidence)
        {
            _selectedReplayPath = path;
            _selectedSidecarDirectory = Path.ChangeExtension(Path.GetFullPath(path), ".schema");

            var warnings = new List<string>();
            var refreshFailed = false;
            if (refreshCurrentEvidence)
            {
                try
                {
                    Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts();
                    AssetDatabase.Refresh();
                }
                catch (Exception ex)
                {
                    refreshFailed = true;
                    warnings.Add("Failed to refresh current evidence: " + ex.Message);
                }
            }

            var recordedHash = ReadRecordedFoxRunHash(_selectedSidecarDirectory, warnings);
            var currentHash = refreshFailed
                ? string.Empty
                : ReadCurrentFoxRunHash(warnings);
            var status = IdentityStatus(recordedHash, currentHash);
            var messageType = status == "Match" ? MessageType.Info : MessageType.Warning;

            var lines = new List<string>
            {
                "Replay: " + MakeRelative(path),
                "Recording Evidence: " + MakeRelative(_selectedSidecarDirectory),
                "Recorded FoxRun Hash: " + FormatHash(recordedHash),
                "Current FoxRun Hash: " + FormatHash(currentHash),
                "Status: " + status
            };

            for (var i = 0; i < warnings.Count; i++)
                lines.Add("Warning: " + warnings[i]);

            SetIdentityMessage(string.Join("\n", lines), messageType);
        }

        private void OpenRecordingEvidence()
        {
            if (!string.IsNullOrEmpty(_selectedSidecarDirectory) && Directory.Exists(_selectedSidecarDirectory))
            {
                EditorUtility.RevealInFinder(_selectedSidecarDirectory);
                return;
            }

            SetIdentityMessage(
                "Missing Evidence: recording sidecar was not found for " + MakeRelative(_selectedReplayPath),
                MessageType.Warning);
        }

        private static void ApplyReplayPath(
            SerializedObject serializedObject,
            UnityEngine.Object targetObject,
            SerializedProperty replayPath,
            string projectRelativeReplayPath)
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            replayPath.stringValue = projectRelativeReplayPath;
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            EditorUtility.SetDirty(targetObject);
            InternalEditorUtility.RepaintAllViews();
        }

        private static string ReadRecordedFoxRunHash(string sidecarDirectory, List<string> warnings)
        {
            if (string.IsNullOrEmpty(sidecarDirectory) || !Directory.Exists(sidecarDirectory))
            {
                warnings.Add("Recording sidecar is missing.");
                return string.Empty;
            }

            var indexPath = Path.Combine(sidecarDirectory, "schema-evidence.json");
            if (!File.Exists(indexPath))
            {
                warnings.Add("schema-evidence.json is missing from the recording sidecar.");
                return string.Empty;
            }

            try
            {
                var index = JObject.Parse(File.ReadAllText(indexPath));
                var hash = index["foxRun"]?["globalManifestHash"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(hash))
                    warnings.Add("Recorded FoxRun hash is missing from schema-evidence.json.");
                return hash.Trim();
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to read recording schema evidence: " + ex.Message);
                return string.Empty;
            }
        }

        private static string ReadCurrentFoxRunHash(List<string> warnings)
        {
            var hashPath = Path.Combine(
                Unity2FoxgloveSchemaEvidencePaths.ResolveFoxRunOutputDirectory(),
                "foxrun.manifest.hash");
            if (!File.Exists(hashPath))
            {
                warnings.Add("Current FoxRun hash is missing. Refresh current evidence first.");
                return string.Empty;
            }

            return File.ReadAllText(hashPath).Trim();
        }

        private static string IdentityStatus(string recordedHash, string currentHash)
        {
            if (string.IsNullOrWhiteSpace(recordedHash) || string.IsNullOrWhiteSpace(currentHash))
                return "Missing Evidence";

            return string.Equals(recordedHash.Trim(), currentHash.Trim(), StringComparison.Ordinal)
                ? "Match"
                : "Mismatch";
        }

        private static string FormatHash(string hash)
        {
            return string.IsNullOrWhiteSpace(hash) ? "(missing)" : hash.Trim();
        }

        private void StartFindLatestReadableRecording(
            SerializedObject serializedObject,
            UnityEngine.Object targetObject,
            SerializedProperty replayPath)
        {
            SetIdentityMessage("Searching latest readable recording...", MessageType.Info);
            var recordingsDir = Path.Combine(GetDefaultDir(), "Recordings");
            var token = StartNewPendingWork();
            _pendingLatestSerializedObject = serializedObject;
            _pendingLatestTargetObject = targetObject;
            _pendingLatestReplayPath = replayPath?.Copy();
            _findLatestRecordingTask = Task.Run(() => FindLatestReadableRecordingWorker(recordingsDir, token), token);
            EditorApplication.update -= CompleteFindLatestRecordingIfReady;
            EditorApplication.update += CompleteFindLatestRecordingIfReady;
        }

        private void CompleteFindLatestRecordingIfReady()
        {
            if (_findLatestRecordingTask == null || !_findLatestRecordingTask.IsCompleted)
                return;

            EditorApplication.update -= CompleteFindLatestRecordingIfReady;
            var task = _findLatestRecordingTask;
            _findLatestRecordingTask = null;

            LatestRecordingResult result;
            try
            {
                result = task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                DisposePendingWorkToken();
                return;
            }
            catch (Exception ex)
            {
                DisposePendingWorkToken();
                SetIdentityMessage("Latest recording search failed: " + ex.Message, MessageType.Error);
                return;
            }

            DisposePendingWorkToken();
            if (!result.Success)
            {
                SetIdentityMessage(result.ErrorMessage, MessageType.Warning);
                return;
            }

            if (_pendingLatestReplayPath == null
                || _pendingLatestSerializedObject == null
                || _pendingLatestSerializedObject.targetObject == null)
            {
                return;
            }

            var latestRecording = result.Path;
            var projectRelativeReplayPath = MakeRelative(latestRecording);
            ApplyReplayPath(
                _pendingLatestSerializedObject,
                _pendingLatestTargetObject,
                _pendingLatestReplayPath,
                projectRelativeReplayPath);
            StartAnalyzeReplayMcap(latestRecording);
        }

        private static LatestRecordingResult FindLatestReadableRecordingWorker(string recordingsDir, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(recordingsDir))
            {
                return LatestRecordingResult.Failed($"Recordings directory was not found: {recordingsDir}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var paths = Directory.GetFiles(recordingsDir, "*.mcap", SearchOption.AllDirectories);
            var candidates = new LatestRecordingCandidate[paths.Length];
            for (var i = 0; i < paths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates[i] = LatestRecordingCandidate.FromPath(paths[i]);
            }

            Array.Sort(candidates, (left, right) =>
                right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

            var lastError = string.Empty;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(candidate.Path);
                    if (info.Length <= 0)
                        continue;

                    using (McapIndexedReader.OpenRead(candidate.Path))
                    {
                    }

                    return LatestRecordingResult.Found(candidate.Path);
                }
                catch (InvalidDataException)
                {
                    lastError = "Skipping unreadable MCAP '" + candidate.Path + "'.";
                }
                catch (Exception ex) when (
                    ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is ArgumentException
                    || ex is NotSupportedException
                    || ex is PathTooLongException)
                {
                    lastError = "Skipping unreadable MCAP '" + candidate.Path + "': " + ex.Message;
                }
            }

            var error = $"No readable MCAP recordings were found under: {recordingsDir}";
            if (!string.IsNullOrEmpty(lastError))
                error += "\n" + lastError;
            return LatestRecordingResult.Failed(error);
        }

        private static List<string> BuildTopicList(IReadOnlyList<McapChannel> channels)
        {
            var topics = new List<string>();
            if (channels == null || channels.Count == 0)
                return topics;

            var seen = new HashSet<string>();
            for (var i = 0; i < channels.Count; i++)
            {
                var topic = channels[i].Topic;
                if (string.IsNullOrEmpty(topic))
                    continue;

                if (seen.Add(topic))
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
            => FoxgloveManagerEditor.GetDefaultDir();

        private static string MakeRelative(string absolute)
            => FoxgloveManagerEditor.MakeRelative(absolute);

        private static string ResolveProjectPath(string path)
            => FoxgloveManagerEditor.ResolveProjectPath(path);

        private sealed class McapReplayAnalysisResult
        {
            public readonly bool Success;
            public readonly string Path;
            public readonly long SizeBytes;
            public readonly int ChannelCount;
            public readonly int ChunkCount;
            public readonly string MessageCount;
            public readonly string RawMessageRange;
            public readonly string HumanMessageRange;
            public readonly int MetadataIndexCount;
            public readonly int AttachmentIndexCount;
            public readonly List<string> Topics;
            public readonly string ErrorMessage;

            public McapReplayAnalysisResult(
                string path,
                long sizeBytes,
                int channelCount,
                int chunkCount,
                string messageCount,
                string rawMessageRange,
                string humanMessageRange,
                int metadataIndexCount,
                int attachmentIndexCount,
                List<string> topics)
            {
                Success = true;
                Path = path ?? string.Empty;
                SizeBytes = sizeBytes;
                ChannelCount = channelCount;
                ChunkCount = chunkCount;
                MessageCount = messageCount ?? "unavailable";
                RawMessageRange = rawMessageRange ?? "unavailable";
                HumanMessageRange = humanMessageRange ?? "unavailable";
                MetadataIndexCount = metadataIndexCount;
                AttachmentIndexCount = attachmentIndexCount;
                Topics = topics ?? new List<string>();
                ErrorMessage = string.Empty;
            }

            private McapReplayAnalysisResult(string path, string errorMessage)
            {
                Success = false;
                Path = path ?? string.Empty;
                Topics = new List<string>();
                ErrorMessage = errorMessage ?? string.Empty;
            }

            public static McapReplayAnalysisResult Failed(string path, string errorMessage)
                => new McapReplayAnalysisResult(path, errorMessage);
        }

        private sealed class LatestRecordingResult
        {
            public readonly bool Success;
            public readonly string Path;
            public readonly string ErrorMessage;

            private LatestRecordingResult(bool success, string path, string errorMessage)
            {
                Success = success;
                Path = path ?? string.Empty;
                ErrorMessage = errorMessage ?? string.Empty;
            }

            public static LatestRecordingResult Found(string path)
                => new LatestRecordingResult(true, path, string.Empty);

            public static LatestRecordingResult Failed(string errorMessage)
                => new LatestRecordingResult(false, string.Empty, errorMessage);
        }

        private sealed class LatestRecordingCandidate
        {
            private LatestRecordingCandidate(string path, DateTime lastWriteTimeUtc)
            {
                Path = path;
                LastWriteTimeUtc = lastWriteTimeUtc;
            }

            public string Path { get; }
            public DateTime LastWriteTimeUtc { get; }

            public static LatestRecordingCandidate FromPath(string path)
            {
                DateTime timestamp;
                try
                {
                    timestamp = File.GetLastWriteTimeUtc(path);
                }
                catch
                {
                    timestamp = DateTime.MinValue;
                }

                return new LatestRecordingCandidate(path, timestamp);
            }
        }
    }
}
