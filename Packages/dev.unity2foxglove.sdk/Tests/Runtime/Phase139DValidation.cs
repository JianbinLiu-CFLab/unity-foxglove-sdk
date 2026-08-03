// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139D validation for the Unity cursor bridge feasibility surface.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139D cursor-bridge scaffold. These checks
    /// validate the internal signal contract and deliberately avoid exposing it
    /// as a product workflow until a Foxglove-side control path exists.
    /// </summary>
    public static class Phase139DValidation
    {
        private static readonly string[] ManagerServerSourcePaths =
        {
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.RemoteMcap.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.ReplayCursor.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.Secrets.cs"
        };

        private static readonly string CachedRepoRoot = ResolveRepoRoot();
        private static readonly Dictionary<string, string> SourceCache = new Dictionary<string, string>();

        private static int _passed;

        /// <summary>Runs all Phase 139D validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139D: Unity Replay Sync Feasibility Scaffold ===");
            _passed = 0;

            VerifyExtensionScaffold();
            VerifyCursorRequestContract();
            VerifyCursorControllerContract();
            VerifyEndpointContract();
            VerifyEndpointLoopbackBehavior();
            VerifyEndpointBearerTokenBehavior();
            VerifySmokeScript();
            VerifyWorkflowDocumentation();
            VerifyRuntimeWiring();
            VerifyMethodExtractorHandlesLiteralBraces();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 139D: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifyCursorRequestContract()
        {
            var json = "{\"source\":\"foxglove-unity-cursor-bridge\",\"sequence\":7,\"mode\":\"seek\",\"didSeek\":true,\"time\":{\"sec\":12,\"nsec\":345}}";
            Check(ReplayCursorRequest.TryParseJson(json, out var request, out _)
                  && request.TimeNs == 12_000_000_345UL
                  && request.Sequence == 7
                  && request.DidSeek
                  && request.Source == "foxglove-unity-cursor-bridge",
                "139D-2A: cursor request parses explicit split sec/nsec payload and seek flag");

            Check(ReplayCursorRequest.TryParseJson(
                      "{\"source\":\"foxglove-unity-cursor-bridge\",\"sequence\":8,\"mode\":\"advance\",\"didSeek\":false,\"time\":{\"sec\":13,\"nsec\":0}}",
                      out var advanceRequest,
                      out _)
                  && advanceRequest.TimeNs == 13_000_000_000UL
                  && !advanceRequest.DidSeek
                  && advanceRequest.Mode == "advance",
                "139D-2A2: cursor request distinguishes smooth playback advance from seek");

            Check(!ReplayCursorRequest.TryParseJson("{\"time\":{\"sec\":1,\"nsec\":1000000000}}", out _, out _),
                "139D-2B: cursor request rejects out-of-range nanoseconds");
            Check(!ReplayCursorRequest.TryParseJson("{\"time\":{\"sec\":-1,\"nsec\":0}}", out _, out _),
                "139D-2C: cursor request rejects negative seconds");
            Check(!ReplayCursorRequest.TryParseJson("{\"timeNs\":123}", out _, out _),
                "139D-2D: cursor request rejects JavaScript-number nanosecond payloads");
        }

        private static void VerifyCursorControllerContract()
        {
            var controller = new ExternalReplayCursorController();
            var request = ReplayCursorRequest.CreateForTests(20_000_000_000UL, "test", 1);

            Check(controller.TryEnqueue(request, replayEnabled: false, startNs: 0, endNs: 30_000_000_000UL, out var disabled)
                  == ExternalReplayCursorEnqueueResult.Disabled
                  && disabled.Contains("disabled", StringComparison.OrdinalIgnoreCase),
                "139D-3A: cursor controller rejects requests while disabled");

            controller.Enabled = true;
            Check(controller.TryEnqueue(request, replayEnabled: false, startNs: 0, endNs: 30_000_000_000UL, out var replayDisabled)
                  == ExternalReplayCursorEnqueueResult.ReplayUnavailable
                  && replayDisabled.Contains("replay", StringComparison.OrdinalIgnoreCase),
                "139D-3B: cursor controller rejects requests when replay is unavailable");

            Check(controller.TryEnqueue(request, replayEnabled: true, startNs: 10_000_000_000UL, endNs: 30_000_000_000UL, out _)
                  == ExternalReplayCursorEnqueueResult.Accepted
                  && controller.TryDrainLatest(out var drained)
                  && drained.TimeNs == request.TimeNs,
                "139D-3C: cursor controller queues accepted request for main-thread drain");

            Check(controller.TryEnqueue(request, replayEnabled: true, startNs: 10_000_000_000UL, endNs: 30_000_000_000UL, out _)
                  == ExternalReplayCursorEnqueueResult.Duplicate,
                "139D-3D: cursor controller ignores duplicate cursor values");

            Check(controller.TryEnqueue(ReplayCursorRequest.CreateForTests(40_000_000_000UL, "test", 2),
                      replayEnabled: true, startNs: 10_000_000_000UL, endNs: 30_000_000_000UL, out _)
                  == ExternalReplayCursorEnqueueResult.Accepted
                  && controller.TryDrainLatest(out var clamped)
                  && clamped.TimeNs == 30_000_000_000UL,
                "139D-3E: cursor controller clamps requests to replay range");

            controller.TryEnqueue(ReplayCursorRequest.CreateForTests(21_000_000_000UL, "test", 3),
                replayEnabled: true, startNs: 10_000_000_000UL, endNs: 30_000_000_000UL, out _);
            controller.TryEnqueue(ReplayCursorRequest.CreateForTests(22_000_000_000UL, "test", 4),
                replayEnabled: true, startNs: 10_000_000_000UL, endNs: 30_000_000_000UL, out _);
            Check(controller.TryDrainLatest(out var latest) && latest.TimeNs == 22_000_000_000UL && !controller.TryDrainLatest(out _),
                "139D-3F: cursor controller coalesces rapid updates to the latest cursor");
        }

        private static void VerifyEndpointContract()
        {
            var endpointSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/UnityReplayCursorEndpoint.cs");

            Check(UnityReplayCursorEndpointOptions.Default.Enabled == false
                  && UnityReplayCursorEndpointOptions.Default.Host == "127.0.0.1"
                  && UnityReplayCursorEndpointOptions.Default.Port == 8892,
                "139D-4A: cursor endpoint is disabled by default and loopback-scoped");
            Check(UnityReplayCursorEndpointOptions.Default.MaxBodyBytes <= 2048,
                "139D-4B: cursor endpoint keeps request bodies bounded");
            Check(!UnityReplayCursorEndpointOptions.Default.IsLoopbackAllowedHost("0.0.0.0"),
                "139D-4C: cursor endpoint rejects non-loopback hosts by default");
            Check(endpointSource.Contains("Authorization", StringComparison.Ordinal)
                  && endpointSource.Contains("Bearer ", StringComparison.Ordinal),
                "139D-4D: cursor endpoint supports an optional bearer token");
            var optionsBranch = endpointSource.IndexOf("HttpMethod, \"OPTIONS\"", StringComparison.Ordinal);
            var authGate = endpointSource.IndexOf("IsAuthorized(context.Request, options)", StringComparison.Ordinal);
            Check(endpointSource.Contains("Access-Control-Allow-Origin", StringComparison.Ordinal)
                  && optionsBranch >= 0
                  && authGate >= 0
                  && optionsBranch < authGate,
                "139D-4E: cursor endpoint handles browser CORS preflight before bearer-token auth");
            Check(endpointSource.Contains("ReplayCursorState", StringComparison.Ordinal)
                  && endpointSource.Contains("GET", StringComparison.Ordinal),
                "139D-4F: cursor endpoint exposes Unity replay state for Foxglove follow mode");

            var managerSource = ReadManagerServerSources();
            Check(managerSource.Contains("Replay cursor bridge disabled", StringComparison.Ordinal)
                  && managerSource.Contains("SetExternalReplayCursorEnabled(false)", StringComparison.Ordinal),
                "139D-4G: cursor endpoint startup failure does not fail the main server");
            Check(managerSource.Contains("Replay cursor bridge received cursor from", StringComparison.Ordinal)
                  && managerSource.Contains("request.Source", StringComparison.Ordinal),
                "139D-4H: cursor endpoint logs first accepted extension cursor for live gate evidence");
        }

        private static void VerifyEndpointLoopbackBehavior()
        {
            ReplayCursorRequest received = default;
            using var endpoint = StartCursorEndpointWithRetry(
                bearerToken: string.Empty,
                request =>
                {
                    received = request;
                    return new UnityReplayCursorEndpointQueueResult(true, "accepted");
                },
                out var port);

            var status = PostJsonStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                "{\"source\":\"test\",\"sequence\":3,\"mode\":\"seek\",\"time\":{\"sec\":22,\"nsec\":33}}",
                bearerToken: string.Empty,
                out var response);

            Check(status == HttpStatusCode.Accepted
                  && response.Contains("\"accepted\":true", StringComparison.Ordinal)
                  && received.TimeNs == 22_000_000_033UL,
                "139D-4I: cursor endpoint accepts loopback POSTs into the runtime queue");

            var preflight = PreflightStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                "https://app.foxglove.dev",
                out var allowOrigin);
            Check(preflight == HttpStatusCode.NoContent
                  && string.Equals(allowOrigin, "https://app.foxglove.dev", StringComparison.Ordinal),
                "139D-4I2: cursor endpoint accepts unauthenticated browser CORS preflight");

            var state = GetText($"http://127.0.0.1:{port}/v1/replay-cursor");
            Check(state.Contains("\"available\":", StringComparison.Ordinal)
                  && state.Contains("\"time\":", StringComparison.Ordinal)
                  && state.Contains("\"sec\":", StringComparison.Ordinal)
                  && state.Contains("\"nsec\":", StringComparison.Ordinal),
                "139D-4J: cursor endpoint returns split-time Unity replay state");
        }

        private static void VerifyEndpointBearerTokenBehavior()
        {
            var acceptedCount = 0;
            ReplayCursorRequest received = default;
            using var endpoint = StartCursorEndpointWithRetry(
                bearerToken: "phase139d-token",
                request =>
                {
                    acceptedCount++;
                    received = request;
                    return new UnityReplayCursorEndpointQueueResult(true, "accepted");
                },
                out var port);

            const string payload = "{\"source\":\"test\",\"sequence\":4,\"mode\":\"seek\",\"time\":{\"sec\":23,\"nsec\":44}}";
            var unauthorized = PostJsonStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                payload,
                bearerToken: string.Empty,
                out _);
            Check(unauthorized == HttpStatusCode.Unauthorized && acceptedCount == 0,
                "139D-4K: cursor endpoint rejects missing bearer token before queueing");

            var wrongToken = PostJsonStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                payload,
                bearerToken: "wrong-token",
                out _);
            Check(wrongToken == HttpStatusCode.Unauthorized && acceptedCount == 0,
                "139D-4L: cursor endpoint rejects wrong bearer token before queueing");

            var preflight = PreflightStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                "https://app.foxglove.dev",
                out var allowOrigin);
            Check(preflight == HttpStatusCode.NoContent
                  && string.Equals(allowOrigin, "https://app.foxglove.dev", StringComparison.Ordinal)
                  && acceptedCount == 0,
                "139D-4L2: token-protected cursor endpoint accepts browser CORS preflight without Authorization");

            var accepted = PostJsonStatus(
                $"http://127.0.0.1:{port}/v1/replay-cursor",
                payload,
                bearerToken: "phase139d-token",
                out var response);
            Check(accepted == HttpStatusCode.Accepted
                  && response.Contains("\"accepted\":true", StringComparison.Ordinal)
                  && acceptedCount == 1
                  && received.TimeNs == 23_000_000_044UL,
                "139D-4M: cursor endpoint accepts correct bearer token");
        }

        private static void VerifyExtensionScaffold()
        {
            var packageJson = Read("Tools/foxglove-extensions/unity-cursor-bridge/package.json");
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var readme = Read("Tools/foxglove-extensions/unity-cursor-bridge/README.md");

            Check(packageJson.Contains("\"name\": \"unity-cursor-bridge\"", StringComparison.Ordinal)
                  && packageJson.Contains("\"displayName\": \"Unity Replay Sync\"", StringComparison.Ordinal)
                  && packageJson.Contains("foxglove-extension build", StringComparison.Ordinal),
                "139D-1A: extension package declares the Unity replay sync panel");
            Check(source.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && source.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-1B: extension watches and reads Foxglove currentTime");
            Check(source.Contains("context.watch(\"startTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"endTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"didSeek\")", StringComparison.Ordinal),
                "139D-1C: extension watches timeline bounds and seek state");
            Check(source.Contains("sec: currentTime.sec", StringComparison.Ordinal)
                  && source.Contains("nsec: currentTime.nsec", StringComparison.Ordinal)
                  && source.Contains("renderState.didSeek === true ? \"seek\" : \"advance\"", StringComparison.Ordinal)
                  && source.Contains("fetch(endpoint", StringComparison.Ordinal),
                "139D-1D: extension sends split sec/nsec cursor metadata and seek/advance mode to loopback");
            Check(!source.Contains("/v1/data", StringComparison.Ordinal),
                "139D-1E: extension does not infer cursor state from Remote Data Loader ranges");
            Check(readme.Contains("sync switch is enabled by default", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("/v1/data", StringComparison.Ordinal)
                  && readme.Contains("playhead signal", StringComparison.OrdinalIgnoreCase),
                "139D-1F: extension README documents the enabled panel default and DataLoader boundary");
            Check(source.Contains("Replay time (UTC)", StringComparison.Ordinal)
                  && source.Contains("Unity is following Foxglove", StringComparison.Ordinal)
                  && source.Contains("Unity rejected replay time", StringComparison.Ordinal)
                  && !source.Contains("Current time", StringComparison.Ordinal)
                  && !source.Contains("Sent sequence", StringComparison.Ordinal)
                  && !source.Contains("Unity rejected sequence", StringComparison.Ordinal),
                "139D-1G: extension surfaces user-readable UTC replay time and sync status");
            Check(source.Contains("Sync Foxglove timeline to Unity", StringComparison.Ordinal)
                  && source.Contains("enabled: true", StringComparison.Ordinal)
                  && source.Contains("name: \"Unity Replay Sync\"", StringComparison.Ordinal)
                  && source.Contains("Follow Unity replay", StringComparison.Ordinal)
                  && source.Contains("seekPlayback", StringComparison.Ordinal)
                  && source.Contains("followUnity: false", StringComparison.Ordinal),
                "139D-1H: extension keeps the Foxglove-to-Unity sync default and adds an opt-in Follow Unity replay toggle (default off, Phase 140K Stage 3)");
            Check(!source.Contains("fetchUnityState", StringComparison.Ordinal)
                  && !source.Contains("suppressForwardUntilMs", StringComparison.Ordinal),
                "139D-1I: follow mode is forward-only ACK-paced via seekPlayback; it does not poll Unity state for reverse follow");
        }

        private static void VerifySmokeScript()
        {
            var script = Read("Scripts/smoke/replay/phase139d_unity_cursor_bridge_acceptance.py");

            Check(script.Contains("extension-metadata", StringComparison.Ordinal)
                  && script.Contains("endpoint-loopback", StringComparison.Ordinal),
                "139D-5A: smoke helper separates metadata and endpoint-loopback modes");
            Check(script.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && script.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-5B: smoke helper validates the extension currentTime contract");
            Check(script.Contains("build_cursor_payload", StringComparison.Ordinal)
                  && script.Contains("\"sec\"", StringComparison.Ordinal)
                  && script.Contains("\"nsec\"", StringComparison.Ordinal),
                "139D-5C: smoke helper sends explicit split-time cursor payloads");
            Check(script.Contains("not playhead-control evidence", StringComparison.Ordinal)
                  && script.Contains("/v1/data", StringComparison.Ordinal),
                "139D-5D: smoke helper documents that /v1/data is not a cursor source");
            Check(!script.Contains("supports_unity_to_foxglove_follow", StringComparison.Ordinal)
                  && !script.Contains("polls_unity_state_without_echo", StringComparison.Ordinal),
                "139D-5E: smoke helper does not validate reverse follow as a product feature");
        }

        private static void VerifyWorkflowDocumentation()
        {
            var docs = Read("docs/research-remote-timeline-scene-reproduction.md");

            Check(docs.Contains("Phase139D Unity Replay Sync Boundary", StringComparison.Ordinal),
                "139D-6A: research document contains a Phase139D replay sync section");
            Check(docs.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && docs.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-6B: documentation records the Foxglove extension currentTime contract");
            Check(docs.Contains("Do not infer Unity cursor state from `/v1/data`", StringComparison.Ordinal),
                "139D-6C: documentation forbids using Remote Data Loader data ranges as cursor signals");
            Check(docs.Contains("sync switch is enabled by default", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("loopback", StringComparison.OrdinalIgnoreCase),
                "139D-6D: documentation records the enabled panel default and loopback boundary");
            Check(docs.Contains("Foxglove Timeline Replay", StringComparison.Ordinal)
                  && docs.Contains("Foxglove controls replay", StringComparison.Ordinal)
                  && docs.Contains("Unity follows the", StringComparison.Ordinal)
                  && docs.Contains("Foxglove timeline", StringComparison.Ordinal),
                "139D-6E: documentation records the Foxglove-owned product boundary");
        }

        private static void VerifyRuntimeWiring()
        {
            var runtime = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var coordinator = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = ReadManagerServerSources();
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            var externalSeek = ExtractMethodBody(coordinator, "private void ReplaySeekExternalCursor");
            var externalAdvance = ExtractMethodBody(coordinator, "private static void ReplayAdvanceToExternalCursor");

            Check(runtime.Contains("ExternalReplayCursorController", StringComparison.Ordinal)
                  && runtime.Contains("TryEnqueueExternalReplayCursor", StringComparison.Ordinal),
                "139D-7A: runtime owns an external cursor controller");
            Check(runtime.Contains("GetExternalReplayCursorState", StringComparison.Ordinal)
                  && runtime.Contains("GetPlaybackState", StringComparison.Ordinal),
                "139D-7B: runtime exposes replay cursor state for the endpoint");
            Check(coordinator.Contains("TryDrainLatest", StringComparison.Ordinal)
                  && coordinator.Contains("ReplayAdvanceToExternalCursor", StringComparison.Ordinal)
                  && coordinator.Contains("ShouldTreatExternalCursorAsSeek", StringComparison.Ordinal)
                  && coordinator.Contains("cursor.DidSeek", StringComparison.Ordinal)
                  && coordinator.Contains("ApplyTickToScene", StringComparison.Ordinal)
                  && !coordinator.Contains("replay.Tick(session, timeNs", StringComparison.Ordinal)
                  && externalSeek.Contains("QueueReplaySceneSnapshot(timeNs)", StringComparison.Ordinal)
                  && !externalAdvance.Contains("QueueReplaySceneSnapshot", StringComparison.Ordinal),
                "139D-7C: runtime tick treats normal external cursors as scene-only smooth replay advances, not WebSocket snapshots");

            var replayController = PhaseValidationSourceHelpers.ReadReplayControllerSources();
            var sceneOnlyAdvance = ExtractMethodBody(replayController, "public void ApplyTickToScene(ulong timeNs, bool deferCallbacks)");
            Check(sceneOnlyAdvance.Contains("_replayEngine.Tick(timeNs, _replayTickBuffer)", StringComparison.Ordinal)
                  && sceneOnlyAdvance.Contains("ForwardReplayMessageToScene", StringComparison.Ordinal)
                  && sceneOnlyAdvance.Contains("FireReplayBatchCompleted", StringComparison.Ordinal)
                  && !sceneOnlyAdvance.Contains("PublishMessages", StringComparison.Ordinal)
                  && !sceneOnlyAdvance.Contains("PublishReplay", StringComparison.Ordinal),
                "139D-7C2: scene-only cursor advance avoids replay MessageData publication back to Foxglove");
            Check(manager.Contains("_enableReplayCursorBridge", StringComparison.Ordinal)
                  && manager.Contains("false", StringComparison.Ordinal),
                "139D-7D: manager exposes a disabled-by-default cursor bridge setting");
            Check(server.Contains("StartReplayCursorEndpointIfNeeded", StringComparison.Ordinal)
                  && server.Contains("StopReplayCursorEndpoint", StringComparison.Ordinal),
                "139D-7E: manager starts and stops the cursor endpoint with server lifecycle");
            Check(server.Contains("RefreshReplayCursorEndpointIfNeeded", StringComparison.Ordinal)
                  && manager.Contains("RefreshReplayCursorEndpointIfNeeded", StringComparison.Ordinal),
                "139D-7F: manager refreshes the cursor endpoint when Inspector settings change during Play Mode");
            Check(server.Contains("ShouldRunReplayCursorEndpoint", StringComparison.Ordinal)
                  && server.Contains("_remoteMcapFileServer != null", StringComparison.Ordinal)
                  && server.Contains("Replay cursor endpoint ready", StringComparison.Ordinal),
                "139D-7G: remote MCAP file access automatically enables the cursor endpoint");
            Check(!editor.Contains("Cursor Bridge (Advanced)", StringComparison.Ordinal)
                  && !editor.Contains("_enableReplayCursorBridge", StringComparison.Ordinal),
                "139D-7H: manager Inspector does not expose unfinished cursor bridge controls");
        }

        private static void VerifyMethodExtractorHandlesLiteralBraces()
        {
            const string source = @"
class Fixture
{
    private void Target()
    {
        var json = ""{literal}"";
        var format = ""value {0}"";
        AfterLiteralBraces();
    }

    private void Other() { ForbiddenAfterTarget(); }
}";
            var body = ExtractMethodBody(source, "private void Target()");
            Check(body.Contains("AfterLiteralBraces()", StringComparison.Ordinal)
                  && !body.Contains("ForbiddenAfterTarget", StringComparison.Ordinal),
                "139D-7I: validation method-body extraction ignores braces inside string literals");
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139d\"", StringComparison.Ordinal),
                "139D-8A: registry wires --phase139d");
            Check(registry.Contains("Phase139DValidation.Validate", StringComparison.Ordinal),
                "139D-8B: registry points Phase139D at the validation entrypoint");
            Check(project.Contains("Phase139DValidation.cs", StringComparison.Ordinal),
                "139D-8C: test project compiles Phase139DValidation");
        }

        /// <summary>Read a repository-relative text file for structural validation checks.</summary>
        private static string Read(string relativePath)
        {
            if (SourceCache.TryGetValue(relativePath, out var cached))
                return cached;

            var text = File.ReadAllText(RepoPath(relativePath));
            SourceCache[relativePath] = text;
            return text;
        }

        private static string ReadManagerServerSources()
        {
            var source = new StringBuilder();
            foreach (var path in ManagerServerSourcePaths)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(Read(path));
            }

            return source.ToString();
        }

        /// <summary>Extract a method body so validation can inspect one implementation boundary.</summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var end = FindMatchingBrace(source, brace);
            return end >= 0 ? source.Substring(brace, end - brace + 1) : string.Empty;
        }

        private static int FindMatchingBrace(string source, int brace)
        {
            var depth = 0;
            var inString = false;
            var inChar = false;
            var inLineComment = false;
            var inBlockComment = false;
            var verbatimString = false;
            for (var i = brace; i < source.Length; i++)
            {
                var ch = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (ch == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (ch == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (inString)
                {
                    if (verbatimString && ch == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (ch == '"' && (verbatimString || !IsEscaped(source, i)))
                    {
                        inString = false;
                        verbatimString = false;
                    }
                    continue;
                }

                if (inChar)
                {
                    if (ch == '\'' && !IsEscaped(source, i))
                        inChar = false;
                    continue;
                }

                if (ch == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    verbatimString = i > 0 && source[i - 1] == '@';
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = true;
                    continue;
                }

                if (ch == '{')
                    depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot() => CachedRepoRoot;

        private static string ResolveRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139D validation.");
            return root;
        }

        private static int ReserveFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static UnityReplayCursorEndpoint StartCursorEndpointWithRetry(
            string bearerToken,
            Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue,
            out int port)
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                port = ReserveFreeLoopbackPort();
                var endpoint = new UnityReplayCursorEndpoint();
                try
                {
                    endpoint.Start(
                        new UnityReplayCursorEndpointOptions(
                            enabled: true,
                            host: "127.0.0.1",
                            port: port,
                            path: "/v1/replay-cursor",
                            bearerToken: bearerToken,
                            maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes),
                        queue);
                    return endpoint;
                }
                catch (Exception ex) when (IsAddressAlreadyInUse(ex))
                {
                    endpoint.Dispose();
                    lastError = ex;
                }
            }

            port = 0;
            throw new InvalidOperationException(
                "Phase139D could not bind a loopback cursor endpoint after 5 attempts. Last error: "
                + lastError?.GetType().Name + ": " + lastError?.Message,
                lastError);
        }

        private static bool IsAddressAlreadyInUse(Exception error)
        {
            return error is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                   || error is HttpListenerException listener
                   && (listener.ErrorCode == 183 || listener.ErrorCode == 10_048);
        }

        private static HttpStatusCode PostJsonStatus(string url, string json, string bearerToken, out string body)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken);
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return response.StatusCode;
        }

        private static HttpStatusCode PreflightStatus(string url, string origin, out string allowOrigin)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Options, url);
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "Authorization, Content-Type");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            allowOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                ? string.Join(",", values)
                : string.Empty;
            return response.StatusCode;
        }

        private static string GetText(string url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
