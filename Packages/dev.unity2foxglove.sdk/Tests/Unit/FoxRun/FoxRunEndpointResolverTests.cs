// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunEndpointResolverTests
    {
        [Fact]
        public void PublicEndpointVocabularyHasNoNoneOrInheritMember()
        {
            Assert.True(typeof(FoxRunEndpoint).IsDefined(typeof(FlagsAttribute), inherit: false));
            Assert.Equal(
                new[] { "Foxglove", "Ros2Native", "Ros2Bridge" },
                Enum.GetNames(typeof(FoxRunEndpoint)));
            Assert.DoesNotContain("None", Enum.GetNames(typeof(FoxRunEndpoint)));
            Assert.DoesNotContain("Inherit", Enum.GetNames(typeof(FoxRunEndpoint)));
            Assert.DoesNotContain("Inherit", Enum.GetNames(typeof(FoxRunEncoding)));
        }

        [Fact]
        public void ExplicitPublishTargetsReplaceTheProfileTargets()
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.Publish,
                declaredSource: (FoxRunEndpoint)0,
                hasExplicitSource: false,
                declaredTargets: FoxRunEndpoint.Ros2Native,
                hasExplicitTargets: true,
                declaredEncoding: (FoxRunEncoding)0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);

            Assert.True(result.Success);
            Assert.Equal(FoxRunEndpoint.Ros2Native, result.Topology.Targets);
            Assert.Equal((FoxRunEndpoint)0, result.Topology.Source);
            Assert.Equal((FoxRunEncoding)0, result.Topology.PublishEncoding);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        public void SubscribeSourceRejectsZeroMultipleBitsAndBridge(int source)
        {
            var result = ResolveSubscribe(
                (FoxRunEndpoint)source,
                hasExplicitSource: true,
                FoxRunEncoding.Protobuf,
                hasExplicitEncoding: false);

            Assert.False(result.Success);
            Assert.NotEqual(FoxRunEndpointDiagnosticCode.None, result.DiagnosticCode);
        }

        [Fact]
        public void FullDuplexAllowsEncodingWhenOnlyOneDirectionUsesFoxglove()
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: FoxRunEndpoint.Ros2Native,
                hasExplicitSource: true,
                declaredTargets: FoxRunEndpoint.Foxglove,
                hasExplicitTargets: true,
                declaredEncoding: FoxRunEncoding.JSON,
                hasExplicitEncoding: true,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Ros2Native,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.Protobuf);

            Assert.True(result.Success);
            Assert.Equal(FoxRunEncoding.JSON, result.Topology.PublishEncoding);
            Assert.Equal((FoxRunEncoding)0, result.Topology.SubscribeEncoding);
        }

        [Fact]
        public void ExplicitEncodingFailsWhenNoResolvedDirectionUsesFoxglove()
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: FoxRunEndpoint.Ros2Native,
                hasExplicitSource: true,
                declaredTargets: FoxRunEndpoint.Ros2Native | FoxRunEndpoint.Ros2Bridge,
                hasExplicitTargets: true,
                declaredEncoding: FoxRunEncoding.Protobuf,
                hasExplicitEncoding: true,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunEndpointDiagnosticCode.EncodingRequiresFoxglove,
                result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitQosFailsClosedWhenInheritedProfileResolvesAllDirectionsToFoxglove()
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: (FoxRunEndpoint)0,
                hasExplicitSource: false,
                declaredTargets: (FoxRunEndpoint)0,
                hasExplicitTargets: false,
                declaredEncoding: (FoxRunEncoding)0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                hasExplicitQos: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunEndpointDiagnosticCode.QosRequiresRos2, result.DiagnosticCode);
            Assert.Equal(
                "FoxRun QoS requires at least one resolved ROS 2 direction.",
                result.DiagnosticMessage);
        }

        [Theory]
        [InlineData((int)FoxRunEndpoint.Ros2Native, (int)FoxRunEndpoint.Foxglove)]
        [InlineData((int)FoxRunEndpoint.Foxglove, (int)FoxRunEndpoint.Ros2Native)]
        [InlineData((int)FoxRunEndpoint.Foxglove, (int)FoxRunEndpoint.Ros2Bridge)]
        public void ExplicitQosSucceedsWhenEitherResolvedDirectionUsesRos2(
            int source,
            int targets)
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: (FoxRunEndpoint)0,
                hasExplicitSource: false,
                declaredTargets: (FoxRunEndpoint)0,
                hasExplicitTargets: false,
                declaredEncoding: (FoxRunEncoding)0,
                hasExplicitEncoding: false,
                defaultSource: (FoxRunEndpoint)source,
                defaultTargets: (FoxRunEndpoint)targets,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON,
                hasExplicitQos: true);

            Assert.True(result.Success, result.DiagnosticMessage);
        }

        [Fact]
        public void OmittedFullDuplexEncodingInheritsEachDirectionalProfile()
        {
            var result = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: (FoxRunEndpoint)0,
                hasExplicitSource: false,
                declaredTargets: (FoxRunEndpoint)0,
                hasExplicitTargets: false,
                declaredEncoding: (FoxRunEncoding)0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);

            Assert.True(result.Success);
            Assert.Equal(FoxRunEncoding.Protobuf, result.Topology.PublishEncoding);
            Assert.Equal(FoxRunEncoding.JSON, result.Topology.SubscribeEncoding);
        }

        [Fact]
        public void EncodingProtocolVocabularyContainsOnlyProtobufAndJson()
        {
            Assert.Equal(
                "protobuf",
                FoxRunEncodingResolver.ToProtocolEncoding(FoxRunEncoding.Protobuf));
            Assert.Equal(
                "json",
                FoxRunEncodingResolver.ToProtocolEncoding(FoxRunEncoding.JSON));
            Assert.Throws<ArgumentException>(() =>
                FoxRunEncodingResolver.FromProtocolEncoding("cdr"));
            Assert.Throws<ArgumentException>(() =>
                FoxRunEncodingResolver.FromProtocolEncoding("ros2"));
            Assert.Throws<ArgumentException>(() =>
                FoxRunEncodingResolver.FromProtocolEncoding("inherit"));
        }

        private static FoxRunEndpointResolution ResolveSubscribe(
            FoxRunEndpoint source,
            bool hasExplicitSource,
            FoxRunEncoding encoding,
            bool hasExplicitEncoding)
            => FoxRunEndpointResolver.Resolve(
                FoxRunFlow.Subscribe,
                declaredSource: source,
                hasExplicitSource,
                declaredTargets: (FoxRunEndpoint)0,
                hasExplicitTargets: false,
                declaredEncoding: encoding,
                hasExplicitEncoding,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);
    }
}
