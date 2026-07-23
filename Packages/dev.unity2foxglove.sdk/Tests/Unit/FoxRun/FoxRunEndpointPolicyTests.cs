// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunEndpointPolicyTests
    {
        [Fact]
        public void SourceValuesRemainStable()
        {
            Assert.Equal(0, (int)(FoxRunEndpoint)0);
            Assert.Equal(1, (int)FoxRunEndpoint.Foxglove);
            Assert.Equal(2, (int)FoxRunEndpoint.Ros2Native);
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

            Assert.Equal((FoxRunEndpoint)0, attribute.Source);
            Assert.Equal(FoxRunRos2QosPreset.Inherit, attribute.Ros2Qos);
        }

        [Fact]
        public void SubscriptionPolicyBelongsOnlyToFoxRunAttribute()
        {
            Assert.NotNull(typeof(FoxRunAttribute).GetProperty(nameof(FoxRunAttribute.Source)));
            Assert.NotNull(typeof(FoxRunAttribute).GetProperty(nameof(FoxRunAttribute.Ros2Qos)));
            Assert.Null(typeof(FoxRunMessageAttribute).GetProperty("Source"));
            Assert.Null(typeof(FoxRunMessageAttribute).GetProperty("Ros2Qos"));
        }

        [Fact]
        public void ZeroVersionMigrationPreservesLegacyJsonAndAddsSafeSubscriptionDefaults()
        {
            var migrated = Migrate(
                serializationVersion: 0,
                legacyDefault: FoxRunEncoding.JSON,
                publishDefault: FoxRunEncoding.Protobuf,
                subscriptionDefault: FoxRunEncoding.Protobuf,
                providerDefault: FoxRunEndpoint.Ros2Native,
                qosDefault: FoxRunRos2QosPreset.SensorData,
                nativeCopyBudgetBytes: 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.JSON, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes, migrated.NativeCopyBudgetBytes);
        }

        [Fact]
        public void PreviousVersionMigrationPreservesDirectionalEncodingsAndAddsSafeDefaults()
        {
            var migrated = Migrate(
                serializationVersion: 1,
                legacyDefault: FoxRunEncoding.Protobuf,
                publishDefault: FoxRunEncoding.Protobuf,
                subscriptionDefault: FoxRunEncoding.JSON,
                providerDefault: FoxRunEndpoint.Ros2Native,
                qosDefault: FoxRunRos2QosPreset.TransientLocal,
                nativeCopyBudgetBytes: 32 * 1024 * 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes, migrated.NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(FoxRunEncoding.JSON, FoxRunEndpoint.Foxglove, FoxRunRos2QosPreset.Reliable, 1024 * 1024)]
        [InlineData(FoxRunEncoding.Protobuf, FoxRunEndpoint.Foxglove, FoxRunRos2QosPreset.Default, 4 * 1024 * 1024)]
        [InlineData(FoxRunEncoding.JSON, FoxRunEndpoint.Ros2Native, FoxRunRos2QosPreset.SensorData, 8 * 1024 * 1024)]
        public void CurrentVersionRoundTripsConcreteSubscriptionPolicy(
            FoxRunEncoding subscriptionDefault,
            FoxRunEndpoint providerDefault,
            FoxRunRos2QosPreset qosDefault,
            int nativeCopyBudgetBytes)
        {
            var migrated = Migrate(
                serializationVersion: 2,
                legacyDefault: FoxRunEncoding.Protobuf,
                publishDefault: FoxRunEncoding.Protobuf,
                subscriptionDefault: subscriptionDefault,
                providerDefault: providerDefault,
                qosDefault: qosDefault,
                nativeCopyBudgetBytes: nativeCopyBudgetBytes);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.PublishDefault);
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
                legacyDefault: FoxRunEncoding.Protobuf,
                publishDefault: FoxRunEncoding.JSON,
                subscriptionDefault: FoxRunEncoding.JSON,
                providerDefault: (FoxRunEndpoint)99,
                qosDefault: (FoxRunRos2QosPreset)99,
                nativeCopyBudgetBytes: -1);

            Assert.Equal(FoxRunEncoding.JSON, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2QosPreset.Default, migrated.QosDefault);
            Assert.Equal(FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes, migrated.NativeCopyBudgetBytes);
        }

        [Fact]
        public void NativeCopyBudgetPolicyPublishesThePortableRangeAndMigrationKeepsCompatibilityAliases()
        {
            Assert.Equal(1024, FoxRunRos2NativeCopyBudgetPolicy.MinBytes);
            Assert.Equal(256 * 1024 * 1024, FoxRunRos2NativeCopyBudgetPolicy.MaxBytes);
            Assert.Equal(4 * 1024 * 1024, FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes);
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MinBytes,
                FoxRunEncodingPolicyMigration.MinRos2NativeCopyBudgetBytes);
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.MaxBytes,
                FoxRunEncodingPolicyMigration.MaxRos2NativeCopyBudgetBytes);
            Assert.Equal(
                FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes,
                FoxRunEncodingPolicyMigration.DefaultRos2NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(-1, FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes)]
        [InlineData(0, FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes)]
        [InlineData(1, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(1024, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(256 * 1024 * 1024, FoxRunRos2NativeCopyBudgetPolicy.MaxBytes)]
        [InlineData(int.MaxValue, FoxRunRos2NativeCopyBudgetPolicy.MaxBytes)]
        public void SerializedNativeCopyBudgetDefaultsOrClampsToThePortableRange(
            int configured,
            int expected)
        {
            Assert.Equal(
                expected,
                FoxRunRos2NativeCopyBudgetPolicy.NormalizeSerializedBytes(configured));
            Assert.Equal(
                expected,
                FoxRunEncodingPolicyMigration.NormalizeRos2NativeCopyBudgetBytes(configured));
        }

        [Theory]
        [InlineData(-1, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(0, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(1, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(1024, FoxRunRos2NativeCopyBudgetPolicy.MinBytes)]
        [InlineData(256 * 1024 * 1024, FoxRunRos2NativeCopyBudgetPolicy.MaxBytes)]
        [InlineData(int.MaxValue, FoxRunRos2NativeCopyBudgetPolicy.MaxBytes)]
        public void UserEditedNativeCopyBudgetClampsToThePortableRange(
            int configured,
            int expected)
        {
            Assert.Equal(
                expected,
                FoxRunRos2NativeCopyBudgetPolicy.ClampUserEditedBytes(configured));
        }

        private static MigrationResult Migrate(
            int serializationVersion,
            FoxRunEncoding legacyDefault,
            FoxRunEncoding publishDefault,
            FoxRunEncoding subscriptionDefault,
            FoxRunEndpoint providerDefault,
            FoxRunRos2QosPreset qosDefault,
            int nativeCopyBudgetBytes)
        {
            FoxRunEncodingPolicyMigration.Migrate(
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
                FoxRunEncoding publishDefault,
                FoxRunEncoding subscriptionDefault,
                FoxRunEndpoint providerDefault,
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
            internal FoxRunEncoding PublishDefault { get; }
            internal FoxRunEncoding SubscriptionDefault { get; }
            internal FoxRunEndpoint ProviderDefault { get; }
            internal FoxRunRos2QosPreset QosDefault { get; }
            internal int NativeCopyBudgetBytes { get; }
        }
    }
}
