// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139D validation for the Unity cursor bridge feasibility surface.

using System;
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
        private static int _passed;

        /// <summary>Runs all Phase 139D validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 139D: Unity Cursor Bridge Feasibility Scaffold ===");
            _passed = 0;

            VerifyExtensionScaffold();
            VerifyCursorRequestContract();
            VerifyCursorControllerContract();
            VerifyEndpointContract();
            VerifyEndpointLoopbackBehavior();
            VerifySmokeScript();
            VerifyWorkflowDocumentation();
            VerifyRuntimeWiring();
            VerifyValidationWiring();

            Console.WriteLine($"Phase 139D: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void VerifyCursorRequestContract()
        {
            var json = "{\"source\":\"foxglove-unity-cursor-bridge\",\"sequence\":7,\"mode\":\"seek\",\"time\":{\"sec\":12,\"nsec\":345}}";
            Check(ReplayCursorRequest.TryParseJson(json, out var request, out _)
                  && request.TimeNs == 12_000_000_345UL
                  && request.Sequence == 7
                  && request.Source == "foxglove-unity-cursor-bridge",
                "139D-2A: cursor request parses explicit split sec/nsec payload");

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
            Check(endpointSource.Contains("Access-Control-Allow-Origin", StringComparison.Ordinal)
                  && endpointSource.Contains("OPTIONS", StringComparison.Ordinal),
                "139D-4E: cursor endpoint supports browser CORS preflight");
            Check(endpointSource.Contains("ReplayCursorState", StringComparison.Ordinal)
                  && endpointSource.Contains("GET", StringComparison.Ordinal),
                "139D-4F: cursor endpoint exposes Unity replay state for Foxglove follow mode");

            var managerSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            Check(managerSource.Contains("Replay cursor bridge disabled", StringComparison.Ordinal)
                  && managerSource.Contains("SetExternalReplayCursorEnabled(false)", StringComparison.Ordinal),
                "139D-4G: cursor endpoint startup failure does not fail the main server");
        }

        private static void VerifyEndpointLoopbackBehavior()
        {
            var port = ReserveFreeLoopbackPort();
            var endpoint = new UnityReplayCursorEndpoint();
            ReplayCursorRequest received = default;
            try
            {
                endpoint.Start(
                    new UnityReplayCursorEndpointOptions(
                        enabled: true,
                        host: "127.0.0.1",
                        port: port,
                        path: "/v1/replay-cursor",
                        bearerToken: string.Empty,
                        maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes),
                    request =>
                    {
                        received = request;
                        return new UnityReplayCursorEndpointQueueResult(true, "accepted");
                    });

                var response = PostJson(
                    $"http://127.0.0.1:{port}/v1/replay-cursor",
                    "{\"source\":\"test\",\"sequence\":3,\"mode\":\"seek\",\"time\":{\"sec\":22,\"nsec\":33}}");

                Check(response.Contains("\"accepted\":true", StringComparison.Ordinal)
                      && received.TimeNs == 22_000_000_033UL,
                    "139D-4H: cursor endpoint accepts loopback POSTs into the runtime queue");

                var state = GetText($"http://127.0.0.1:{port}/v1/replay-cursor");
                Check(state.Contains("\"available\":", StringComparison.Ordinal)
                      && state.Contains("\"time\":", StringComparison.Ordinal)
                      && state.Contains("\"sec\":", StringComparison.Ordinal)
                      && state.Contains("\"nsec\":", StringComparison.Ordinal),
                    "139D-4I: cursor endpoint returns split-time Unity replay state");
            }
            finally
            {
                endpoint.Dispose();
            }
        }

        private static void VerifyExtensionScaffold()
        {
            var packageJson = Read("Tools/foxglove-extensions/unity-cursor-bridge/package.json");
            var source = Read("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var readme = Read("Tools/foxglove-extensions/unity-cursor-bridge/README.md");

            Check(packageJson.Contains("\"name\": \"unity-cursor-bridge\"", StringComparison.Ordinal)
                  && packageJson.Contains("foxglove-extension build", StringComparison.Ordinal),
                "139D-1A: extension package declares the Unity cursor bridge panel");
            Check(source.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && source.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-1B: extension watches and reads Foxglove currentTime");
            Check(source.Contains("context.watch(\"startTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"endTime\")", StringComparison.Ordinal)
                  && source.Contains("context.watch(\"didSeek\")", StringComparison.Ordinal),
                "139D-1C: extension watches timeline bounds and seek state");
            Check(source.Contains("sec: currentTime.sec", StringComparison.Ordinal)
                  && source.Contains("nsec: currentTime.nsec", StringComparison.Ordinal)
                  && source.Contains("fetch(endpoint", StringComparison.Ordinal),
                "139D-1D: extension sends split sec/nsec cursor metadata to loopback");
            Check(!source.Contains("/v1/data", StringComparison.Ordinal),
                "139D-1E: extension does not infer cursor state from Remote Data Loader ranges");
            Check(readme.Contains("disabled by default", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("/v1/data", StringComparison.Ordinal)
                  && readme.Contains("playhead signal", StringComparison.OrdinalIgnoreCase),
                "139D-1F: extension README documents the disabled default and DataLoader boundary");
            Check(source.Contains("Status:", StringComparison.Ordinal)
                  && source.Contains("Unity rejected sequence", StringComparison.Ordinal),
                "139D-1G: extension surfaces cursor forwarding status to the operator");
            Check(source.Contains("seekPlayback", StringComparison.Ordinal)
                  && source.Contains("Follow Unity replay", StringComparison.Ordinal),
                "139D-1H: extension can drive Foxglove from Unity replay state");
            Check(source.Contains("fetchUnityState", StringComparison.Ordinal)
                  && source.Contains("suppressForwardUntilMs", StringComparison.Ordinal),
                "139D-1I: extension polls Unity state without echoing follow-up seeks back to Unity");
        }

        private static void VerifySmokeScript()
        {
            var script = Read("Scripts/smoke/phase139d_unity_cursor_bridge_acceptance.py");

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
            Check(script.Contains("GET", StringComparison.Ordinal)
                  && script.Contains("unity_state", StringComparison.Ordinal),
                "139D-5E: smoke helper probes Unity cursor state for follow mode");
        }

        private static void VerifyWorkflowDocumentation()
        {
            var docs = Read("docs/research-remote-timeline-scene-reproduction.md");

            Check(docs.Contains("Phase139D Unity Cursor Bridge Boundary", StringComparison.Ordinal),
                "139D-6A: research document contains a Phase139D cursor bridge section");
            Check(docs.Contains("context.watch(\"currentTime\")", StringComparison.Ordinal)
                  && docs.Contains("renderState.currentTime", StringComparison.Ordinal),
                "139D-6B: documentation records the Foxglove extension currentTime contract");
            Check(docs.Contains("Do not infer Unity cursor state from `/v1/data`", StringComparison.Ordinal),
                "139D-6C: documentation forbids using Remote Data Loader data ranges as cursor signals");
            Check(docs.Contains("disabled by default", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("loopback", StringComparison.OrdinalIgnoreCase),
                "139D-6D: documentation keeps the bridge optional and loopback-bounded");
            Check(docs.Contains("not a product-ready bidirectional sync feature", StringComparison.OrdinalIgnoreCase)
                  && docs.Contains("Remote File Access only opens the MCAP data source", StringComparison.Ordinal),
                "139D-6E: documentation records the current product boundary");
        }

        private static void VerifyRuntimeWiring()
        {
            var runtime = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs");
            var coordinator = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/TickCoordinator.cs");
            var manager = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");

            Check(runtime.Contains("ExternalReplayCursorController", StringComparison.Ordinal)
                  && runtime.Contains("TryEnqueueExternalReplayCursor", StringComparison.Ordinal),
                "139D-7A: runtime owns an external cursor controller");
            Check(runtime.Contains("GetExternalReplayCursorState", StringComparison.Ordinal)
                  && runtime.Contains("GetPlaybackState", StringComparison.Ordinal),
                "139D-7B: runtime exposes replay cursor state for the endpoint");
            Check(coordinator.Contains("TryDrainLatest", StringComparison.Ordinal)
                  && coordinator.Contains("QueueReplaySceneSnapshot", StringComparison.Ordinal),
                "139D-7C: runtime tick drains external cursors into replay scene snapshots");
            Check(manager.Contains("_enableReplayCursorBridge", StringComparison.Ordinal)
                  && manager.Contains("false", StringComparison.Ordinal),
                "139D-7D: manager exposes a disabled-by-default cursor bridge setting");
            Check(server.Contains("StartReplayCursorEndpointIfNeeded", StringComparison.Ordinal)
                  && server.Contains("StopReplayCursorEndpoint", StringComparison.Ordinal),
                "139D-7E: manager starts and stops the cursor endpoint with server lifecycle");
            Check(server.Contains("RefreshReplayCursorEndpointIfNeeded", StringComparison.Ordinal)
                  && manager.Contains("RefreshReplayCursorEndpointIfNeeded", StringComparison.Ordinal),
                "139D-7F: manager refreshes the cursor endpoint when Inspector settings change during Play Mode");
            Check(!editor.Contains("Cursor Bridge (Advanced)", StringComparison.Ordinal)
                  && !editor.Contains("_enableReplayCursorBridge", StringComparison.Ordinal),
                "139D-7G: manager Inspector does not expose unfinished cursor bridge controls");
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

        private static string Read(string relativePath) => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
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

        private static string PostJson(string url, string json)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = client.PostAsync(url, content).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
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
