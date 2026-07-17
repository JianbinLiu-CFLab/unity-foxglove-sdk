// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunSubscriptionProviderPolicyTests
    {
        [Fact]
        public void SubscriptionProviderValuesRemainStable()
        {
            Assert.Equal(0, (int)FoxRunSubscriptionProvider.Inherit);
            Assert.Equal(1, (int)FoxRunSubscriptionProvider.FoxgloveWebSocket);
            Assert.Equal(2, (int)FoxRunSubscriptionProvider.Ros2Native);
        }

        [Fact]
        public void Ros2QosPresetValuesRemainStable()
        {
            Assert.Equal(0, (int)FoxRunRos2QosPreset.Inherit);
            Assert.Equal(1, (int)FoxRunRos2QosPreset.Default);
            Assert.Equal(2, (int)FoxRunRos2QosPreset.Reliable);
            Assert.Equal(3, (int)FoxRunRos2QosPreset.SensorData);
            Assert.Equal(4, (int)FoxRunRos2QosPreset.TransientLocal);
        }

        [Fact]
        public void FoxRunAttributeDefaultsToInheritedSubscriptionPolicy()
        {
            var attribute = new FoxRunAttribute("/phase179/default");

            Assert.Equal(FoxRunSubscriptionProvider.Inherit, attribute.SubscriptionProvider);
            Assert.Equal(FoxRunRos2QosPreset.Inherit, attribute.Ros2Qos);
        }

        [Fact]
        public void SubscriptionPolicyBelongsOnlyToFoxRunAttribute()
        {
            Assert.NotNull(typeof(FoxRunAttribute).GetProperty(nameof(FoxRunAttribute.SubscriptionProvider)));
            Assert.NotNull(typeof(FoxRunAttribute).GetProperty(nameof(FoxRunAttribute.Ros2Qos)));
            Assert.Null(typeof(FoxRunMessageAttribute).GetProperty("SubscriptionProvider"));
            Assert.Null(typeof(FoxRunMessageAttribute).GetProperty("Ros2Qos"));
        }

        [Fact]
        public void ZeroVersionMigrationPreservesLegacyJsonAndAddsSafeSubscriptionDefaults()
        {
            var migrated = Migrate(
                serializationVersion: 0,
                legacyDefault: FoxRunWireEncoding.Json,
                publishDefault: FoxRunWireEncoding.Protobuf,
                subscriptionDefault: FoxRunWireEncoding.Protobuf,
                providerDefault: FoxRunSubscriptionProvider.Ros2Native,
                qosDefault: FoxRunRos2QosPreset.SensorData,
                nativeCopyBudgetBytes: 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Json, migrated.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Json, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(4 * 1024 * 1024, migrated.NativeCopyBudgetBytes);
        }

        [Fact]
        public void PreviousVersionMigrationPreservesDirectionalEncodingsAndAddsSafeDefaults()
        {
            var migrated = Migrate(
                serializationVersion: 1,
                legacyDefault: FoxRunWireEncoding.Protobuf,
                publishDefault: FoxRunWireEncoding.Protobuf,
                subscriptionDefault: FoxRunWireEncoding.Json,
                providerDefault: FoxRunSubscriptionProvider.Ros2Native,
                qosDefault: FoxRunRos2QosPreset.TransientLocal,
                nativeCopyBudgetBytes: 32 * 1024 * 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Json, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(4 * 1024 * 1024, migrated.NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(FoxRunWireEncoding.Json, FoxRunSubscriptionProvider.FoxgloveWebSocket, FoxRunRos2QosPreset.Reliable, 1024 * 1024)]
        [InlineData(FoxRunWireEncoding.Protobuf, FoxRunSubscriptionProvider.FoxgloveWebSocket, FoxRunRos2QosPreset.Default, 4 * 1024 * 1024)]
        [InlineData(FoxRunWireEncoding.Json, FoxRunSubscriptionProvider.Ros2Native, FoxRunRos2QosPreset.SensorData, 8 * 1024 * 1024)]
        public void CurrentVersionRoundTripsConcreteSubscriptionPolicy(
            FoxRunWireEncoding subscriptionDefault,
            FoxRunSubscriptionProvider providerDefault,
            FoxRunRos2QosPreset qosDefault,
            int nativeCopyBudgetBytes)
        {
            var migrated = Migrate(
                serializationVersion: 2,
                legacyDefault: FoxRunWireEncoding.Protobuf,
                publishDefault: FoxRunWireEncoding.Protobuf,
                subscriptionDefault: subscriptionDefault,
                providerDefault: providerDefault,
                qosDefault: qosDefault,
                nativeCopyBudgetBytes: nativeCopyBudgetBytes);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunWireEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(subscriptionDefault, migrated.SubscriptionDefault);
            Assert.Equal(providerDefault, migrated.ProviderDefault);
            Assert.Equal(qosDefault, migrated.QosDefault);
            Assert.Equal(nativeCopyBudgetBytes, migrated.NativeCopyBudgetBytes);
        }

        [Fact]
        public void CurrentVersionNormalizesCorruptProviderQosAndBudget()
        {
            var migrated = Migrate(
                serializationVersion: 2,
                legacyDefault: FoxRunWireEncoding.Protobuf,
                publishDefault: FoxRunWireEncoding.Json,
                subscriptionDefault: FoxRunWireEncoding.Json,
                providerDefault: (FoxRunSubscriptionProvider)99,
                qosDefault: (FoxRunRos2QosPreset)99,
                nativeCopyBudgetBytes: -1);

            Assert.Equal(FoxRunWireEncoding.Json, migrated.PublishDefault);
            Assert.Equal(FoxRunWireEncoding.Json, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunSubscriptionProvider.FoxgloveWebSocket, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(4 * 1024 * 1024, migrated.NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(-1, 4 * 1024 * 1024)]
        [InlineData(0, 4 * 1024 * 1024)]
        [InlineData(1, 1024)]
        [InlineData(1024, 1024)]
        [InlineData(256 * 1024 * 1024, 256 * 1024 * 1024)]
        [InlineData(int.MaxValue, 256 * 1024 * 1024)]
        public void NativeCopyBudgetDefaultsOrClampsToThePortableRange(int configured, int expected)
        {
            Assert.Equal(
                expected,
                FoxRunWireEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(configured));
        }

        private static MigrationResult Migrate(
            int serializationVersion,
            FoxRunWireEncoding legacyDefault,
            FoxRunWireEncoding publishDefault,
            FoxRunWireEncoding subscriptionDefault,
            FoxRunSubscriptionProvider providerDefault,
            FoxRunRos2QosPreset qosDefault,
            int nativeCopyBudgetBytes)
        {
            FoxRunWireEncodingPolicyMigration.Migrate(
                ref serializationVersion,
                legacyDefault,
                ref publishDefault,
                ref subscriptionDefault,
                ref providerDefault,
                ref qosDefault,
                ref nativeCopyBudgetBytes);
            return new MigrationResult(
                serializationVersion,
                publishDefault,
                subscriptionDefault,
                providerDefault,
                qosDefault,
                nativeCopyBudgetBytes);
        }

        private readonly struct MigrationResult
        {
            internal MigrationResult(
                int serializationVersion,
                FoxRunWireEncoding publishDefault,
                FoxRunWireEncoding subscriptionDefault,
                FoxRunSubscriptionProvider providerDefault,
                FoxRunRos2QosPreset qosDefault,
                int nativeCopyBudgetBytes)
            {
                SerializationVersion = serializationVersion;
                PublishDefault = publishDefault;
                SubscriptionDefault = subscriptionDefault;
                ProviderDefault = providerDefault;
                QosDefault = qosDefault;
                NativeCopyBudgetBytes = nativeCopyBudgetBytes;
            }

            internal int SerializationVersion { get; }
            internal FoxRunWireEncoding PublishDefault { get; }
            internal FoxRunWireEncoding SubscriptionDefault { get; }
            internal FoxRunSubscriptionProvider ProviderDefault { get; }
            internal FoxRunRos2QosPreset QosDefault { get; }
            internal int NativeCopyBudgetBytes { get; }
        }
    }
}
