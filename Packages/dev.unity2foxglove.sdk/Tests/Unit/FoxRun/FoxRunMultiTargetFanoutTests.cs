// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunMultiTargetFanoutTests
    {
        [Fact]
        public void ExplicitTargetsReplaceTheFrozenProfileWithoutFallback()
        {
            var info = Topic(
                declaredTargets: FoxRunEndpoint.Ros2Bridge,
                hasExplicitTargets: true);

            var result = FoxRunResolvedPublishContract.TryResolve(
                info,
                defaultTargets: FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                nativeDefaultQos: FoxRunResolvedQos.SensorData,
                bridgeDefaultQos: FoxRunResolvedQos.SystemDefault,
                defaultSource: FoxRunEndpoint.Foxglove,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                out var contract,
                out var diagnostic);

            Assert.True(result, diagnostic);
            Assert.Equal(FoxRunEndpoint.Ros2Bridge, contract.Targets);
            Assert.False(contract.Selects(FoxRunEndpoint.Foxglove));
            Assert.False(contract.Selects(FoxRunEndpoint.Ros2Native));
            Assert.True(contract.Selects(FoxRunEndpoint.Ros2Bridge));
            Assert.Equal((FoxRunEncoding)0, contract.FoxgloveEncoding);
            Assert.Equal(FoxRunResolvedQos.SystemDefault, contract.BridgeQos);
        }

        [Fact]
        public void ExplicitQosIsResolvedIndependentlyAgainstEachRos2TargetDefault()
        {
            var info = Topic(
                declaredTargets: FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                hasExplicitTargets: true,
                qosReliability: FoxRunQosReliability.BestEffort,
                hasExplicitReliability: true);

            Assert.True(FoxRunResolvedPublishContract.TryResolve(
                info,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.JSON,
                nativeDefaultQos: FoxRunResolvedQos.Default,
                bridgeDefaultQos: FoxRunResolvedQos.SystemDefault,
                defaultSource: FoxRunEndpoint.Foxglove,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                out var contract,
                out var diagnostic), diagnostic);

            Assert.Equal(FoxRunQosReliability.BestEffort, contract.NativeQos.Reliability);
            Assert.Equal(FoxRunQosHistory.KeepLast, contract.NativeQos.History);
            Assert.Equal(10, contract.NativeQos.Depth);
            Assert.Equal(FoxRunQosReliability.BestEffort, contract.BridgeQos.Reliability);
            Assert.Equal(FoxRunQosHistory.SystemDefault, contract.BridgeQos.History);
            Assert.Equal(0, contract.BridgeQos.Depth);
        }

        [Fact]
        public void OneCaptureAndTimestampAreSharedWhileFailingTargetIsIsolated()
        {
            var contract = Resolved(
                FoxRunEndpoint.Foxglove
                | FoxRunEndpoint.Ros2Native
                | FoxRunEndpoint.Ros2Bridge);
            var captureCount = 0;
            var deliveries = new List<(FoxRunEndpoint Target, object Sample, ulong Timestamp)>();

            var result = FoxRunPublishFanout.Dispatch(
                contract,
                timestampNs: 99UL,
                capture: () =>
                {
                    captureCount++;
                    return new Sample(7);
                },
                isReady: target => target != FoxRunEndpoint.Ros2Bridge,
                publish: (target, sample, timestamp) =>
                {
                    deliveries.Add((target, sample, timestamp));
                    return target != FoxRunEndpoint.Ros2Native;
                });

            Assert.Equal(1, captureCount);
            Assert.Equal(FoxRunPublishTargetStatus.Degraded, result.Status);
            Assert.Equal(FoxRunEndpoint.Foxglove, result.SucceededTargets);
            Assert.Equal(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                result.FailedTargets);
            Assert.Equal(2, deliveries.Count);
            Assert.Same(deliveries[0].Sample, deliveries[1].Sample);
            Assert.All(deliveries, delivery => Assert.Equal(99UL, delivery.Timestamp));
        }

        [Fact]
        public void AllUnavailableTargetsDoNotCaptureAndSurfaceUnavailable()
        {
            var captures = 0;
            var publishes = 0;

            var result = FoxRunPublishFanout.Dispatch(
                Resolved(FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge),
                timestampNs: 10UL,
                capture: () =>
                {
                    captures++;
                    return new Sample(1);
                },
                isReady: _ => false,
                publish: (_, __, ___) =>
                {
                    publishes++;
                    return true;
                });

            Assert.Equal(FoxRunPublishTargetStatus.Unavailable, result.Status);
            Assert.Equal(0, captures);
            Assert.Equal(0, publishes);
            Assert.Equal(
                FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                result.FailedTargets);
        }

        [Fact]
        public void PublishExceptionDoesNotBlockLaterSelectedTarget()
        {
            var order = new List<FoxRunEndpoint>();

            var result = FoxRunPublishFanout.Dispatch(
                Resolved(FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge),
                timestampNs: 5UL,
                capture: () => new Sample(2),
                isReady: _ => true,
                publish: (target, _, __) =>
                {
                    order.Add(target);
                    if (target == FoxRunEndpoint.Foxglove)
                        throw new InvalidOperationException("live failed");
                    return true;
                });

            Assert.Equal(
                new[] { FoxRunEndpoint.Foxglove, FoxRunEndpoint.Ros2Bridge },
                order);
            Assert.Equal(FoxRunPublishTargetStatus.Degraded, result.Status);
            Assert.Equal(FoxRunEndpoint.Ros2Bridge, result.SucceededTargets);
            Assert.Equal(FoxRunEndpoint.Foxglove, result.FailedTargets);
        }

        [Fact]
        public void RecoverableDiagnosticFailureDoesNotBlockLaterSelectedTarget()
        {
            var order = new List<FoxRunEndpoint>();

            var result = FoxRunPublishFanout.Dispatch(
                Resolved(FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge),
                timestampNs: 6UL,
                capture: () => new Sample(3),
                isReady: _ => true,
                publish: (target, _, __) =>
                {
                    order.Add(target);
                    if (target == FoxRunEndpoint.Foxglove)
                        throw new InvalidOperationException("live failed");
                    return true;
                },
                onTargetFault: (_, __, ___) =>
                    throw new InvalidOperationException("diagnostic failed"));

            Assert.Equal(
                new[] { FoxRunEndpoint.Foxglove, FoxRunEndpoint.Ros2Bridge },
                order);
            Assert.Equal(FoxRunEndpoint.Ros2Bridge, result.SucceededTargets);
            Assert.Equal(FoxRunEndpoint.Foxglove, result.FailedTargets);
        }

        [Fact]
        public void FatalDiagnosticFailurePassesThroughFanout()
        {
            Assert.Throws<OutOfMemoryException>(() =>
                FoxRunPublishFanout.Dispatch(
                    Resolved(FoxRunEndpoint.Foxglove),
                    timestampNs: 7UL,
                    capture: () => new Sample(4),
                    isReady: _ => true,
                    publish: (_, __, ___) =>
                        throw new InvalidOperationException("target failed"),
                    onTargetFault: (_, __, ___) =>
                        throw new OutOfMemoryException("fatal diagnostic")));
        }

        [Fact]
        public void FatalReadinessExceptionIsNotConvertedToAnUnavailableTarget()
        {
            Assert.Throws<OutOfMemoryException>(() =>
                FoxRunPublishFanout.Dispatch(
                    Resolved(FoxRunEndpoint.Foxglove),
                    timestampNs: 5UL,
                    capture: () => new Sample(2),
                    isReady: _ => throw new OutOfMemoryException("fatal"),
                    publish: (_, __, ___) => true));
        }

        [Fact]
        public void FatalPublishExceptionIsNotConvertedToADegradedTarget()
        {
            Assert.Throws<OutOfMemoryException>(() =>
                FoxRunPublishFanout.Dispatch(
                    Resolved(FoxRunEndpoint.Foxglove),
                    timestampNs: 5UL,
                    capture: () => new Sample(2),
                    isReady: _ => true,
                    publish: (_, __, ___) => throw new OutOfMemoryException("fatal")));
        }

        [Fact]
        public void RemoteOriginBlocksScheduledHeartbeatUntilLocalMutationButNotExplicitTrigger()
        {
            var state = new FoxRunPublishOriginState<int>();
            state.MarkRemoteApplied(4);

            Assert.False(state.CanPublishScheduled(4));
            Assert.False(state.CanPublishScheduled(4));
            Assert.True(state.CanPublishScheduled(5));

            state.MarkRemoteApplied(7);
            Assert.True(state.CanPublishExplicit(7));
        }

        [Fact]
        public void GeneratedSourceCapturesEachMemberOnceAndCarriesDirectionalMetadata()
        {
            var presence = FoxRunNamedArgumentPresence.Targets
                           | FoxRunNamedArgumentPresence.Encoding
                           | FoxRunNamedArgumentPresence.Reliability;
            var type = new FoxRunGenerationType(
                "Demo",
                "FanoutProbe",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "FanoutProbe",
                        "_value",
                        "field",
                        "System.Int32",
                        true,
                        false,
                        string.Empty,
                        "/phase184/generated",
                        10f,
                        "Demo.Fanout",
                        1,
                        0f,
                        "UnitTest",
                        0,
                        string.Empty,
                        mode: 1,
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                        namedArgumentPresence: presence,
                        targets: FoxRunGenerationDescriptorConstants.FoxgloveTarget
                                 + ","
                                 + FoxRunGenerationDescriptorConstants.Ros2BridgeTarget,
                        qosReliability: FoxRunGenerationDescriptorConstants.BestEffortQosReliability)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("IFoxglovePublishCaptureSource", source, StringComparison.Ordinal);
            Assert.Contains("IFoxglovePublishTargetSource", source, StringComparison.Ordinal);
            Assert.Equal(1, Count(source, "this._value"));
            Assert.Contains("__foxRunCapture_0_0", source, StringComparison.Ordinal);
            Assert.Contains("declaredEncoding: FoxRunEncoding.JSON", source, StringComparison.Ordinal);
            Assert.Contains("hasExplicitEncoding: true", source, StringComparison.Ordinal);
            Assert.Contains("qosReliability: FoxRunQosReliability.BestEffort", source, StringComparison.Ordinal);
            Assert.Contains("hasExplicitReliability: true", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedCaptureRollsBackEveryFieldWhenAGetterThrows()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "TransactionalCaptureProbe",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "TransactionalCaptureProbe", "First", "property",
                        "System.String", false, false, string.Empty,
                        "/phase184/capture", 10f, "Demo.Capture", 1, 0f,
                        "UnitTest", 0, string.Empty, mode: 1),
                    new FoxRunGenerationMember(
                        "Demo", "TransactionalCaptureProbe", "Throwing", "property",
                        "System.String", false, false, string.Empty,
                        "/phase184/capture", 10f, "Demo.Capture", 1, 0f,
                        "UnitTest", 1, string.Empty, mode: 1)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);
            var begin = source.Substring(
                source.IndexOf(
                    "bool IFoxglovePublishCaptureSource.FoxgloveLog_BeginCapture",
                    StringComparison.Ordinal));
            begin = begin.Substring(
                0,
                begin.IndexOf(
                    "void IFoxglovePublishCaptureSource.FoxgloveLog_EndCapture",
                    StringComparison.Ordinal));

            Assert.Contains("try", begin, StringComparison.Ordinal);
            Assert.Contains("catch", begin, StringComparison.Ordinal);
            Assert.Contains("__foxRunCapture_0_0 = default;", begin, StringComparison.Ordinal);
            Assert.Contains("__foxRunCapture_0_1 = default;", begin, StringComparison.Ordinal);
            Assert.Contains("__foxRunCaptureActive_0 = false;", begin, StringComparison.Ordinal);
            Assert.Contains("throw;", begin, StringComparison.Ordinal);
        }

        [Fact]
        public void PackagedNativeKeepsJsonFactoryPayloadWhileBridgeUsesCdr()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "PackagedFanoutProbe",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "PackagedFanoutProbe", "_image", "field",
                        "Foxglove.RawImage", true, false, string.Empty,
                        "/phase184/image", 10f, "foxglove_msgs/msg/RawImage", 1, 0f,
                        "UnitTest", 0, string.Empty,
                        mode: 1,
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                        namedArgumentPresence: FoxRunNamedArgumentPresence.Targets
                                               | FoxRunNamedArgumentPresence.Encoding,
                        targets: FoxRunGenerationDescriptorConstants.Ros2NativeTarget
                                 + ","
                                 + FoxRunGenerationDescriptorConstants.Ros2BridgeTarget)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains(
                "var __nativeJson_0 = __BuildFoxRunJson_0();",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "router.PublishTarget(target, __contract, nowNs, __nativeJson_0, __foxRunOrigin)",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain("__nativeCdr_0", source, StringComparison.Ordinal);
            Assert.Contains("__bridgeCdr_0", source, StringComparison.Ordinal);
            Assert.Contains("Ros2CdrSerializerRegistry.TrySerialize", source, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedFullDuplexUsesPersistentValueOriginInsteadOfOneShotSuppression()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "DuplexProbe",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "DuplexProbe",
                        "_value",
                        "field",
                        "System.Int32",
                        true,
                        false,
                        string.Empty,
                        "/phase184/duplex",
                        10f,
                        "Demo.Duplex",
                        2,
                        0f,
                        "UnitTest",
                        0,
                        string.Empty,
                        mode: 3,
                        encoding: FoxRunGenerationDescriptorConstants.JsonEncoding)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__foxRunRemoteOwned_0", source, StringComparison.Ordinal);
            Assert.Contains("__foxRunRemoteValue_0_0", source, StringComparison.Ordinal);
            Assert.Contains("__FoxRunCanPublishOrigin_0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("__foxRunSuppressNextPublish_0", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonConvert.SerializeObject", source, StringComparison.Ordinal);
        }

        private static FoxgloveLogTopicInfo Topic(
            FoxRunEndpoint declaredTargets,
            bool hasExplicitTargets,
            FoxRunQosReliability qosReliability = 0,
            bool hasExplicitReliability = false)
            => new(
                "/phase184/fanout",
                10f,
                FoxRunPolicy.Change,
                0f,
                FoxRunFlow.Publish,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets,
                hasExplicitTargets,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                qosProfile: 0,
                hasExplicitQosProfile: false,
                qosReliability,
                hasExplicitReliability,
                qosDurability: 0,
                hasExplicitDurability: false,
                qosHistory: 0,
                hasExplicitHistory: false,
                qosDepth: 0,
                hasExplicitDepth: false,
                hasExplicitHz: false);

        private static FoxRunResolvedPublishContract Resolved(FoxRunEndpoint targets)
        {
            Assert.True(FoxRunResolvedPublishContract.TryResolve(
                Topic(targets, hasExplicitTargets: true),
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.JSON,
                nativeDefaultQos: FoxRunResolvedQos.Default,
                bridgeDefaultQos: FoxRunResolvedQos.Default,
                defaultSource: FoxRunEndpoint.Foxglove,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                out var contract,
                out var diagnostic), diagnostic);
            return contract;
        }

        private sealed class Sample
        {
            public Sample(int value) => Value = value;
            public int Value { get; }
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }
    }
}
