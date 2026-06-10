// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-3 recording/replay controller review fixes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Review-driven validation for recording/replay controller defects found in Phase 140-3.
    /// </summary>
    public static class Phase140_3Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 140-3 recording/replay controller review checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 140-3: Recording and replay controller review fixes ===");
            _passed = 0;

            RecordingDisableStopsActiveRecorderAndClearsParameterStore();
            ReplayControllerStateUsesPublishedMemorySemantics();
            ReplayCursorJsonEscapesControlCharactersAndUsesNamedStatuses();
            ReplayCursorEndpointRestrictsCorsAndEscapesJson();
            ExternalCursorEnabledCheckIsSynchronized();
            SchemaSidecarSuccessResultPointsAtExistingDirectory();
            SchemaSidecarPublishFailureReportsPreservedBackup();
            VerifyOpt1DrainReplayCallbacksUsesPooledBuffer();
            VerifyOpt4EndInitDeferralNoToArray();
            VerifyOpt2ReplayControllerCachedInvocationList();
            VerifyOpt3ReplayOrchestratorCachedInvocationList();

            Console.WriteLine($"Phase 140-3: {_passed} checks passed.");
        }

        private static void RecordingDisableStopsActiveRecorderAndClearsParameterStore()
        {
            var tempRoot = NewTempRoot();
            try
            {
                var transport = new Phase140_3FakeTransport();
                using var session = new FoxgloveSession("phase140-3", transport);
                var parameters = new FoxgloveParameterStore(new ConsoleLogger());
                using var controller = new RecordingController(new ConsoleLogger(), new SystemClock());
                var channel = new AdvertiseChannel
                {
                    Id = 1,
                    Topic = "/phase140_3",
                    Encoding = "json",
                    SchemaName = "phase140_3.Message",
                    SchemaEncoding = "jsonschema",
                    Schema = "{}"
                };

                session.RegisterChannel(channel);
                controller.Enable(Path.Combine(tempRoot, "recording.mcap"));
                controller.AttachToSession(parameters, session);

                Check(session.HasChannelDemand(channel.Id),
                    "140-3A-1: attached recorder creates recording demand before disable");

                controller.Disable();

                Check(!controller.IsEnabled,
                    "140-3A-2: recording state reports disabled after Disable");
                Check(!session.HasChannelDemand(channel.Id),
                    "140-3A-3: Disable detaches the active recorder from the session");
                Check(GetPrivateField<object>(controller, "_parameters") == null,
                    "140-3A-4: recording detach clears the retained parameter store reference");
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static void ReplayControllerStateUsesPublishedMemorySemantics()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");

            Check(source.Contains("Volatile.Read(ref _replayEnabled)", StringComparison.Ordinal),
                "140-3B-1: replay enabled state is read through a volatile publication boundary");
            Check(source.Contains("Volatile.Write(ref _replayEnabled", StringComparison.Ordinal),
                "140-3B-2: replay enabled state is written through a volatile publication boundary");
            Check(source.Contains("Volatile.Read(ref _lastEnableHadSchemaMismatch)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _lastEnableBlockedBySchemaMismatch)", StringComparison.Ordinal)
                  && source.Contains("Volatile.Read(ref _lastEnableFailureMessage)", StringComparison.Ordinal),
                "140-3B-3: replay enable diagnostic properties use published reads");
        }

        private static void ReplayCursorJsonEscapesControlCharactersAndUsesNamedStatuses()
        {
            var json = ReplayCursorState.Unavailable("line1\nline2\t\b\f\r\0").ToJson();
            var parsed = JObject.Parse(json);

            Check((string)parsed["message"] == "line1\nline2\t\b\f\r\0",
                "140-3C-1: ReplayCursorState JSON escapes control characters and parses back");

            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayCursorRequest.cs");
            Check(source.Contains("PlaybackStatusPlaying", StringComparison.Ordinal)
                  && source.Contains("PlaybackStatusEnded", StringComparison.Ordinal)
                  && !source.Contains("snapshot.Status == 0", StringComparison.Ordinal)
                  && !source.Contains("snapshot.Status == 3", StringComparison.Ordinal),
                "140-3C-2: ReplayCursorState status mapping uses named constants instead of magic numbers");
        }

        private static void ReplayCursorEndpointRestrictsCorsAndEscapesJson()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");

            Check(source.Contains("IsCorsOriginAllowed", StringComparison.Ordinal)
                  && !source.Contains("Access-Control-Allow-Origin\"] = \"*\"", StringComparison.Ordinal),
                "140-3D-1: replay cursor endpoint restricts CORS instead of emitting a wildcard origin");
            Check(source.Contains("https://app.foxglove.dev", StringComparison.Ordinal),
                "140-3D-2: replay cursor endpoint keeps the hosted Foxglove origin in the default allowlist");
            Check(source.Contains("TryWrite(context, 401", StringComparison.Ordinal)
                  && source.IndexOf("IsAuthorized(context.Request)", StringComparison.Ordinal)
                  < source.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal),
                "140-3D-3: bearer authorization is checked before OPTIONS responses");
            Check(source.Contains("JsonEscape", StringComparison.Ordinal)
                  && !source.Contains("private static string Escape(string value)\r\n            => (value ?? string.Empty).Replace", StringComparison.Ordinal),
                "140-3D-4: replay cursor endpoint error JSON uses full JSON string escaping");
        }

        private static void ExternalCursorEnabledCheckIsSynchronized()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ExternalReplayCursorController.cs");
            var tryEnqueue = ExtractMethodBody(source, "public ExternalReplayCursorEnqueueResult TryEnqueue(");

            Check(source.Contains("Volatile.Read(ref _enabled)", StringComparison.Ordinal)
                  || Ordered(tryEnqueue, "lock (_gate)", "if (!Enabled)") 
                  || Ordered(tryEnqueue, "lock (_gate)", "if (!IsEnabled"),
                "140-3E-1: external replay cursor Enabled gate is synchronized with enqueue");
        }

        private static void SchemaSidecarSuccessResultPointsAtExistingDirectory()
        {
            var tempRoot = NewTempRoot();
            try
            {
                var evidenceRoot = Path.Combine(tempRoot, "evidence");
                CreateSchemaEvidence(evidenceRoot);
                var result = SchemaEvidenceSidecarWriter.WriteSidecar(
                    Path.Combine(tempRoot, "recording.mcap"),
                    evidenceRoot,
                    SchemaIdentityMode.Strict,
                    requireComplete: true);

                Check(result.Success,
                    "140-3F-1: schema sidecar fixture writes successfully");
                Check(Directory.Exists(result.SidecarDirectory),
                    "140-3F-2: schema sidecar final directory exists after success");
                Check(result.TemporaryDirectory == result.SidecarDirectory
                      && Directory.Exists(result.TemporaryDirectory),
                    "140-3F-3: successful sidecar result reports an existing published directory");
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static void SchemaSidecarPublishFailureReportsPreservedBackup()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/SchemaEvidenceSidecarWriter.cs");

            Check(source.Contains("Preserved existing sidecar backup", StringComparison.Ordinal)
                  && source.Contains("backupDirectory", StringComparison.Ordinal),
                "140-3G-1: sidecar publish failure warning reports the preserved backup directory");
        }

        private static void CreateSchemaEvidence(string evidenceRoot)
        {
            WriteEvidenceFile(evidenceRoot, "FoxRun", "foxrun.manifest.json", "{}");
            WriteEvidenceFile(evidenceRoot, "FoxRun", "foxrun.manifest.hash", "foxrun-hash");
            WriteEvidenceFile(evidenceRoot, "FoxRun", "foxrun.manifest.report.json", "{}");
            WriteEvidenceFile(evidenceRoot, "FoxRun", "FoxRunSchemaInfo.g.cs", "// generated");
            WriteEvidenceFile(evidenceRoot, "FoxRun", "foxrun.generation-descriptor.json", "{}");
            WriteEvidenceFile(evidenceRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.json", "{}");
            WriteEvidenceFile(evidenceRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.hash", "sdk-hash");
            WriteEvidenceFile(evidenceRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.report.json", "{}");
        }

        private static void WriteEvidenceFile(string root, string group, string fileName, string content)
        {
            var directory = Path.Combine(root, group);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        /// <summary>
        /// OPT-1: Verify DrainReplayCallbacks uses a pooled drain buffer instead of
        /// allocating a new List on every call.
        /// </summary>
        private static void VerifyOpt1DrainReplayCallbacksUsesPooledBuffer()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var body = ExtractMethodBody(source, "public void DrainReplayCallbacks()");
            Check(!string.IsNullOrEmpty(body), "OPT-1: DrainReplayCallbacks method body found");
            Check(!body.Contains("new List<ReplayCallbackDispatch>", StringComparison.Ordinal),
                "OPT-1: DrainReplayCallbacks no longer allocates a new List per call");
            Check(source.Contains("_drainBuffer", StringComparison.Ordinal)
                || source.Contains("_replayCallbackDrainBuffer", StringComparison.Ordinal),
                "OPT-1: pooled drain buffer field exists");
            Check(body.Contains("Clear()", StringComparison.Ordinal),
                "OPT-1: drain buffer is cleared after iteration");
        }

        /// <summary>
        /// OPT-4: Verify EndInitDeferral returns _resolvedHeld directly
        /// instead of allocating a copy via ToArray().
        /// </summary>
        private static void VerifyOpt4EndInitDeferralNoToArray()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayPoseOwnershipArbiter.cs");
            var body = ExtractMethodBody(source, "public IReadOnlyList<ReplayPoseOwnershipDecision> EndInitDeferral()");
            Check(!string.IsNullOrEmpty(body), "OPT-4: EndInitDeferral method body found");
            Check(!body.Contains("ToArray()", StringComparison.Ordinal),
                "OPT-4: EndInitDeferral no longer calls ToArray()");
            Check(body.Contains("return _resolvedHeld", StringComparison.Ordinal),
                "OPT-4: EndInitDeferral returns _resolvedHeld directly");
        }

        /// <summary>
        /// OPT-2: Verify ReplayController uses cached handler arrays instead of
        /// per-call GetInvocationList().
        /// </summary>
        private static void VerifyOpt2ReplayControllerCachedInvocationList()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs");
            var body = ExtractMethodBody(source, "private void InvokeReplayMessage(");
            Check(!string.IsNullOrEmpty(body), "OPT-2: InvokeReplayMessage method body found");
            Check(!body.Contains("GetInvocationList()", StringComparison.Ordinal),
                "OPT-2: InvokeReplayMessage no longer calls GetInvocationList()");
            Check(source.Contains("_replayMessageHandlers", StringComparison.Ordinal),
                "OPT-2: cached _replayMessageHandlers array field exists");
        }

        /// <summary>
        /// OPT-3: Verify ReplayOrchestrator uses cached handler arrays instead of
        /// per-call GetInvocationList().
        /// </summary>
        private static void VerifyOpt3ReplayOrchestratorCachedInvocationList()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayOrchestrator.cs");
            var body = ExtractMethodBody(source, "private void SafeInvokeReplayMessage(");
            Check(!string.IsNullOrEmpty(body), "OPT-3: SafeInvokeReplayMessage method body found");
            Check(!body.Contains("GetInvocationList()", StringComparison.Ordinal),
                "OPT-3: SafeInvokeReplayMessage no longer calls GetInvocationList()");
            Check(source.Contains("_replayMessageHandlers", StringComparison.Ordinal),
                "OPT-3: cached _replayMessageHandlers array field exists");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        private static string ExtractMethodBody(string source, string signature)
        {
            var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
            if (signatureIndex < 0)
                return string.Empty;
            var braceIndex = source.IndexOf('{', signatureIndex);
            if (braceIndex < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(braceIndex, i - braceIndex + 1);
                }
            }

            return string.Empty;
        }

        private static bool Ordered(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().FullName, name);
            return (T)field.GetValue(target);
        }

        private static string NewTempRoot()
        {
            var path = Path.Combine(Path.GetTempPath(), "u2f-phase140-3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            try { Directory.Delete(path, recursive: true); }
            catch { }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class Phase140_3FakeTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() => Stop();
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Connect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void Disconnect(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void Text(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void Binary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
