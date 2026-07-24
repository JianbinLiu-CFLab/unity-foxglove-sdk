// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks official ROS 2 QoS profile and policy resolution.

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunRos2QosProfileResolverTests
    {
        [Fact]
        public void OfficialBaseProfilesResolveWithoutPolicyRewrites()
        {
            AssertQos(
                Resolve(profile: FoxRunQosProfile.Default, hasProfile: true).Qos,
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                10);
            AssertQos(
                Resolve(profile: FoxRunQosProfile.SensorData, hasProfile: true).Qos,
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                5);
            AssertQos(
                Resolve(profile: FoxRunQosProfile.SystemDefault, hasProfile: true).Qos,
                FoxRunQosProfile.SystemDefault,
                FoxRunQosReliability.SystemDefault,
                FoxRunQosDurability.SystemDefault,
                FoxRunQosHistory.SystemDefault,
                0);
        }

        [Fact]
        public void OmittedDeclarationInheritsTheDirectionalProfileExactly()
        {
            var inherited = new FoxRunResolvedQos(
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                17);

            var result = FoxRunRos2QosProfileResolver.Resolve(
                default, false,
                default, false,
                default, false,
                default, false,
                0, false,
                inherited);

            Assert.True(result.Success, result.DiagnosticMessage);
            Assert.Equal(inherited, result.Qos);
        }

        [Fact]
        public void OmittedDeclarationInheritsAllThreeDirectionalDefaultsIndependently()
        {
            var nativePublishDefault = FoxRunResolvedQos.SensorData;
            var bridgeDefault = new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepAll,
                0);
            var nativeSubscribeDefault = FoxRunResolvedQos.SystemDefault;

            var nativePublish = Resolve(inherited: nativePublishDefault);
            var bridge = Resolve(inherited: bridgeDefault);
            var nativeSubscribe = Resolve(inherited: nativeSubscribeDefault);

            Assert.True(nativePublish.Success, nativePublish.DiagnosticMessage);
            Assert.True(bridge.Success, bridge.DiagnosticMessage);
            Assert.True(nativeSubscribe.Success, nativeSubscribe.DiagnosticMessage);
            Assert.Equal(nativePublishDefault, nativePublish.Qos);
            Assert.Equal(bridgeDefault, bridge.Qos);
            Assert.Equal(nativeSubscribeDefault, nativeSubscribe.Qos);
            Assert.NotEqual(nativePublish.Qos, bridge.Qos);
            Assert.NotEqual(nativePublish.Qos, nativeSubscribe.Qos);
            Assert.NotEqual(bridge.Qos, nativeSubscribe.Qos);
        }

        [Fact]
        public void ReliabilityOverrideChangesOnlyReliability()
        {
            var result = Resolve(
                profile: FoxRunQosProfile.SensorData,
                hasProfile: true,
                reliability: FoxRunQosReliability.Reliable,
                hasReliability: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            AssertQos(
                result.Qos,
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                5);
        }

        [Fact]
        public void DurabilityOverrideChangesOnlyDurability()
        {
            var result = Resolve(
                profile: FoxRunQosProfile.SensorData,
                hasProfile: true,
                durability: FoxRunQosDurability.TransientLocal,
                hasDurability: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            AssertQos(
                result.Qos,
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                5);
        }

        [Fact]
        public void HistoryOverrideChangesOnlyHistoryAndItsDepthCoupling()
        {
            var result = Resolve(
                profile: FoxRunQosProfile.SensorData,
                hasProfile: true,
                history: FoxRunQosHistory.KeepAll,
                hasHistory: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            AssertQos(
                result.Qos,
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepAll,
                0);
        }

        [Fact]
        public void DepthOverrideChangesOnlyDepth()
        {
            var result = Resolve(
                profile: FoxRunQosProfile.SensorData,
                hasProfile: true,
                history: FoxRunQosHistory.KeepLast,
                hasHistory: true,
                depth: 23,
                hasDepth: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            AssertQos(
                result.Qos,
                FoxRunQosProfile.SensorData,
                FoxRunQosReliability.BestEffort,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepLast,
                23);
        }

        [Fact]
        public void OneExplicitFullDuplexContractOverridesEveryDirectionalDefault()
        {
            FoxRunQosResolution ResolveDirection(FoxRunResolvedQos inherited)
                => Resolve(
                    profile: FoxRunQosProfile.SystemDefault,
                    hasProfile: true,
                    reliability: FoxRunQosReliability.Reliable,
                    hasReliability: true,
                    durability: FoxRunQosDurability.TransientLocal,
                    hasDurability: true,
                    history: FoxRunQosHistory.KeepLast,
                    hasHistory: true,
                    depth: 29,
                    hasDepth: true,
                    inherited: inherited);

            var expected = new FoxRunResolvedQos(
                FoxRunQosProfile.SystemDefault,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.TransientLocal,
                FoxRunQosHistory.KeepLast,
                29);
            var nativePublish = ResolveDirection(FoxRunResolvedQos.Default);
            var bridgePublish = ResolveDirection(FoxRunResolvedQos.SensorData);
            var nativeSubscribe = ResolveDirection(FoxRunResolvedQos.SystemDefault);

            Assert.True(nativePublish.Success, nativePublish.DiagnosticMessage);
            Assert.True(bridgePublish.Success, bridgePublish.DiagnosticMessage);
            Assert.True(nativeSubscribe.Success, nativeSubscribe.DiagnosticMessage);
            Assert.Equal(expected, nativePublish.Qos);
            Assert.Equal(expected, bridgePublish.Qos);
            Assert.Equal(expected, nativeSubscribe.Qos);
        }

        [Fact]
        public void OfficialPolicyOverridesAreNeverDowngraded()
        {
            var result = Resolve(
                reliability: FoxRunQosReliability.SystemDefault,
                hasReliability: true,
                durability: FoxRunQosDurability.SystemDefault,
                hasDurability: true,
                history: FoxRunQosHistory.KeepAll,
                hasHistory: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            AssertQos(
                result.Qos,
                FoxRunQosProfile.Default,
                FoxRunQosReliability.SystemDefault,
                FoxRunQosDurability.SystemDefault,
                FoxRunQosHistory.KeepAll,
                0);
        }

        [Fact]
        public void KeepLastWithoutDepthUsesTenWhenTheBaseHasNoDepth()
        {
            var result = Resolve(
                profile: FoxRunQosProfile.SystemDefault,
                hasProfile: true,
                history: FoxRunQosHistory.KeepLast,
                hasHistory: true);

            Assert.True(result.Success, result.DiagnosticMessage);
            Assert.Equal(FoxRunQosHistory.KeepLast, result.Qos.History);
            Assert.Equal(10, result.Qos.Depth);
        }

        [Theory]
        [InlineData(FoxRunQosHistory.KeepAll)]
        [InlineData(FoxRunQosHistory.SystemDefault)]
        public void ExplicitDepthWithNonKeepLastHistoryFailsClosed(FoxRunQosHistory history)
        {
            var result = Resolve(
                history: history,
                hasHistory: true,
                depth: 4,
                hasDepth: true);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunQosDiagnosticCode.DepthRequiresKeepLast,
                result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitZeroDepthIsNotTreatedAsOmission()
        {
            var result = Resolve(depth: 0, hasDepth: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunQosDiagnosticCode.InvalidDepth, result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitZeroProfileIsNotTreatedAsOmission()
        {
            var result = Resolve(profile: default, hasProfile: true);

            Assert.False(result.Success);
            Assert.Equal(FoxRunQosDiagnosticCode.InvalidProfile, result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitZeroReliabilityIsNotTreatedAsOmission()
        {
            var result = Resolve(
                reliability: default,
                hasReliability: true);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunQosDiagnosticCode.InvalidReliability,
                result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitZeroDurabilityIsNotTreatedAsOmission()
        {
            var result = Resolve(
                durability: default,
                hasDurability: true);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunQosDiagnosticCode.InvalidDurability,
                result.DiagnosticCode);
        }

        [Fact]
        public void ExplicitZeroHistoryIsNotTreatedAsOmission()
        {
            var result = Resolve(
                history: default,
                hasHistory: true);

            Assert.False(result.Success);
            Assert.Equal(
                FoxRunQosDiagnosticCode.InvalidHistory,
                result.DiagnosticCode);
        }

        [Fact]
        public void ResolvedValueRejectsContradictoryHistoryAndDepth()
        {
            Assert.Throws<System.ArgumentException>(() => new FoxRunResolvedQos(
                FoxRunQosProfile.Default,
                FoxRunQosReliability.Reliable,
                FoxRunQosDurability.Volatile,
                FoxRunQosHistory.KeepAll,
                10));
        }

        private static FoxRunQosResolution Resolve(
            FoxRunQosProfile profile = default,
            bool hasProfile = false,
            FoxRunQosReliability reliability = default,
            bool hasReliability = false,
            FoxRunQosDurability durability = default,
            bool hasDurability = false,
            FoxRunQosHistory history = default,
            bool hasHistory = false,
            int depth = 0,
            bool hasDepth = false,
            FoxRunResolvedQos? inherited = null)
        {
            return FoxRunRos2QosProfileResolver.Resolve(
                profile, hasProfile,
                reliability, hasReliability,
                durability, hasDurability,
                history, hasHistory,
                depth, hasDepth,
                inherited ?? FoxRunResolvedQos.Default);
        }

        private static void AssertQos(
            FoxRunResolvedQos actual,
            FoxRunQosProfile profile,
            FoxRunQosReliability reliability,
            FoxRunQosDurability durability,
            FoxRunQosHistory history,
            int depth)
        {
            Assert.Equal(profile, actual.Profile);
            Assert.Equal(reliability, actual.Reliability);
            Assert.Equal(durability, actual.Durability);
            Assert.Equal(history, actual.History);
            Assert.Equal(depth, actual.Depth);
        }
    }
}
