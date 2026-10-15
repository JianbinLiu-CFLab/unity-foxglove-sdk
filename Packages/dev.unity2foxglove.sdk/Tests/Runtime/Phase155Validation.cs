// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 155 validation for additive FoxRun multi-sink fanout.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase155Validation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 155 Tests ---");
            _passCount = 0;

            VerifyByteOrientedSinkInterface();
            VerifyRouterIsAdditiveAndFaultIsolated();
            VerifyHubOwnsRouterAndFansOutAfterLivePublish();
            VerifyGeneratedSinkSideChannel();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 155: " + _passCount + " checks passed.\n");
        }

        private static void VerifyByteOrientedSinkInterface()
        {
            var sink = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/IFoxTopicSink.cs");

            Check(sink.Contains("public interface IFoxTopicSink : IDisposable", StringComparison.Ordinal)
                  && sink.Contains("void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)", StringComparison.Ordinal)
                  && sink.Contains("void Register(FoxTopicContract contract)", StringComparison.Ordinal)
                  && sink.Contains("enum FoxTopicSinkCapabilities", StringComparison.Ordinal),
                "155-1: sink boundary is byte-oriented so one serialized buffer is shared across sinks");
        }

        private static void VerifyRouterIsAdditiveAndFaultIsolated()
        {
            var router = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxTopicSinkRouter.cs");

            Check(router.Contains("public sealed class FoxTopicSinkRouter : IDisposable", StringComparison.Ordinal)
                  && router.Contains("if (contract.Visibility == FoxTopicVisibility.LocalOnly)", StringComparison.Ordinal)
                  && router.Contains("ReportFault(", StringComparison.Ordinal)
                  && router.Contains("\"publish\"", StringComparison.Ordinal)
                  && router.Contains("_reportedFaults.Add(key)", StringComparison.Ordinal),
                "155-2: router never exports LocalOnly topics and isolates per-sink faults with report-once dedup");

            Check(router.Contains("foreach (var contract in _contracts.Values)", StringComparison.Ordinal),
                "155-3: router replays known contracts so a sink can be attached at any time");

            Check(router.Contains("public bool Unregister(string topic)", StringComparison.Ordinal)
                  && router.Contains("_contracts.Remove(topic);", StringComparison.Ordinal)
                  && router.Contains("_wireContracts.Remove(topic);", StringComparison.Ordinal)
                  && router.Contains("_ownerCounts.Remove(topic);", StringComparison.Ordinal)
                  && router.Contains("lifecycle.Unregister(topic);", StringComparison.Ordinal),
                "155-4: router removes logical, wire, and ownership state before notifying lifecycle sinks");
        }

        private static void VerifyHubOwnsRouterAndFansOutAfterLivePublish()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");

            Check(hub.Contains("public FoxTopicSinkRouter TopicSinkRouter => _sinkRouter", StringComparison.Ordinal)
                  && hub.Contains("public static bool TryGetTopicSinkRouter(", StringComparison.Ordinal)
                  && hub.Contains("out FoxTopicSinkRouter router)", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.Register(", StringComparison.Ordinal)
                  && hub.Contains("ResolveWebSocketEncoding(info)", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.Dispose();", StringComparison.Ordinal),
                "155-5: hub owns the additive sink router, registers its frozen wire view, and disposes it on teardown");

            Check(LiveThenSink(hub),
                "155-6: live publish stays primary and the sink fanout runs afterward gated on HasSinks");

            Check(hub.Contains("_sinkRouter.Unregister(", StringComparison.Ordinal)
                  && hub.Contains("contract.Topic);", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.SinkFaulted += OnSinkFaulted", StringComparison.Ordinal)
                  && hub.Contains("Debug.LogException(fault.Exception)", StringComparison.Ordinal),
                "155-7: hub unregisters stale sink contracts and makes sink faults visible by default");
        }

        private static void VerifyGeneratedSinkSideChannel()
        {
            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var frame = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/ClassFrameEmitter.cs");

            Check(frame.Contains("IFoxgloveTopicSinkSource", StringComparison.Ordinal)
                  && emitter.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", StringComparison.Ordinal)
                  && emitter.Contains("if (router == null || !router.HasSinks)", StringComparison.Ordinal)
                  && emitter.Contains("router.PublishCompatible(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(", StringComparison.Ordinal)
                  && emitter.Contains("FoxRunEncoding.JSON", StringComparison.Ordinal)
                  && emitter.Contains("FoxRunEncoding.MessagePack", StringComparison.Ordinal)
                  && !emitter.Contains("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(", StringComparison.Ordinal),
                "155-8: generated sources fan compatible JSON or MessagePack views only, gated on HasSinks");

            Check(emitter.Contains("__foxRunLastJson_", StringComparison.Ordinal)
                  && emitter.Contains("var __sink_", StringComparison.Ordinal)
                  && !emitter.Contains("if (!IsAggregateTopic(fields))\r\n                    continue;", StringComparison.Ordinal)
                  && !emitter.Contains("if (!IsAggregateTopic(fields))\n                    continue;", StringComparison.Ordinal),
                "155-9: aggregate sink fanout reuses primary JSON bytes and legacy field topics also get sink payloads");
        }

        private static void VerifyValidationRegistryEntry()
        {
            Check(PhaseValidationRegistry.All.Any(item => item.Flag == "--phase155"),
                "155-10: validation registry exposes the multi-sink fanout flag");
        }

        private static bool LiveThenSink(string hub)
        {
            var dispatchStart = hub.IndexOf(
                "private bool TryPublish(",
                StringComparison.Ordinal);
            var dispatchEnd = hub.IndexOf(
                "private bool SelectsWebSocket(",
                dispatchStart,
                StringComparison.Ordinal);
            var live = hub.IndexOf(
                "source.FoxgloveLog_Publish(",
                dispatchStart,
                StringComparison.Ordinal);
            var gate = hub.IndexOf(
                "_sinkRouter.HasSinks",
                dispatchStart,
                StringComparison.Ordinal);
            var publish = hub.IndexOf(
                ".FoxgloveLog_PublishToSinks(",
                gate,
                StringComparison.Ordinal);
            return dispatchStart >= 0
                   && dispatchEnd > dispatchStart
                   && live >= dispatchStart
                   && gate > live
                   && publish > gate
                   && publish < dispatchEnd;
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
