// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
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
    }
}
