// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Ros2Bridge;
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
            var bridgeQos = new Ros2BridgeQosProfile(
                Ros2BridgeReliability.BestEffort,
                Ros2BridgeDurability.TransientLocal,
                7,
                "Test");

            var policy = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.JSON,
                defaultPublishRateHz: 25f,
                FoxRunRos2QosPreset.SensorData,
                bridgeQos);

            Assert.True(policy.SessionActive);
            Assert.Equal(1UL, policy.SessionGeneration);
            Assert.Equal(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                policy.DefaultTargets);
            Assert.Equal(FoxRunEncoding.JSON, policy.FoxgloveEncoding);
            Assert.Equal(25f, policy.DefaultPublishRateHz);
            Assert.Equal(FoxRunRos2QosPreset.SensorData, policy.NativeRos2Qos);
            Assert.Equal("Test", policy.BridgeRos2Qos.PresetName);
        }

        [Fact]
        public void RepeatedBeginFreezesInspectorEditsAndTransportRestarts()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunRos2QosPreset.Default,
                Ros2BridgeQosProfile.ReliableDefault);

            var repeated = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.JSON,
                90f,
                FoxRunRos2QosPreset.TransientLocal,
                new Ros2BridgeQosProfile(
                    Ros2BridgeReliability.BestEffort,
                    Ros2BridgeDurability.TransientLocal,
                    99,
                    "Changed"));

            Assert.Same(first, repeated);
            Assert.Equal(FoxRunEndpoint.Foxglove, repeated.DefaultTargets);
            Assert.Equal(FoxRunEncoding.Protobuf, repeated.FoxgloveEncoding);
            Assert.Equal(10f, repeated.DefaultPublishRateHz);
            Assert.Equal("Reliable Default", repeated.BridgeRos2Qos.PresetName);
        }

        [Fact]
        public void EndThenBeginRecapturesAndAdvancesGeneration()
        {
            var state = new FoxRunPublishSessionState();
            var first = state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunRos2QosPreset.Default,
                Ros2BridgeQosProfile.ReliableDefault);

            var disabled = state.End();
            var repeatedEnd = state.End();
            var second = state.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                20f,
                FoxRunRos2QosPreset.Reliable,
                Ros2BridgeQosProfile.ReliableDefault);

            Assert.False(disabled.SessionActive);
            Assert.Same(disabled, repeatedEnd);
            Assert.Equal(first.SessionGeneration + 1UL, second.SessionGeneration);
            Assert.Equal(FoxRunEndpoint.Ros2Native, second.DefaultTargets);
        }

        [Fact]
        public void BeginRejectsInvalidProfileValues()
        {
            var state = new FoxRunPublishSessionState();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginIfNeeded(
                0,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunRos2QosPreset.Default,
                Ros2BridgeQosProfile.ReliableDefault));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                0,
                10f,
                FoxRunRos2QosPreset.Default,
                Ros2BridgeQosProfile.ReliableDefault));
        }
    }
}
