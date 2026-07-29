// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunPublishSessionPolicyTests
    {
        [Fact]
        public void InitialStateIsAnInertNonNullSnapshot()
        {
            var state = new FoxRunPublishSessionState();

            Assert.NotNull(state.Current);
            Assert.False(state.Current.SessionActive);
            Assert.Equal(0UL, state.Current.SessionGeneration);
            Assert.Equal((FoxRunEndpoint)0, state.Current.DefaultTargets);
        }

        [Fact]
        public void BeginCapturesEveryDirectionalDefault()
        {
            var state = new FoxRunPublishSessionState();
            var bridgeQos = new FoxRunResolvedQos(
                FoxRunQosProfile.SystemDefault,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepAll,
                0);

            var policy = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.JSON,
                defaultPublishRateHz: 25f,
                FoxRunResolvedQos.SensorData,
                bridgeQos);

            Assert.True(policy.SessionActive);
            Assert.Equal(1UL, policy.SessionGeneration);
            Assert.Equal(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                policy.DefaultTargets);
            Assert.Equal(FoxRunEncoding.JSON, policy.FoxgloveEncoding);
            Assert.Equal(25f, policy.DefaultPublishRateHz);
            Assert.Equal(FoxRunResolvedQos.SensorData, policy.NativeRos2Qos);
            Assert.Equal(bridgeQos, policy.BridgeRos2Qos);
        }

        [Fact]
        public void RepeatedBeginFreezesInspectorEditsAndTransportRestarts()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunResolvedQos.Default,
                FoxRunResolvedQos.SystemDefault);

            var repeated = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.JSON,
                90f,
                FoxRunResolvedQos.SensorData,
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.BestEffort,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    99));

            Assert.Same(first, repeated);
            Assert.Equal(FoxRunEndpoint.Foxglove, repeated.DefaultTargets);
            Assert.Equal(FoxRunEncoding.Protobuf, repeated.FoxgloveEncoding);
            Assert.Equal(10f, repeated.DefaultPublishRateHz);
            Assert.Equal(FoxRunResolvedQos.Default, repeated.NativeRos2Qos);
            Assert.Equal(FoxRunResolvedQos.SystemDefault, repeated.BridgeRos2Qos);
        }

        [Fact]
        public void MessagePackPublishDefaultIsFrozenUntilTheSessionEnds()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                (FoxRunEncoding)3,
                10f,
                FoxRunResolvedQos.Default,
                FoxRunResolvedQos.Default);
            var repeated = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.JSON,
                20f,
                FoxRunResolvedQos.SensorData,
                FoxRunResolvedQos.SensorData);

            Assert.Same(first, repeated);
            Assert.Equal((FoxRunEncoding)3, repeated.FoxgloveEncoding);

            state.End();
            var recaptured = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.JSON,
                20f,
                FoxRunResolvedQos.SensorData,
                FoxRunResolvedQos.SensorData);
            Assert.Equal(FoxRunEncoding.JSON, recaptured.FoxgloveEncoding);
        }

        [Fact]
        public void EndThenBeginRecapturesAndAdvancesGeneration()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunResolvedQos.Default,
                FoxRunResolvedQos.SystemDefault);

            var disabled = state.End();
            var repeatedEnd = state.End();
            var second = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                20f,
                FoxRunResolvedQos.SensorData,
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepAll,
                    0));

            Assert.False(disabled.SessionActive);
            Assert.Same(disabled, repeatedEnd);
            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
            Assert.Equal(FoxRunEndpoint.Ros2Native, second.DefaultTargets);
            Assert.Equal(FoxRunResolvedQos.SensorData, second.NativeRos2Qos);
            Assert.Equal(FoxRunQosHistory.KeepAll, second.BridgeRos2Qos.History);
        }

        [Fact]
        public void BeginRejectsInvalidProfileValues()
        {
            var state = new FoxRunPublishSessionState();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginIfNeeded(
                0,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunResolvedQos.Default,
                FoxRunResolvedQos.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                0,
                10f,
                FoxRunResolvedQos.Default,
                FoxRunResolvedQos.Default));
        }

        [Fact]
        public void ManagerNotifiesPublishSessionBeginAndEndSynchronously()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/"
                + "FoxgloveManager.FoxRunPublishing.cs");

            Assert.Contains(
                "public event Action<FoxRunPublishSessionPolicy> FoxRunPublishSessionChanged;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "NotifyFoxRunPublishSessionChanged(policy);",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "((Action<FoxRunPublishSessionPolicy>)subscriber)(policy);",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void UnavailableInheritedMessagePackPublishFailsResolutionWithoutCodecFallback()
        {
            const string declaringType = "Demo.UnavailablePublisher";
            const string topic = "/phase185/unavailable-publish";
            var manifest = new FoxRunSchemaManifestInfo(
                3,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "phase185-publish-session-gate",
                "phase185-publish-session-gate",
                new[]
                {
                    new FoxRunSchemaTypeInfo(
                        declaringType,
                        new[]
                        {
                            new FoxRunSchemaContractInfo(
                                declaringType,
                                topic,
                                string.Empty,
                                "msgpack",
                                "msgpack",
                                "msgpack",
                                "policy",
                                "FixedRate",
                                10f,
                                0f,
                                Array.Empty<FoxRunSchemaFieldInfo>(),
                                flow: "Publish",
                                publishAvailable: false,
                                unavailableDiagnosticId: "FOXRUN619",
                                unavailableReason: "incompatible publish schedule")
                        })
                });
            var info = new FoxgloveLogTopicInfo(
                topic,
                10f,
                FoxRunPolicy.FixedRate,
                0f);

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                var resolved = FoxRunResolvedPublishContract.TryResolve(
                    info,
                    FoxRunEndpoint.Foxglove,
                    FoxRunEncoding.MessagePack,
                    FoxRunResolvedQos.Default,
                    FoxRunResolvedQos.Default,
                    FoxRunEndpoint.Foxglove,
                    FoxRunEncoding.MessagePack,
                    out var contract,
                    out var diagnostic);

                Assert.False(resolved);
                Assert.Null(contract);
                Assert.Contains("FOXRUN619", diagnostic, StringComparison.Ordinal);
                Assert.DoesNotContain("protobuf", diagnostic, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("json", diagnostic, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }

        [Fact]
        [Trait("Phase", "185-A")]
        public void FullDuplexUnavailableMessagePackSessionReportsTheRequestedDirection()
        {
            const string declaringType = "Demo.UnavailableFullDuplex";
            const string topic = "/phase185/unavailable-full-duplex";
            var contractInfo = new FoxRunSchemaContractInfo(
                declaringType,
                topic,
                string.Empty,
                "msgpack",
                "msgpack",
                "msgpack",
                "policy",
                "FixedRate",
                10f,
                0f,
                Array.Empty<FoxRunSchemaFieldInfo>(),
                flow: "PublishAndSubscribe",
                publishAvailable: false,
                subscribeAvailable: false,
                publishUnavailableDiagnosticId: "FOXRUN619",
                publishUnavailableReason: "incompatible publish schedule",
                subscribeUnavailableDiagnosticId: "FOXRUN616",
                subscribeUnavailableReason: "inbound DTO is not constructible");
            var manifest = new FoxRunSchemaManifestInfo(
                3,
                "Unity2Foxglove",
                "FoxRun",
                1,
                "phase185-directional-session-gate",
                "phase185-directional-session-gate",
                new[]
                {
                    new FoxRunSchemaTypeInfo(declaringType, new[] { contractInfo })
                });

            FoxRunSchemaInfoRegistry.ClearForTests();
            try
            {
                FoxRunSchemaInfoRegistry.RegisterGenerated(manifest);

                Assert.False(FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                    declaringType,
                    topic,
                    FoxRunFlow.Publish,
                    FoxRunEncoding.MessagePack,
                    out _,
                    out var publishDiagnostic));
                Assert.Contains("FOXRUN619", publishDiagnostic, StringComparison.Ordinal);
                Assert.DoesNotContain("FOXRUN616", publishDiagnostic, StringComparison.Ordinal);

                Assert.False(FoxRunSchemaInfoRegistry.TryResolveSessionContract(
                    declaringType,
                    topic,
                    FoxRunFlow.Subscribe,
                    FoxRunEncoding.MessagePack,
                    out _,
                    out var subscribeDiagnostic));
                Assert.Contains("FOXRUN616", subscribeDiagnostic, StringComparison.Ordinal);
                Assert.DoesNotContain("FOXRUN619", subscribeDiagnostic, StringComparison.Ordinal);
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
            }
        }
    }
}
