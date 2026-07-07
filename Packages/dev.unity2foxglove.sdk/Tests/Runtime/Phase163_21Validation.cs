// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-21 validation for FoxRun runtime bus, sinks, and inbound gates.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_21Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-21: FoxRun Runtime Bus, Sinks, and Inbound Gates ===");
            _passed = 0;

            TopicBusLifecycleAndDiagnosticsAreHardened();
            InboundJsonRejectsDeepPayloadsAndSupportsGeneratedPrimitiveTypes();
            SinkRouterRequiresRegisteredLiveContracts();
            ClientMessagesAreDeliveredOnManagerUpdate();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-21: {_passed} checks passed.");
        }

        private static void TopicBusLifecycleAndDiagnosticsAreHardened()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicBus.cs");

            Check(source.Contains("public bool Unsubscribe<T>", StringComparison.Ordinal)
                  && source.Contains("_subscriptions.Remove(topic)", StringComparison.Ordinal),
                "163-21A-1: FoxTopicBus exposes typed unsubscribe and removes empty topic lists");
            Check(source.Contains("PayloadType", StringComparison.Ordinal)
                  && source.Contains("incompatible subscriber type", StringComparison.Ordinal)
                  && source.Contains("new InvalidOperationException", StringComparison.Ordinal),
                "163-21A-2: FoxTopicBus reports type-mismatched subscribers instead of silently skipping them");
        }

        private static void InboundJsonRejectsDeepPayloadsAndSupportsGeneratedPrimitiveTypes()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInboundJson.cs");

            Check(source.Contains("MaxTypeHintScanDepth", StringComparison.Ordinal)
                  && source.Contains("payload nesting exceeds the maximum supported depth", StringComparison.Ordinal)
                  && source.Contains("ContainsForbiddenTypeHint(property.Value, depth + 1", StringComparison.Ordinal),
                "163-21B-1: FoxRun inbound $type scan is depth bounded");
            Check(source.Contains("out decimal value", StringComparison.Ordinal)
                  && source.Contains("out char value", StringComparison.Ordinal)
                  && source.Contains("must be a single character", StringComparison.Ordinal),
                "163-21B-2: FoxRun inbound decoder supports generated decimal and char inputs");
            Check(source.Contains("FoxRun inbound vector component", StringComparison.Ordinal)
                  && source.Contains("cannot be converted", StringComparison.Ordinal)
                  && source.Contains("catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException)", StringComparison.Ordinal),
                "163-21B-3: FoxRun vector numeric conversion failures are reported without escaping");
            Check(source.Contains("private static readonly JsonLoadSettings LoadSettings", StringComparison.Ordinal)
                  && source.Contains("JToken.Parse(json, LoadSettings)", StringComparison.Ordinal)
                  && source.Contains("intended for low-frequency FoxRun control inputs", StringComparison.Ordinal),
                "163-21B-4: FoxRun inbound decoder reuses load settings and documents per-call JSON allocations");
        }

        private static void SinkRouterRequiresRegisteredLiveContracts()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicSinkRouter.cs");
            var publish = PhaseValidationSourceHelpers.SourceMethod(source, "public void Publish");

            Check(publish.Contains("_contracts.TryGetValue(contract.Topic", StringComparison.Ordinal)
                  && publish.Contains("ReferenceEquals(registeredContract, contract)", StringComparison.Ordinal),
                "163-21C-1: FoxTopicSinkRouter ignores stale or never-registered contract references");
        }

        private static void ClientMessagesAreDeliveredOnManagerUpdate()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var clientEvents = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var update = PhaseValidationSourceHelpers.SourceMethod(manager, "private void Update");
            var drain = PhaseValidationSourceHelpers.SourceMethod(clientEvents, "private void DrainClientEventQueue");

            Check(server.Contains("EnqueueClientMessageEvent(ClientEvent.Message", StringComparison.Ordinal)
                  && update.Contains("DrainClientEventQueue(_clientMessageEvents)", StringComparison.Ordinal)
                  && drain.Contains("OnClientMessage?.Invoke", StringComparison.Ordinal),
                "163-21D-1: Foxglove client messages are queued by transport callbacks and invoked from manager Update");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_21Validation.cs", StringComparison.Ordinal),
                "163-21E-1: runtime test project compiles Phase163_21Validation");
            Check(registry.Contains("--phase163-21", StringComparison.Ordinal)
                  && registry.Contains("Phase163_21Validation.Validate", StringComparison.Ordinal),
                "163-21E-2: validation registry exposes --phase163-21");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
