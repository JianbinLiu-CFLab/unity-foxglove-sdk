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
                  && router.Contains("ReportFault(sink, contract.Topic, \"publish\", ex)", StringComparison.Ordinal)
                  && router.Contains("_reportedFaults.Add(key)", StringComparison.Ordinal),
                "155-2: router never exports LocalOnly topics and isolates per-sink faults with report-once dedup");

            Check(router.Contains("foreach (var contract in _contracts.Values)", StringComparison.Ordinal),
                "155-3: router replays known contracts so a sink can be attached at any time");

            Check(router.Contains("public bool Unregister(string topic)", StringComparison.Ordinal)
                  && router.Contains("return _contracts.Remove(topic)", StringComparison.Ordinal),
                "155-4: router removes stale source contracts so late sinks do not replay destroyed topics");
        }

        private static void VerifyHubOwnsRouterAndFansOutAfterLivePublish()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");

            Check(hub.Contains("public FoxTopicSinkRouter TopicSinkRouter => _sinkRouter", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.Register(contract)", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.Dispose()", StringComparison.Ordinal),
                "155-5: hub owns the sink router, registers exported contracts, and disposes it on teardown");

            Check(LiveThenSink(hub),
                "155-6: live publish stays primary and the sink fanout runs afterward gated on HasSinks");

            Check(hub.Contains("_sinkRouter.Unregister(contract.Topic)", StringComparison.Ordinal)
                  && hub.Contains("_sinkRouter.SinkFaulted += OnSinkFaulted", StringComparison.Ordinal)
                  && hub.Contains("Debug.LogWarning(message)", StringComparison.Ordinal),
                "155-7: hub unregisters stale sink contracts and makes sink faults visible by default");
        }

        private static void VerifyGeneratedSinkSideChannel()
        {
            var emitter = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var frame = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/ClassFrameEmitter.cs");

            Check(frame.Contains("IFoxgloveTopicSinkSource", StringComparison.Ordinal)
                  && emitter.Contains("void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)", StringComparison.Ordinal)
                  && emitter.Contains("if (router == null || !router.HasSinks)", StringComparison.Ordinal)
                  && emitter.Contains("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(", StringComparison.Ordinal),
                "155-8: generated sources fan topics out to the sink router, gated on HasSinks");

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
            var live = hub.IndexOf("source.FoxgloveLog_Publish(topicIndex, _mgr, nowNs);", StringComparison.Ordinal);
            var sink = hub.IndexOf("FoxgloveLog_PublishToSinks(topicIndex, _sinkRouter, nowNs)", StringComparison.Ordinal);
            var gate = hub.IndexOf("_sinkRouter.HasSinks && source is IFoxgloveTopicSinkSource", StringComparison.Ordinal);
            return live >= 0 && sink > live && gate >= 0 && gate < sink;
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
