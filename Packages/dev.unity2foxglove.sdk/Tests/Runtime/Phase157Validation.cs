// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Repository boundary checks for FoxRun inbound and local services.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase157Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 157 Tests ---");
            _passCount = 0;

            VerifyOptionalPackagePresent();
            VerifyGeneratedInputSurface();
            VerifyMainThreadAndSecurityBoundary();
            VerifyExistingServiceHubExtension();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 157: " + _passCount + " checks passed.\n");
        }

        private static void VerifyOptionalPackagePresent()
        {
            Check(Directory.Exists(RepoPath("Packages/dev.unity2foxglove.ros2forunity")),
                "157-0: optional R2FU adapter package is present for Phase157 validation");
        }

        private static void VerifyGeneratedInputSurface()
        {
            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputSource.cs");
            var decoder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInboundJson.cs");

            Check(input.Contains("interface IFoxgloveInputSource", StringComparison.Ordinal)
                  && emitter.Contains("FoxgloveInput_TryApply", StringComparison.Ordinal),
                "generated inbound members use a dedicated typed input interface");
            Check(decoder.Contains("ContainsForbiddenTypeHint", StringComparison.Ordinal)
                  && decoder.Contains("forbidden $type hint", StringComparison.Ordinal)
                  && !decoder.Contains("TypeNameHandling.Auto", StringComparison.Ordinal),
                "inbound JSON rejects polymorphic type hints and never enables runtime-selected types");
        }

        private static void VerifyMainThreadAndSecurityBoundary()
        {
            var clientEvents = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var subscriptionSession = ReadRepoText(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var authorization = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInboundAuthorization.cs");

            Check(clientEvents.Contains("BoundedEventQueue<ClientEvent>", StringComparison.Ordinal)
                  && clientEvents.Contains("OnClientMessage?.Invoke", StringComparison.Ordinal)
                  && hub.Contains("_manager.OnClientMessageWithEncoding += OnClientMessage", StringComparison.Ordinal),
                "transport messages cross the existing bounded manager queue before input assignment");
            Check(authorization.Contains("IsLoopbackHost", StringComparison.Ordinal)
                  && authorization.Contains("remote inbound requires a configured shared token", StringComparison.Ordinal),
                "non-loopback inbound fails closed without explicit token-backed authorization");
            Check(hub.Contains(
                      "_router.MaxPayloadBytes = _manager.FoxRunSubscriptionMaxPayloadBytes;",
                      StringComparison.Ordinal)
                  && subscriptionSession.Contains(
                      "ConfiguredFoxRunSubscriptionMaxMessagesPerSecondPerTopic",
                      StringComparison.Ordinal)
                  && hub.Contains(
                      "_router.MaxMessagesPerSecondPerTopic = policy.MainThreadApplyRateLimitHz;",
                      StringComparison.Ordinal),
                "input dispatch receives manager-owned live payload and session-frozen per-topic rate limits");
        }

        private static void VerifyExistingServiceHubExtension()
        {
            var hub = PhaseValidationSourceHelpers.ReadFoxgloveServiceHubSources();
            var local = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveLocalServiceCall.cs");

            Check(hub.Contains("FoxgloveLocalServiceCallResult CallLocal", StringComparison.Ordinal)
                  && hub.Contains("_ownersByServiceName", StringComparison.Ordinal)
                  && hub.Contains("_descriptorsBySource", StringComparison.Ordinal),
                "local service calls extend the existing generated FoxService registry");
            Check(local.Contains("HandlerFailed", StringComparison.Ordinal)
                  && local.Contains("TimedOut", StringComparison.Ordinal)
                  && local.Contains("MissingService", StringComparison.Ordinal),
                "local service calls expose deterministic missing, failure, and timeout results");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase157"),
                "validation registry exposes the FoxRun inbound and service flag");
        }

        private static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }
    }
}
