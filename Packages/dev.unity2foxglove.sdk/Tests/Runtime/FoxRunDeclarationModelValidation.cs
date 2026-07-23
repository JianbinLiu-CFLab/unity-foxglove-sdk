// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase183-A behavior and structural evidence for the FoxRun declaration reset.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunDeclarationModelValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Declaration Model Tests ---");
            _passed = 0;

            VerifyFreshDeclarationDefaults();
            VerifyDirectionAwareUpdatePolicies();
            VerifyFrozenSubscriptionFrequencies();
            VerifyLatestWinsInputRouting();
            VerifyGeneratedFullDuplexStructure();
            VerifyLegacySurfaceAndCoreBoundary();
            VerifyRegistryEntry();

            Console.WriteLine("FoxRun declaration model: " + _passed + " checks passed.\n");
        }

        private static void VerifyFreshDeclarationDefaults()
        {
            var field = new FoxRunAttribute("/phase183/default");
            var aggregate = new FoxRunMessageAttribute("/phase183/aggregate");

            Check(field.Mode == FoxRunFlow.Publish
                  && field.Policy == FoxRunPolicy.FixedRate
                  && field.Hz < 0f
                  && aggregate.Policy == FoxRunPolicy.FixedRate
                  && aggregate.Hz < 0f,
                "Behavior 183A-1: field and aggregate declarations use Publish/FixedRate with an explicit unspecified-rate sentinel");
            Check(Enum.GetValues(typeof(FoxRunFlow)).Cast<int>().SequenceEqual(new[] { 1, 2, 3 })
                  && Enum.GetValues(typeof(FoxRunPolicy)).Cast<int>().SequenceEqual(new[] { 1, 2, 4 }),
                "Behavior 183A-2: all three flows and three policies use fresh non-zero values while retired value 3 stays invalid");
        }

        private static void VerifyDirectionAwareUpdatePolicies()
        {
            Check(FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.FixedRate, 1d, true, false, 0d, 0d)
                  && FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.FixedRate, true, true, false, 1d, 0d, 0d)
                  && !FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.FixedRate, false, true, false, 2d, 1d, 0d),
                "Behavior 183A-3: FixedRate crosses each boundary only when the caller has a current eligible value");
            Check(FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.Change, 1d, false, false, 0d, 0d)
                  && FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.Change, true, true, true, 2d, 1d, 0d)
                  && !FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.Change, true, true, false, 2d, 1d, 0d),
                "Behavior 183A-4: Change accepts first or changed values and suppresses fresh duplicates");
            Check(FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.Change, true, true, false, 3d, 1d, 2d)
                  && !FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.Change, false, true, false, 4d, 1d, 2d),
                "Behavior 183A-5: Change with Hz requires a newly received duplicate and never invents a stale heartbeat");
            Check(!FoxRunUpdatePolicy.ShouldPublish(FoxRunPolicy.Trigger, 1d, false, true, 0d, 0d)
                  && !FoxRunUpdatePolicy.ShouldApply(FoxRunPolicy.Trigger, true, false, true, 1d, 0d, 0d),
                "Behavior 183A-6: Trigger blocks automatic publication and application");
        }

        private static void VerifyFrozenSubscriptionFrequencies()
        {
            var state = new FoxRunSubscriptionSessionState();
            var policy = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunWireEncoding.Protobuf,
                FoxRunRos2QosPreset.Default,
                4 * 1024 * 1024,
                transportAdmissionRateLimitHz: 120,
                defaultSubscribeRateHz: 30);
            var frozen = state.BeginIfNeeded(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunWireEncoding.Json,
                FoxRunRos2QosPreset.SensorData,
                1,
                transportAdmissionRateLimitHz: 1,
                defaultSubscribeRateHz: 1);

            Check(policy.TransportAdmissionRateLimitHz == 120
                  && policy.DefaultSubscribeRateHz == 30
                  && ReferenceEquals(policy, frozen),
                "Behavior 183A-7: maximum and default subscription rates are distinct and frozen for one session");
        }

        private static void VerifyLatestWinsInputRouting()
        {
            var source = new LatestWinsInputSource();
            var router = new FoxRunInputRouter(maxPayloadBytes: 16, maxMessagesPerSecondPerTopic: 60);
            router.Register(source);

            var first = router.Dispatch("/phase183/latest", new byte[] { 1 }, "json", 1d);
            var second = router.Dispatch("/phase183/latest", new byte[] { 2 }, "json", 1.01d);
            var applied = router.Flush(2d, inheritedSubscribeRateHz: 30);

            Check(first.Status == FoxRunInputDispatchStatus.Staged
                  && second.Status == FoxRunInputDispatchStatus.Staged
                  && applied == 1
                  && source.AppliedValue == 2
                  && router.Flush(3d, inheritedSubscribeRateHz: 30) == 0,
                "Behavior 183A-8: accepted input is bounded latest-wins and a later flush cannot replay stale state");
        }

        private static void VerifyGeneratedFullDuplexStructure()
        {
            var input = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/InputDispatchEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var native = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/Ros2InputDispatchEmitter.cs");
            var trigger = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TriggerEmitter.cs");

            Check(input.Contains("member.HasExplicitHz", StringComparison.Ordinal)
                  && input.Contains("inheritedSubscribeRateHz", StringComparison.Ordinal)
                  && trigger.Contains("var baseName = \"FoxRun_Apply_\"", StringComparison.Ordinal)
                  && input.Contains("__foxRunSuppressNextPublish_", StringComparison.Ordinal),
                "Structural 183A-9: generated WebSocket input inherits or overrides subscription rate, exposes Trigger apply, and marks remote-echo suppression");
            Check(publish.Contains("fields.Any(field => field.Mode == 3)", StringComparison.Ordinal)
                  && publish.Contains("__foxRunSuppressNextPublish_", StringComparison.Ordinal)
                  && native.Contains("member.HasExplicitHz", StringComparison.Ordinal)
                  && native.Contains("member.Hz", StringComparison.Ordinal),
                "Structural 183A-10: PublishAndSubscribe generates both independently scheduled directions with one-shot echo suppression");
        }

        private static void VerifyLegacySurfaceAndCoreBoundary()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var declaredProperties = typeof(FoxRunAttribute)
                .GetProperties()
                .Where(property => property.DeclaringType == typeof(FoxRunAttribute))
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedProperties = new[]
            {
                "Encoding",
                "Hz",
                "Mode",
                "OnlyIf",
                "Policy",
                "ProtobufFieldNumber",
                "Ros2Qos",
                "SchemaName",
                "SubscriptionProvider",
                "Tolerance",
                "Topic",
            };
            Check(declaredProperties.SequenceEqual(expectedProperties, StringComparer.Ordinal)
                  && typeof(FoxRunAttribute).GetProperty("Mode")?.PropertyType == typeof(FoxRunFlow)
                  && typeof(FoxRunAttribute).GetProperty("Policy")?.PropertyType == typeof(FoxRunPolicy),
                "Structural 183A-11: the public attribute surface exposes only the fresh flow and policy model");
            Check(!assembly.GetReferencedAssemblies().Any(reference =>
                    reference.Name.IndexOf("Ros2ForUnity", StringComparison.OrdinalIgnoreCase) >= 0
                    || reference.Name.Equals("ros2cs_common", StringComparison.OrdinalIgnoreCase)),
                "Structural 183A-12: the core declaration and policy assembly remains ROS-free");
        }

        private static void VerifyRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item =>
                    item.Flag == "--phase183a"
                    && item.Name == "FoxRun declaration and full-duplex update policy"
                    && item.Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Structural)),
                "Structural 183A-13: registry classifies behavior and source-shape evidence without overstating either");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }

        private sealed class LatestWinsInputSource : IFoxgloveInputSource
        {
            private bool _hasPending;
            private int _pendingValue;

            public int AppliedValue { get; private set; }
            public int FoxgloveInput_TopicCount => 1;

            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index)
                => new(
                    "/phase183/latest",
                    FoxRunWireEncoding.Json,
                    FoxRunFlow.Subscribe,
                    FoxRunSubscriptionProvider.FoxgloveWebSocket,
                    supportsWebSocket: true,
                    supportsRos2Native: false);

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                _pendingValue = payload[0];
                _hasPending = true;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
            {
                if (!_hasPending)
                    return 0;
                AppliedValue = _pendingValue;
                _hasPending = false;
                return 1;
            }
        }
    }
}
