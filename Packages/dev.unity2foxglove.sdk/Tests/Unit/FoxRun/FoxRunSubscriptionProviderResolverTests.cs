// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunSubscriptionProviderResolverTests
    {
        [Theory]
        [InlineData(
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunSubscriptionProvider.Ros2Native,
            FoxRunWireEncoding.Json,
            FoxRunSubscriptionProvider.FoxgloveWebSocket)]
        [InlineData(
            FoxRunSubscriptionProvider.Ros2Native,
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunWireEncoding.Inherit,
            FoxRunSubscriptionProvider.Ros2Native)]
        [InlineData(
            FoxRunSubscriptionProvider.Inherit,
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunWireEncoding.Inherit,
            FoxRunSubscriptionProvider.FoxgloveWebSocket)]
        [InlineData(
            FoxRunSubscriptionProvider.Inherit,
            FoxRunSubscriptionProvider.Ros2Native,
            FoxRunWireEncoding.Inherit,
            FoxRunSubscriptionProvider.Ros2Native)]
        [InlineData(
            FoxRunSubscriptionProvider.Inherit,
            FoxRunSubscriptionProvider.Inherit,
            FoxRunWireEncoding.Inherit,
            FoxRunSubscriptionProvider.FoxgloveWebSocket)]
        public void ProviderTruthTableResolvesExplicitInheritedAndNormalizedDefaults(
            FoxRunSubscriptionProvider declaredProvider,
            FoxRunSubscriptionProvider managerDefault,
            FoxRunWireEncoding declaredEncoding,
            FoxRunSubscriptionProvider expectedProvider)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                declaredProvider,
                managerDefault,
                FoxRunFlow.Subscribe,
                declaredEncoding,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.True(result.Success);
            Assert.Equal(expectedProvider, result.Provider);
            Assert.Equal(FoxRunSubscriptionProviderDiagnosticCode.None, result.DiagnosticCode);
            Assert.Equal(string.Empty, result.DiagnosticMessage);
        }

        [Theory]
        [InlineData(
            (FoxRunSubscriptionProvider)99,
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredProvider,
            "FoxRun subscription provider declaration is invalid.")]
        [InlineData(
            FoxRunSubscriptionProvider.Inherit,
            (FoxRunSubscriptionProvider)99,
            FoxRunSubscriptionProviderDiagnosticCode.InvalidManagerProvider,
            "FoxRun Manager subscription provider is invalid.")]
        public void ProviderTruthTableRejectsInvalidDeclaredAndManagerValues(
            FoxRunSubscriptionProvider declaredProvider,
            FoxRunSubscriptionProvider managerDefault,
            FoxRunSubscriptionProviderDiagnosticCode expectedCode,
            string expectedMessage)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                declaredProvider,
                managerDefault,
                FoxRunFlow.Subscribe,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Inherit, result.Provider);
            Assert.Equal(expectedCode, result.DiagnosticCode);
            Assert.Equal(expectedMessage, result.DiagnosticMessage);
        }

        [Fact]
        public void ManagerProviderNormalizerReturnsAConcreteDefault()
        {
            Assert.Equal(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(
                    FoxRunSubscriptionProvider.Inherit));
            Assert.Equal(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(
                    FoxRunSubscriptionProvider.Ros2Native));
        }

        [Fact]
        public void ManagerProviderNormalizerRejectsCorruption()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunSubscriptionProviderResolver.NormalizeManagerDefault(
                    (FoxRunSubscriptionProvider)99));
            Assert.Equal("managerDefault", exception.ParamName);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Json)]
        [InlineData(FoxRunWireEncoding.Protobuf)]
        public void InheritedNativeProviderPreservesAnExplicitWebSocketEncoding(
            FoxRunWireEncoding declaredEncoding)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Inherit,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunFlow.Subscribe,
                declaredEncoding,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.True(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, result.Provider);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Json)]
        [InlineData(FoxRunWireEncoding.Protobuf)]
        public void ExplicitNativeProviderRejectsAnExplicitWebSocketEncoding(
            FoxRunWireEncoding declaredEncoding)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunFlow.Subscribe,
                declaredEncoding,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, result.Provider);
            Assert.Equal(
                FoxRunSubscriptionProviderDiagnosticCode.NativeEncodingConflict,
                result.DiagnosticCode);
            Assert.Equal(
                "Explicit Ros2Native subscriptions cannot declare a WebSocket Json or Protobuf encoding.",
                result.DiagnosticMessage);
        }

        [Theory]
        [InlineData(FoxRunSubscriptionProvider.Ros2Native, FoxRunFlow.Publish)]
        [InlineData(FoxRunSubscriptionProvider.Ros2Native, FoxRunFlow.PublishAndSubscribe)]
        [InlineData(FoxRunSubscriptionProvider.Inherit, FoxRunFlow.PublishAndSubscribe)]
        public void NativeProviderFailsClosedOutsideSubscribe(
            FoxRunSubscriptionProvider declaredProvider,
            FoxRunFlow mode)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                declaredProvider,
                FoxRunSubscriptionProvider.Ros2Native,
                mode,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, result.Provider);
            Assert.Equal(
                FoxRunSubscriptionProviderDiagnosticCode.NativeRequiresSubscribe,
                result.DiagnosticCode);
            Assert.Equal(
                "Ros2Native subscriptions require Subscribe mode.",
                result.DiagnosticMessage);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Inherit)]
        [InlineData(FoxRunWireEncoding.Json)]
        [InlineData(FoxRunWireEncoding.Protobuf)]
        public void CompleteCustomNativeBidirectionalContractKeepsNativeInputAndWebSocketOutputPolicy(
            FoxRunWireEncoding outputEncoding)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunFlow.PublishAndSubscribe,
                outputEncoding,
                supportsWebSocket: true,
                supportsRos2Native: true,
                allowsNativePublishAndSubscribe: true);

            Assert.True(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, result.Provider);
            Assert.Equal(FoxRunSubscriptionProviderDiagnosticCode.None, result.DiagnosticCode);
        }

        [Fact]
        public void PackagedNativeBidirectionalContractKeepsThePhase179SubscribeFailure()
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunFlow.PublishAndSubscribe,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true,
                allowsNativePublishAndSubscribe: false);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunSubscriptionProviderDiagnosticCode.NativeRequiresSubscribe,
                result.DiagnosticCode);
        }

        [Theory]
        [InlineData(
            FoxRunSubscriptionProvider.FoxgloveWebSocket,
            false,
            true)]
        [InlineData(
            FoxRunSubscriptionProvider.Ros2Native,
            true,
            false)]
        public void UnsupportedProviderCapabilityNeverFallsBack(
            FoxRunSubscriptionProvider provider,
            bool supportsWebSocket,
            bool supportsRos2Native)
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                provider,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunFlow.Subscribe,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket,
                supportsRos2Native);

            Assert.False(result.Success);
            Assert.Equal(provider, result.Provider);
            Assert.Equal(FoxRunSubscriptionProviderDiagnosticCode.Unsupported, result.DiagnosticCode);
            Assert.Equal(
                "The resolved FoxRun subscription provider is unsupported for this type.",
                result.DiagnosticMessage);
        }

        [Fact]
        public void OrdinaryDtoWithNativeManagerDefaultDoesNotFallBackToWebSocket()
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Inherit,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunFlow.Subscribe,
                FoxRunWireEncoding.Protobuf,
                supportsWebSocket: true,
                supportsRos2Native: false);

            Assert.False(result.Success);
            Assert.Equal(FoxRunSubscriptionProvider.Ros2Native, result.Provider);
            Assert.Equal(FoxRunSubscriptionProviderDiagnosticCode.Unsupported, result.DiagnosticCode);
            Assert.Equal(
                "The resolved FoxRun subscription provider is unsupported for this type.",
                result.DiagnosticMessage);
        }

        [Fact]
        public void InvalidModeReturnsAStableDiagnostic()
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                (FoxRunFlow)99,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunSubscriptionProviderDiagnosticCode.InvalidMode, result.DiagnosticCode);
            Assert.Equal("FoxRun subscription mode is invalid.", result.DiagnosticMessage);
        }

        [Fact]
        public void InvalidEncodingReturnsAStableDiagnostic()
        {
            var result = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunSubscriptionProvider.FoxgloveWebSocket,
                FoxRunFlow.Subscribe,
                (FoxRunWireEncoding)99,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredEncoding,
                result.DiagnosticCode);
            Assert.Equal("FoxRun wire encoding declaration is invalid.", result.DiagnosticMessage);
        }

        [Fact]
        public void ProviderDiagnosticCodesRemainStable()
        {
            Assert.Equal(0, (int)FoxRunSubscriptionProviderDiagnosticCode.None);
            Assert.Equal(1, (int)FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredProvider);
            Assert.Equal(2, (int)FoxRunSubscriptionProviderDiagnosticCode.InvalidManagerProvider);
            Assert.Equal(3, (int)FoxRunSubscriptionProviderDiagnosticCode.InvalidMode);
            Assert.Equal(4, (int)FoxRunSubscriptionProviderDiagnosticCode.InvalidDeclaredEncoding);
            Assert.Equal(5, (int)FoxRunSubscriptionProviderDiagnosticCode.NativeEncodingConflict);
            Assert.Equal(6, (int)FoxRunSubscriptionProviderDiagnosticCode.NativeRequiresSubscribe);
            Assert.Equal(7, (int)FoxRunSubscriptionProviderDiagnosticCode.Unsupported);
        }

        [Theory]
        [InlineData(FoxRunRos2QosPreset.Default)]
        [InlineData(FoxRunRos2QosPreset.Reliable)]
        [InlineData(FoxRunRos2QosPreset.SensorData)]
        [InlineData(FoxRunRos2QosPreset.TransientLocal)]
        public void ExplicitQosPresetWinsOverTheManagerDefault(FoxRunRos2QosPreset declaredPreset)
        {
            var result = FoxRunRos2QosResolver.Resolve(
                declaredPreset,
                FoxRunRos2QosPreset.SensorData);

            Assert.True(result.Success);
            Assert.Equal(declaredPreset, result.Preset);
            Assert.Equal(FoxRunRos2QosDiagnosticCode.None, result.DiagnosticCode);
            Assert.Equal(string.Empty, result.DiagnosticMessage);
        }

        [Theory]
        [InlineData(FoxRunRos2QosPreset.Default)]
        [InlineData(FoxRunRos2QosPreset.Reliable)]
        [InlineData(FoxRunRos2QosPreset.SensorData)]
        [InlineData(FoxRunRos2QosPreset.TransientLocal)]
        public void InheritedQosUsesTheManagerDefault(FoxRunRos2QosPreset managerDefault)
        {
            var result = FoxRunRos2QosResolver.Resolve(
                FoxRunRos2QosPreset.Inherit,
                managerDefault);

            Assert.True(result.Success);
            Assert.Equal(managerDefault, result.Preset);
            Assert.Equal(FoxRunRos2QosDiagnosticCode.None, result.DiagnosticCode);
            Assert.Equal(string.Empty, result.DiagnosticMessage);
        }

        [Fact]
        public void ManagerInheritedQosNormalizesSafelyToDefault()
        {
            var result = FoxRunRos2QosResolver.Resolve(
                FoxRunRos2QosPreset.Inherit,
                FoxRunRos2QosPreset.Inherit);

            Assert.True(result.Success);
            Assert.Equal(FoxRunRos2QosPreset.Default, result.Preset);
            Assert.Equal(FoxRunRos2QosDiagnosticCode.None, result.DiagnosticCode);
        }

        [Theory]
        [InlineData(FoxRunRos2QosPreset.Inherit, FoxRunRos2QosPreset.Default)]
        [InlineData(FoxRunRos2QosPreset.Default, FoxRunRos2QosPreset.Default)]
        [InlineData(FoxRunRos2QosPreset.Reliable, FoxRunRos2QosPreset.Reliable)]
        [InlineData(FoxRunRos2QosPreset.SensorData, FoxRunRos2QosPreset.SensorData)]
        [InlineData(FoxRunRos2QosPreset.TransientLocal, FoxRunRos2QosPreset.TransientLocal)]
        [InlineData((FoxRunRos2QosPreset)99, FoxRunRos2QosPreset.Default)]
        public void SerializedManagerQosNormalizerRecoversInspectorValuesWithoutWeakeningRuntimeResolution(
            FoxRunRos2QosPreset serialized,
            FoxRunRos2QosPreset expected)
        {
            Assert.Equal(
                expected,
                FoxRunRos2QosResolver.NormalizeSerializedManagerDefault(serialized));
        }

        [Fact]
        public void StrictManagerQosNormalizerStillRejectsCorruption()
        {
            Assert.Equal(
                FoxRunRos2QosPreset.Default,
                FoxRunRos2QosResolver.NormalizeManagerDefault(FoxRunRos2QosPreset.Inherit));

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                FoxRunRos2QosResolver.NormalizeManagerDefault((FoxRunRos2QosPreset)99));

            Assert.Equal("managerDefault", exception.ParamName);
        }

        [Fact]
        public void ExplicitQosPresetIgnoresAnInvalidUnusedManagerDefault()
        {
            var result = FoxRunRos2QosResolver.Resolve(
                FoxRunRos2QosPreset.Reliable,
                (FoxRunRos2QosPreset)99);

            Assert.True(result.Success);
            Assert.Equal(FoxRunRos2QosPreset.Reliable, result.Preset);
            Assert.Equal(FoxRunRos2QosDiagnosticCode.None, result.DiagnosticCode);
        }

        [Fact]
        public void InvalidDeclaredQosValueReturnsATypedFailure()
        {
            var result = FoxRunRos2QosResolver.Resolve(
                (FoxRunRos2QosPreset)99,
                FoxRunRos2QosPreset.Default);

            Assert.False(result.Success);
            Assert.Equal(FoxRunRos2QosPreset.Inherit, result.Preset);
            Assert.Equal(
                FoxRunRos2QosDiagnosticCode.InvalidDeclaredPreset,
                result.DiagnosticCode);
            Assert.Equal("FoxRun ROS2 QoS declaration is invalid.", result.DiagnosticMessage);
        }

        [Fact]
        public void InvalidManagerQosValueReturnsATypedFailureWhenInherited()
        {
            var result = FoxRunRos2QosResolver.Resolve(
                FoxRunRos2QosPreset.Inherit,
                (FoxRunRos2QosPreset)99);

            Assert.False(result.Success);
            Assert.Equal(FoxRunRos2QosPreset.Inherit, result.Preset);
            Assert.Equal(
                FoxRunRos2QosDiagnosticCode.InvalidManagerPreset,
                result.DiagnosticCode);
            Assert.Equal("FoxRun Manager ROS2 QoS default is invalid.", result.DiagnosticMessage);
        }

        [Fact]
        public void QosDiagnosticCodesRemainStable()
        {
            Assert.Equal(0, (int)FoxRunRos2QosDiagnosticCode.None);
            Assert.Equal(1, (int)FoxRunRos2QosDiagnosticCode.InvalidDeclaredPreset);
            Assert.Equal(2, (int)FoxRunRos2QosDiagnosticCode.InvalidManagerPreset);
        }
    }
}
