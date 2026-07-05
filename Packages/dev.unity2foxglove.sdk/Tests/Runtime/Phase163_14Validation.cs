// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-14 validation for publisher base, cadence, and output policy review fixes.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_14Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-14: Publisher Base, Cadence, and Output Policy ===");
            _passed = 0;

            PublisherBaseDefaultsAndWarningsAreStable();
            Ros2SchemaValidationIsNonThrowingForPublishPaths();
            SystemInfoPublisherClampsEffectiveRateOnlyAtRuntime();
            ChannelLifecycleContractIsSessionScoped();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-14: {_passed} checks passed.");
        }

        private static void PublisherBaseDefaultsAndWarningsAreStable()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");

            Check(source.Contains("_publishRateSource = PublisherRateSource.UseManagerDefault", StringComparison.Ordinal),
                "163-14A-1: code-added publishers default to manager publish rate like Inspector-added publishers");
            Check(source.Contains("When true, this publisher sends data on each scheduled tick", StringComparison.Ordinal),
                "163-14A-2: publish-on-enable Inspector tooltip describes runtime gate semantics");
            Check(source.Contains("var managerDefault = _manager != null ? _manager.DefaultPublisherEncoding : GlobalEncoding.Json", StringComparison.Ordinal),
                "163-14A-3: publishers without a manager resolve JSON without protobuf fallback spam");
            Check(source.Contains("FindAnyObjectByType<FoxgloveManager>()", StringComparison.Ordinal)
                  && !source.Contains("manager = FindFirstObjectByType<FoxgloveManager>();", StringComparison.Ordinal),
                "163-14A-4: Edit Mode effective-rate lookup uses unordered manager search");
            Check(source.Contains("_lastEncodingFallbackWarningKey", StringComparison.Ordinal)
                  && source.Contains("_lastEncodingMismatchWarningKey", StringComparison.Ordinal)
                  && source.Contains("_lastPublishTopicWarningKey", StringComparison.Ordinal)
                  && source.Contains("_lastRos2BridgeTopicWarningKey", StringComparison.Ordinal),
                "163-14A-5: warning dedupe state is split by warning category");
            Check(source.Contains("fresh cadence window", StringComparison.Ordinal),
                "163-14A-6: OnEnable documents immediate first scheduled publish behavior");
        }

        private static void Ros2SchemaValidationIsNonThrowingForPublishPaths()
        {
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var warningState = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/WarningDebounceState.cs");

            Check(manager.Contains("TryGetOrRegisterRos2MsgSchemaChannel", StringComparison.Ordinal)
                  && manager.Contains("TryValidateRos2SchemaName", StringComparison.Ordinal)
                  && manager.Contains("WarnInvalidRos2Schema", StringComparison.Ordinal),
                "163-14B-1: ROS2 publisher paths have non-throwing schema validation helpers");
            Check(MethodContains(manager, "public bool TryPrepareRos2Publish", "TryGetOrRegisterRos2MsgSchemaChannel(topic, schemaName, out channelId"),
                "163-14B-2: ROS2 prepare returns false instead of throwing for bad schemas");
            Check(MethodContains(manager, "public void PublishRos2(string topic", "TryGetOrRegisterRos2MsgSchemaChannel(topic, schemaName, out var channelId"),
                "163-14B-3: direct ROS2 publish returns after warning instead of throwing for bad schemas");
            Check(warningState.Contains("LastInvalidRos2SchemaWarningKey", StringComparison.Ordinal)
                  && manager.Contains("_warningDebounceState.LastInvalidRos2SchemaWarningKey", StringComparison.Ordinal),
                "163-14B-4: invalid ROS2 schema warnings are deduplicated independently");
        }

        private static void SystemInfoPublisherClampsEffectiveRateOnlyAtRuntime()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxgloveSystemInfoPublisher.cs");
            var validation = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/SystemInfoPublisherValidation.cs");

            Check(source.Contains("private const float MaxPublishRateHz = 5f;", StringComparison.Ordinal),
                "163-14C-1: SystemInfo names the 5 Hz product cap");
            Check(source.Contains("Mathf.Min(EffectivePublishRateHz, MaxPublishRateHz)", StringComparison.Ordinal),
                "163-14C-2: SystemInfo applies the rate cap to the effective scheduler rate");
            Check(source.Contains("ApplySystemInfoDefaults(clampSerializedRate: false)", StringComparison.Ordinal)
                  && source.Contains("ApplySystemInfoDefaults(clampSerializedRate: true)", StringComparison.Ordinal),
                "163-14C-3: SystemInfo separates runtime defaults from Edit Mode serialized clamping");
            Check(source.Contains("if (clampSerializedRate)", StringComparison.Ordinal),
                "163-14C-4: SystemInfo only mutates the max rate field when Edit Mode validation requests it");
            Check(source.Contains("dedicated cadence state", StringComparison.Ordinal),
                "163-14C-5: SystemInfo documents why it owns a separate rate state");
            Check(validation.Contains("private const float MaxPublishRateHz = 5f;", StringComparison.Ordinal),
                "163-14C-6: Phase145 validation checks the named max-rate constant instead of any 5f literal");
        }

        private static void ChannelLifecycleContractIsSessionScoped()
        {
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var manager = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var server = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");

            Check(publisher.Contains("protected virtual void OnDisable() { }", StringComparison.Ordinal),
                "163-14D-1: publisher disable does not unadvertise shared session channels without ref counts");
            Check(manager.Contains("_channelCache[key] = id;", StringComparison.Ordinal)
                  && manager.Contains("var key = (topic, schemaName, CdrEncoding, Ros2MsgSchemaEncoding);", StringComparison.Ordinal),
                "163-14D-2: channel cache remains keyed by full topic/schema/encoding identity");
            Check(server.Contains("_channelCache.Clear();", StringComparison.Ordinal),
                "163-14D-3: channel cache lifecycle remains scoped to StopServer session teardown");
        }

        private static void PhaseWiringIsPresent()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("Phase163_14Validation.cs", StringComparison.Ordinal),
                "163-14E-1: runtime test project compiles Phase163_14Validation");
            Check(registry.Contains("--phase163-14", StringComparison.Ordinal)
                  && registry.Contains("Phase163_14Validation.Validate", StringComparison.Ordinal),
                "163-14E-2: validation registry exposes --phase163-14");
        }

        private static bool MethodContains(string source, string signature, string needle)
            => ExtractMethod(source, signature).Contains(needle, StringComparison.Ordinal);

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Check(start >= 0, "Phase 163-14 validation helper found method: " + signature);
            return ExtractBlock(source, start);
        }

        private static string ExtractBlock(string source, int start)
        {
            var brace = source.IndexOf('{', start);
            Check(brace >= 0, "Phase 163-14 validation helper found opening brace");

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unable to extract source block.");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
