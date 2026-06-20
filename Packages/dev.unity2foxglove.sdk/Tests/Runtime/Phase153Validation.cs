// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 153 validation for FoxRun topic contracts and local bus boundaries.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase153Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 153 Tests ---");
            _passCount = 0;

            VerifyRuntimeContractAndEnvelopeShape();
            VerifyTypedBusBehaviorShape();
            VerifyHubUsesBusAsSideChannel();
            VerifyGeneratedSourceSurface();
            VerifyValidationCoverage();

            Console.WriteLine("Phase 153: " + _passCount + " checks passed.\n");
        }

        private static void VerifyRuntimeContractAndEnvelopeShape()
        {
            var contract = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicContract.cs");
            var envelope = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicEnvelope.cs");

            Check(contract.Contains("public sealed class FoxTopicContract", StringComparison.Ordinal)
                  && contract.Contains("public enum FoxTopicVisibility", StringComparison.Ordinal)
                  && contract.Contains("public enum FoxTopicWriterPolicy", StringComparison.Ordinal)
                  && contract.Contains("SingleWriter = 0", StringComparison.Ordinal)
                  && contract.Contains("throw new ArgumentException", StringComparison.Ordinal)
                  && contract.Contains("Encoding = string.IsNullOrWhiteSpace(encoding) ? \"json\"", StringComparison.Ordinal),
                "FoxTopicContract exposes stable topic identity with single-writer policy and json default");

            Check(envelope.Contains("public readonly struct FoxTopicEnvelope<T>", StringComparison.Ordinal)
                  && envelope.Contains("public T Payload { get; }", StringComparison.Ordinal)
                  && !envelope.Contains("object Payload", StringComparison.Ordinal),
                "FoxTopicEnvelope is generic and does not carry object payloads");
        }

        private static void VerifyTypedBusBehaviorShape()
        {
            var bus = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicBus.cs");
            var tests = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxTopicBusTests.cs");

            Check(bus.Contains("public sealed class FoxTopicBus", StringComparison.Ordinal)
                  && bus.Contains("Subscribe<T>(string topic, Action<FoxTopicEnvelope<T>> callback)", StringComparison.Ordinal)
                  && bus.Contains("Publish<T>(FoxTopicContract contract, ulong timestampNs, in T payload, string origin)", StringComparison.Ordinal)
                  && bus.Contains("public bool HasSubscribers(string topic)", StringComparison.Ordinal)
                  && bus.Contains("public bool Unregister(string topic, string origin)", StringComparison.Ordinal)
                  && bus.Contains("SubscriberFaulted", StringComparison.Ordinal)
                  && bus.Contains("already has a single writer", StringComparison.Ordinal)
                  && !bus.Contains("(object)envelope.Payload", StringComparison.Ordinal),
                "FoxTopicBus provides typed publish/subscribe, unregister, subscriber checks, fault events, and no payload boxing cast");

            Check(tests.Contains("BusDispatchesTypedPayloadWithoutObjectEnvelope", StringComparison.Ordinal)
                  && tests.Contains("SingleWriterRejectsSecondOriginAndKeepsFirstActive", StringComparison.Ordinal)
                  && tests.Contains("UnregisterReleasesSingleWriterTopicForReplacementOrigin", StringComparison.Ordinal)
                  && tests.Contains("SubscriberExceptionIsBoundedAndDoesNotStopRemainingSubscribers", StringComparison.Ordinal),
                "xUnit coverage locks typed dispatch, writer policy, unregister lifecycle, and bounded subscriber fault isolation");
        }

        private static void VerifyHubUsesBusAsSideChannel()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");

            Check(hub.Contains("public interface IFoxgloveTopicContractSource", StringComparison.Ordinal)
                  && hub.Contains("public interface IFoxgloveTopicBusSource", StringComparison.Ordinal)
                  && hub.Contains("private readonly FoxTopicBus _topicBus = new();", StringComparison.Ordinal)
                  && hub.Contains("public FoxTopicBus TopicBus => _topicBus;", StringComparison.Ordinal)
                  && hub.Contains("RegisterSourceContracts(source, count)", StringComparison.Ordinal)
                  && hub.Contains("UnregisterSourceContracts(source, timers.Length)", StringComparison.Ordinal)
                  && hub.Contains("RemoveSourceNow(source)", StringComparison.Ordinal),
                "FoxgloveLogHub owns a local bus and registers/unregisters optional generated contracts");

            Check(MethodContainsLiveThenBus(hub, "TryPublishScheduledTopic", "scheduled publish")
                  && MethodContainsLiveThenBus(hub, "TryPublishTriggeredTopic", "trigger publish"),
                "FoxRun live publish remains the primary path and the bus runs afterward as a side-channel");

            Check(hub.Contains("operation + \" bus side-channel\"", StringComparison.Ordinal)
                  && hub.Contains("catch (Exception ex) when (IsRecoverableSourceException(ex))", StringComparison.Ordinal),
                "Bus side-channel failures are isolated from live publish");
        }

        private static void VerifyGeneratedSourceSurface()
        {
            var classFrame = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/ClassFrameEmitter.cs");
            var metadata = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var publish = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
            var golden = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture_FoxRun.golden.cs");

            Check(classFrame.Contains("IFoxgloveTopicContractSource", StringComparison.Ordinal)
                  && classFrame.Contains("IFoxgloveTopicBusSource", StringComparison.Ordinal)
                  && emitter.Contains("TopicMetadataEmitter.EmitGetContract", StringComparison.Ordinal)
                  && emitter.Contains("PublishDispatchEmitter.EmitPublishToBus", StringComparison.Ordinal),
                "Generated FoxRun classes implement contract and local bus side-channel interfaces");

            Check(metadata.Contains("Sha256Hex(canonical)", StringComparison.Ordinal)
                  && metadata.Contains("FoxRunCanonicalTypeNormalizer.NormalizeTypeName", StringComparison.Ordinal)
                  && metadata.Contains("FoxTopicVisibility.Exported", StringComparison.Ordinal)
                  && metadata.Contains("FoxTopicWriterPolicy.SingleWriter", StringComparison.Ordinal),
                "Generated contracts embed canonical topic shape fingerprints");

            Check(publish.Contains("bus.HasSubscribers", StringComparison.Ordinal)
                  && publish.Contains("bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract", StringComparison.Ordinal)
                  && publish.Contains("new Dictionary<string, object>", StringComparison.Ordinal),
                "Generated bus side-channel gates payload construction behind subscriber demand");

            Check(golden.Contains("IFoxgloveTopicContractSource", StringComparison.Ordinal)
                  && golden.Contains("FoxgloveLog_PublishToBus", StringComparison.Ordinal)
                  && golden.Contains("bus.HasSubscribers(\"/debug/value\")", StringComparison.Ordinal)
                  && golden.Contains("topic=/debug/value\\nencoding=json\\nschema=\\nfields=value:float32;valueMirror:float32", StringComparison.Ordinal),
                "Roslyn golden baseline includes generated contracts and bus side-channel output");
        }

        private static void VerifyValidationCoverage()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase153"),
                "Validation registry exposes the Phase153 flag");
        }

        private static bool MethodContainsLiveThenBus(string source, string methodName, string operation)
        {
            var methodStart = source.IndexOf(methodName, StringComparison.Ordinal);
            if (methodStart < 0)
                return false;

            var live = source.IndexOf("source.FoxgloveLog_Publish(topicIndex, _mgr, nowNs);", methodStart, StringComparison.Ordinal);
            var bus = source.IndexOf("PublishTopicBusSideChannel(source, topicIndex, nowNs, \"" + operation + "\")", methodStart, StringComparison.Ordinal);
            return live >= 0 && bus > live;
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
            _passCount++;
        }
    }
}
