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
        public void PortableRos2QosValuesReserveZeroForAttributeOmission()
        {
            Assert.Equal(0, (int)(FoxRunQosProfile)0);
            Assert.Equal(1, (int)FoxRunQosProfile.Default);
            Assert.Equal(2, (int)FoxRunQosProfile.SensorData);
            Assert.Equal(3, (int)FoxRunQosProfile.SystemDefault);

            Assert.Equal(0, (int)(FoxRunQosReliability)0);
            Assert.Equal(1, (int)FoxRunQosReliability.SystemDefault);
            Assert.Equal(2, (int)FoxRunQosReliability.Reliable);
            Assert.Equal(3, (int)FoxRunQosReliability.BestEffort);

            Assert.Equal(0, (int)(FoxRunQosDurability)0);
            Assert.Equal(1, (int)FoxRunQosDurability.SystemDefault);
            Assert.Equal(2, (int)FoxRunQosDurability.Volatile);
            Assert.Equal(3, (int)FoxRunQosDurability.TransientLocal);

            Assert.Equal(0, (int)(FoxRunQosHistory)0);
            Assert.Equal(1, (int)FoxRunQosHistory.SystemDefault);
            Assert.Equal(2, (int)FoxRunQosHistory.KeepLast);
            Assert.Equal(3, (int)FoxRunQosHistory.KeepAll);
        }

        [Fact]
        public void FoxRunAttributeDefaultsToOmittedPortableSubscriptionPolicy()
        {
            var attribute = new FoxRunAttribute("/phase179/default");

            Assert.Equal((FoxRunEndpoint)0, attribute.Source);
            Assert.Equal((FoxRunQosProfile)0, attribute.QoS);
            Assert.Equal((FoxRunQosReliability)0, attribute.Reliability);
            Assert.Equal((FoxRunQosDurability)0, attribute.Durability);
            Assert.Equal((FoxRunQosHistory)0, attribute.History);
            Assert.Equal(0, attribute.Depth);
        }

        [Fact]
        public void PortableQosAxesBelongToMemberAndAggregateDeclarations()
        {
            Assert.NotNull(typeof(FoxRunAttribute).GetProperty(nameof(FoxRunAttribute.Source)));
            Assert.Null(typeof(FoxRunMessageAttribute).GetProperty("Source"));
            foreach (var property in new[] { "QoS", "Reliability", "Durability", "History", "Depth" })
            {
                Assert.NotNull(typeof(FoxRunAttribute).GetProperty(property));
                Assert.NotNull(typeof(FoxRunMessageAttribute).GetProperty(property));
            }
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
                nativeCopyBudgetBytes: 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.JSON, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
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
                nativeCopyBudgetBytes: 32 * 1024 * 1024);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes, migrated.NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(FoxRunEncoding.JSON, FoxRunEndpoint.Foxglove, 1024 * 1024)]
        [InlineData(FoxRunEncoding.Protobuf, FoxRunEndpoint.Foxglove, 4 * 1024 * 1024)]
        [InlineData(FoxRunEncoding.JSON, FoxRunEndpoint.Ros2Native, 8 * 1024 * 1024)]
        public void CurrentVersionRoundTripsConcreteSubscriptionPolicy(
            FoxRunEncoding subscriptionDefault,
            FoxRunEndpoint providerDefault,
            int nativeCopyBudgetBytes)
        {
            var migrated = Migrate(
                serializationVersion: 2,
                legacyDefault: FoxRunEncoding.Protobuf,
                publishDefault: FoxRunEncoding.Protobuf,
                subscriptionDefault: subscriptionDefault,
                providerDefault: providerDefault,
                nativeCopyBudgetBytes: nativeCopyBudgetBytes);

            Assert.Equal(2, migrated.SerializationVersion);
            Assert.Equal(FoxRunEncoding.Protobuf, migrated.PublishDefault);
            Assert.Equal(subscriptionDefault, migrated.SubscriptionDefault);
            Assert.Equal(providerDefault, migrated.ProviderDefault);
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
                nativeCopyBudgetBytes: -1);

            Assert.Equal(FoxRunEncoding.JSON, migrated.PublishDefault);
            Assert.Equal(FoxRunEncoding.JSON, migrated.SubscriptionDefault);
            Assert.Equal(FoxRunEndpoint.Foxglove, migrated.ProviderDefault);
            Assert.Equal(FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes, migrated.NativeCopyBudgetBytes);
        }

        [Theory]
        [InlineData(0, FoxRunQosProfile.Default)]
        [InlineData(1, FoxRunQosProfile.Default)]
        [InlineData(2, FoxRunQosProfile.Default)]
        [InlineData(3, FoxRunQosProfile.SensorData)]
        [InlineData(99, FoxRunQosProfile.Default)]
        public void LegacyNativePresetMigrationProducesOnlyOfficialProfiles(
            int legacyPreset,
            FoxRunQosProfile expectedProfile)
        {
            var settings = new FoxRunQosProfileSettings();

            settings.MigrateLegacyPreset(legacyPreset);

            Assert.Equal(expectedProfile, settings.Profile);
        }

        [Fact]
        public void LegacyTransientLocalPresetMigratesToAnExplicitAxisOverride()
        {
            var settings = new FoxRunQosProfileSettings();

            settings.MigrateLegacyPreset(4);

            Assert.Equal(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    1),
                settings.Resolve());
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(37, 37)]
        public void LegacyBridgeCustomDepthPreservesTheOldProfileClamp(
            int legacyDepth,
            int expectedDepth)
        {
            var settings = new FoxRunQosProfileSettings();

            settings.MigrateLegacyBridgePreset(
                legacyPreset: 3,
                legacyReliability: 0,
                legacyDurability: 0,
                legacyDepth);

            Assert.Equal(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.Volatile,
                    FoxRunQosHistory.KeepLast,
                    expectedDepth),
                settings.Resolve());
        }

        [Fact]
        public void FreshQosSettingsMarkedBeforeSerializationSurviveReload()
        {
            var policyVersion = 0;
            var bridgeVersion = 0;
            var publish = new FoxRunQosProfileSettings
            {
                Profile = FoxRunQosProfile.SensorData
            };
            var subscribe = new FoxRunQosProfileSettings
            {
                Profile = FoxRunQosProfile.SystemDefault
            };
            var bridge = new FoxRunQosProfileSettings
            {
                Profile = FoxRunQosProfile.SystemDefault
            };

            FoxRunQosPolicySerializationMigration.MarkCurrent(
                ref policyVersion,
                ref bridgeVersion);
            FoxRunQosPolicySerializationMigration.MigrateNativeProfiles(
                ref policyVersion,
                ref publish,
                ref subscribe,
                legacyPublishPreset: 1,
                legacySubscribePreset: 1);
            FoxRunQosPolicySerializationMigration.MigrateBridgeProfile(
                ref bridgeVersion,
                ref bridge,
                legacyPreset: 1,
                legacyReliability: 0,
                legacyDurability: 0,
                legacyDepth: 1);

            Assert.Equal(FoxRunEncodingPolicyMigration.QosProfileSerializationVersion, policyVersion);
            Assert.Equal(FoxRunQosPolicySerializationMigration.BridgeSerializationVersion, bridgeVersion);
            Assert.Equal(FoxRunQosProfile.SensorData, publish.Profile);
            Assert.Equal(FoxRunQosProfile.SystemDefault, subscribe.Profile);
            Assert.Equal(FoxRunQosProfile.SystemDefault, bridge.Profile);
        }

        [Fact]
        public void LegacyQosSettingsStillMigrateBeforeCurrentVersionIsMarked()
        {
            var policyVersion = FoxRunEncodingPolicyMigration.CurrentSerializationVersion;
            var bridgeVersion = 0;
            FoxRunQosProfileSettings publish = null;
            FoxRunQosProfileSettings subscribe = null;
            FoxRunQosProfileSettings bridge = null;

            FoxRunQosPolicySerializationMigration.MigrateNativeProfiles(
                ref policyVersion,
                ref publish,
                ref subscribe,
                legacyPublishPreset: 3,
                legacySubscribePreset: 4);
            FoxRunQosPolicySerializationMigration.MigrateBridgeProfile(
                ref bridgeVersion,
                ref bridge,
                legacyPreset: 3,
                legacyReliability: 1,
                legacyDurability: 1,
                legacyDepth: 37);

            Assert.Equal(FoxRunQosProfile.SensorData, publish.Profile);
            Assert.Equal(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.Reliable,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    1),
                subscribe.Resolve());
            Assert.Equal(
                new FoxRunResolvedQos(
                    FoxRunQosProfile.Default,
                    FoxRunQosReliability.BestEffort,
                    FoxRunQosDurability.TransientLocal,
                    FoxRunQosHistory.KeepLast,
                    37),
                bridge.Resolve());
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
            int nativeCopyBudgetBytes)
        {
            FoxRunEncodingPolicyMigration.Migrate(
                ref serializationVersion,
                legacyDefault,
                ref publishDefault,
                ref subscriptionDefault,
                ref providerDefault,
                ref nativeCopyBudgetBytes);
            return new MigrationResult(
                serializationVersion,
                publishDefault,
                subscriptionDefault,
                providerDefault,
                nativeCopyBudgetBytes);
        }

        private readonly struct MigrationResult
        {
            internal MigrationResult(
                int serializationVersion,
                FoxRunEncoding publishDefault,
                FoxRunEncoding subscriptionDefault,
                FoxRunEndpoint providerDefault,
                int nativeCopyBudgetBytes)
            {
                SerializationVersion = serializationVersion;
                PublishDefault = publishDefault;
                SubscriptionDefault = subscriptionDefault;
                ProviderDefault = providerDefault;
                NativeCopyBudgetBytes = nativeCopyBudgetBytes;
            }

            internal int SerializationVersion { get; }
            internal FoxRunEncoding PublishDefault { get; }
            internal FoxRunEncoding SubscriptionDefault { get; }
            internal FoxRunEndpoint ProviderDefault { get; }
            internal int NativeCopyBudgetBytes { get; }
        }
    }
}
