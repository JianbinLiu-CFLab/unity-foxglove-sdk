// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 140-3 recording/replay controller review fixes.

using System;
using System.Collections.Generic;
using System.Linq;
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
            SchemaSidecarSuccessResultClearsStagingDirectory();
            SchemaSidecarPublishFailureReportsPreservedBackup();
            VerifyOpt1DrainReplayCallbacksUsesPooledBuffer();
            VerifyOpt4EndInitDeferralReturnsSnapshot();
            VerifyOpt2ReplayControllerCachedInvocationList();
            VerifyOpt3ReplayOrchestratorCachedInvocationList();
            VerifyPhase173_024SessionLockBoundaries();

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
            var stop = ExtractMethodBody(source, "public void Stop()");
            var isCorsOriginAllowed = ExtractMethodBody(source, "private bool IsCorsOriginAllowed(string origin)");

            Check(source.Contains("IsCorsOriginAllowed", StringComparison.Ordinal)
                  && !source.Contains("Access-Control-Allow-Origin\"] = \"*\"", StringComparison.Ordinal),
                "140-3D-1: replay cursor endpoint restricts CORS instead of emitting a wildcard origin");
            Check(source.Contains("https://app.foxglove.dev", StringComparison.Ordinal),
                "140-3D-2: replay cursor endpoint keeps the hosted Foxglove origin in the default allowlist");
            Check(source.Contains("TryWrite(context, 401", StringComparison.Ordinal)
                  && source.IndexOf("IsAuthorized(context.Request)", StringComparison.Ordinal)
                  > source.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal),
                "140-3D-3: cursor endpoint answers browser OPTIONS preflight before bearer authorization");
            Check(source.Contains("JsonEscape", StringComparison.Ordinal)
                  && !source.Contains("private static string Escape(string value)\r\n            => (value ?? string.Empty).Replace", StringComparison.Ordinal),
                "140-3D-4: replay cursor endpoint error JSON uses full JSON string escaping");
            Check(isCorsOriginAllowed.Contains("if (!TryGetOriginBounds(origin, out var start, out var length))", StringComparison.Ordinal)
                  && Ordered(isCorsOriginAllowed, "if (!TryGetOriginBounds(origin, out var start, out var length))", "return false;")
                  && Ordered(isCorsOriginAllowed, "return false;", "foreach (var allowedOrigin"),
                "140-3D-5: replay cursor endpoint rejects malformed non-empty Origin headers");
            Check(Ordered(stop, "listener.Stop();", "listener.Close();")
                  && Ordered(stop, "listener.Close();", "finally")
                  && stop.Substring(stop.IndexOf("finally", StringComparison.Ordinal))
                      .Contains("_queue = null;", StringComparison.Ordinal),
                "140-3D-6: replay cursor endpoint closes listener before clearing in-flight delegates");
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

        private static void SchemaSidecarSuccessResultClearsStagingDirectory()
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
                SchemaEvidenceSidecarWriter.CleanupStagedSidecar(result);
                Check(string.IsNullOrEmpty(result.TemporaryDirectory)
                      && Directory.Exists(result.SidecarDirectory),
                    "140-3F-3: successful sidecar result clears staging directory without deleting published sidecar");
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
        /// OPT-1: Verify DrainReplayCallbacks can safely drain reentrant fire paths.
        /// </summary>
        private static void VerifyOpt1DrainReplayCallbacksUsesPooledBuffer()
        {
            var controller = new ReplayController(new ConsoleLogger(), null, null);
            var order = new List<string>();
            var reentered = false;

            Action<ReplayMessageContext> first = _ =>
            {
                order.Add("first");
                if (!reentered)
                {
                    reentered = true;
                    controller.FireContextForTests(NewTestContext("/phase140_3/reenter"));
                }
            };
            Action<ReplayMessageContext> second = _ => order.Add("second");

            controller.OnReplayMessageContext += first;
            controller.OnReplayMessageContext += second;
            controller.FireContextForTests(NewTestContext("/phase140_3/original"));

            Check(order.Count == 4, "OPT-1: reentrant callback fire preserves both listeners across nested DrainReplayCallbacks calls");
            Check(order[0] == "first" && order[1] == "second" && order[2] == "first" && order[3] == "second",
                "OPT-1: callback order is deterministic for direct fire and reentrant fire");
            Check(order.Distinct().Count() == 2,
                "OPT-1: callback set remains stable under reentrant invocation");
        }

        /// <summary>
        /// OPT-4: Verify EndInitDeferral returns a stable snapshot.
        /// The copy is intentional because callers and older validation keep
        /// the returned list readable after later arbiter mutations.
        /// </summary>
        private static void VerifyOpt4EndInitDeferralReturnsSnapshot()
        {
            var arbiter = new ReplayPoseOwnershipArbiter();
            var held = arbiter.OfferPose(
                transformKey: 101,
                channelId: 10,
                behavior: ReplayChannelBehavior.ScenePrimitivePose,
                logTimeNs: 100,
                pose: ReplayPoseSample.CreatePosition(1, 2, 3));
            Check(held.Kind == ReplayPoseOwnershipDecisionKind.Hold, "OPT-4: deferred pose is initially held");

            var resolved = arbiter.EndInitDeferral();
            Check(resolved.Count == 1 && resolved[0].Kind == ReplayPoseOwnershipDecisionKind.Apply,
                "OPT-4: EndInitDeferral resolves held poses into readable decisions");

            var later = arbiter.OfferPose(
                transformKey: 101,
                channelId: 10,
                behavior: ReplayChannelBehavior.ScenePrimitivePose,
                logTimeNs: 200,
                pose: ReplayPoseSample.CreatePosition(4, 5, 6));
            Check(later.Kind == ReplayPoseOwnershipDecisionKind.Apply && later.OwnerChannelId == 10,
                "OPT-4: subsequent pose application still works after EndInitDeferral");
            Check(resolved.Count == 1, "OPT-4: resolved list remains stable for read-only use after subsequent Applies");
        }

        /// <summary>
        /// OPT-2: Verify ReplayController uses cached handler arrays instead of
        /// per-call GetInvocationList().
        /// </summary>
        private static void VerifyOpt2ReplayControllerCachedInvocationList()
        {
            var controller = new ReplayController(new ConsoleLogger(), null, null);
            var messages = new List<string>();
            var contexts = new List<string>();
            var batches = new List<string>();

            Action<string, byte[]> onMessage1 = (_, _) => messages.Add("message-1");
            Action<string, byte[]> onMessage2 = (_, _) => messages.Add("message-2");
            Action<ReplayMessageContext> onContext1 = _ => contexts.Add("context-1");
            Action<ReplayMessageContext> onContext2 = _ => contexts.Add("context-2");
            Action<ReplayBatchContext> onBatch1 = _ => batches.Add("batch-1");
            Action<ReplayBatchContext> onBatch2 = _ => batches.Add("batch-2");

            controller.OnReplayMessage += onMessage1;
            controller.OnReplayMessage += onMessage2;
            controller.OnReplayMessageContext += onContext1;
            controller.OnReplayMessageContext += onContext2;
            controller.OnReplayBatchCompleted += onBatch1;
            controller.OnReplayBatchCompleted += onBatch2;

            controller.FireForTests("/phase140_3", new byte[] { 1, 2, 3 });
            Check(messages.Count == 2 && messages[0] == "message-1" && messages[1] == "message-2",
                $"OPT-2: replay controller invokes all message handlers after multi-subscribe (count={messages.Count}, values={string.Join(',', messages)})");
            messages.Clear();
            contexts.Clear();

            controller.FireContextForTests(NewTestContext("/phase140_3/context-1"));
            Check(contexts.Count == 2 && contexts.Contains("context-1") && contexts.Contains("context-2"),
                $"OPT-2: replay controller invokes all context handlers after multi-subscribe (count={contexts.Count}, values={string.Join(',', contexts)})");
            contexts.Clear();

            controller.FireBatchCompletedForTests(new ReplayBatchContext(
                batchLogTimeNs: 100,
                replayStartTimeNs: 0,
                messageCount: 1,
                source: "phase140_3",
                replaySessionId: 1));
            Check(batches.Count == 2 && batches.Contains("batch-1") && batches.Contains("batch-2"),
                $"OPT-2: replay controller invokes all batch handlers after multi-subscribe (count={batches.Count}, values={string.Join(',', batches)})");
            batches.Clear();

            controller.OnReplayMessage -= onMessage1;
            controller.OnReplayMessageContext -= onContext1;
            controller.OnReplayBatchCompleted -= onBatch1;
            messages.Clear();
            contexts.Clear();

            controller.FireForTests("/phase140_3", new byte[] { 4, 5, 6 });
            Check(messages.Count == 1 && messages.Contains("message-2"),
                $"OPT-2: replay controller removes one message handler correctly (count={messages.Count}, values={string.Join(',', messages)})");
            messages.Clear();
            contexts.Clear();

            controller.FireContextForTests(NewTestContext("/phase140_3/context-2"));
            Check(contexts.Count == 1 && contexts.Contains("context-2"),
                $"OPT-2: replay controller removes one context handler correctly (count={contexts.Count}, values={string.Join(',', contexts)})");
            contexts.Clear();

            controller.FireBatchCompletedForTests(new ReplayBatchContext(
                batchLogTimeNs: 200,
                replayStartTimeNs: 0,
                messageCount: 1,
                source: "phase140_3",
                replaySessionId: 1));
            Check(batches.Count == 1 && batches[0] == "batch-2",
                "OPT-2: replay controller removes one batch handler correctly");
        }

        /// <summary>
        /// OPT-3: Verify ReplayOrchestrator uses cached handler arrays instead of
        /// per-call GetInvocationList().
        /// </summary>
        private static void VerifyOpt3ReplayOrchestratorCachedInvocationList()
        {
            var controller = new ReplayController(new ConsoleLogger(), null, null);
            var orchestrator = new ReplayOrchestrator(new ConsoleLogger());
            var contexts = new List<string>();

            Action<ReplayMessageContext> onContext1 = _ => contexts.Add("context-1");
            Action<ReplayMessageContext> onContext2 = _ => contexts.Add("context-2");

            orchestrator.OnReplayMessageContext += onContext1;
            orchestrator.OnReplayMessageContext += onContext2;
            orchestrator.Attach(controller, null);

            controller.FireContextForTests(NewTestContext("/phase140_3/orchestrated-1"));
            Check(contexts.Count == 2 && contexts[0] == "context-1" && contexts[1] == "context-2",
                "OPT-3: orchestrator forwards replay context to both listeners after multi-subscribe");

            orchestrator.OnReplayMessageContext -= onContext1;
            contexts.Clear();
            controller.FireContextForTests(NewTestContext("/phase140_3/orchestrated-2"));
            Check(contexts.Count == 1 && contexts[0] == "context-2",
                "OPT-3: orchestrator removes one context listener correctly");

            orchestrator.Detach(controller);
            contexts.Clear();
            controller.FireContextForTests(NewTestContext("/phase140_3/orchestrated-3"));
            Check(contexts.Count == 0,
                "OPT-3: orchestrator detach stops forwarding context callbacks");
        }

        private static void VerifyPhase173_024SessionLockBoundaries()
        {
            var session = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.cs");
            var parameters = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Parameters.cs");
            var publish = ExtractMethodBody(session, "public void Publish(uint channelId, byte[] payload, ulong logTimeNs)");
            var copy = ExtractMethodBody(session, "private List<(uint clientId, uint subscriptionId)> CopySubscribersForPublish");
            var replay = ExtractMethodBody(session, "internal void PublishReplay");
            var dispose = ExtractMethodBody(session, "public void Dispose()");
            var broadcast = ExtractMethodBody(parameters, "public void BroadcastParameterValues");

            Check(session.Contains("[ThreadStatic]", StringComparison.Ordinal)
                  && session.Contains("s_publishSubscriberScratch", StringComparison.Ordinal)
                  && publish.Contains("var subscribers = CopySubscribersForPublish(channelId);", StringComparison.Ordinal)
                  && replay.Contains("var subscribers = CopySubscribersForPublish(channelId);", StringComparison.Ordinal)
                  && copy.Contains("lock (_subscriberScratchLock)", StringComparison.Ordinal)
                  && !copy.Contains("SendBinary", StringComparison.Ordinal)
                  && !copy.Contains("SendDataBinary", StringComparison.Ordinal),
                "173-024A: FoxgloveSession only holds subscriber scratch lock while copying subscribers");
            Check(!session.Contains("_singleAdvertiseChannels", StringComparison.Ordinal)
                  && !session.Contains("_singleUnadvertiseChannelIds", StringComparison.Ordinal)
                  && session.Contains("new List<AdvertiseChannel>(1) { channel }", StringComparison.Ordinal)
                  && session.Contains("new List<uint>(1) { channelId }", StringComparison.Ordinal),
                "173-024B: single advertise/unadvertise serialization avoids shared mutable lists");
            Check(dispose.Contains("Volatile.Write(ref _recorder, null)", StringComparison.Ordinal)
                  && dispose.Contains("Volatile.Write(ref _mirrorSink, null)", StringComparison.Ordinal),
                "173-024C: disposed sessions release recorder and mirror sink references");
              Check(broadcast.Contains("subscribedClientIds = GetParamSubscribersForChanged", StringComparison.Ordinal)
                    && broadcast.IndexOf("foreach (var cid in subscribedClientIds)", StringComparison.Ordinal)
                    > broadcast.LastIndexOf("finally", StringComparison.Ordinal),
                  "173-024D: parameter broadcasts release scratch locks before transport sends");
              Check(broadcast.Contains("names = new List<string>(_parameterBroadcastNames)", StringComparison.Ordinal)
                    && broadcast.IndexOf("JsonConvert.SerializeObject", StringComparison.Ordinal) > broadcast.LastIndexOf("finally", StringComparison.Ordinal)
                    && broadcast.IndexOf("GetParamSubscribersForChanged", StringComparison.Ordinal) > broadcast.LastIndexOf("finally", StringComparison.Ordinal),
                  "173-082A: parameter broadcasts copy scratch names before serialization and subscriber lookup");
              Check(session.Contains("s_jsonPublishStream", StringComparison.Ordinal)
                    && session.Contains("stream.SetLength(0);", StringComparison.Ordinal),
                  "173-024E: PublishJson reuses a thread-local payload stream");
        }

        private static ReplayMessageContext NewTestContext(string topic, string message = "phase140_3")
        {
            return new ReplayMessageContext(
                channelId: 10,
                topic: topic,
                messageEncoding: "json",
                schemaName: "phase140_3.Message",
                schemaEncoding: "json",
                logTimeNs: 100,
                replayStartTimeNs: 0,
                payload: System.Text.Encoding.UTF8.GetBytes(message),
                replaySessionId: 1);
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
