// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Player-safe serialized FoxRun directional-policy migration.

namespace Unity.FoxgloveSDK.Components
{
    internal static class FoxRunEncodingPolicyMigration
    {
        private const int DirectionalSerializationVersion = 1;
        internal const int CurrentSerializationVersion = 2;
        internal const int MinRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.MinBytes;
        internal const int MaxRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.MaxBytes;
        internal const int DefaultRos2NativeCopyBudgetBytes = FoxRunRos2NativeCopyBudgetPolicy.DefaultBytes;

        /// <summary>
        /// Copies the former shared default into both directional defaults once.
        /// This method is Unity-API-free so Manager deserialization can use it in Editor and players.
        /// </summary>
        public static void Migrate(
            ref int serializationVersion,
            FoxRunEncoding legacyDefault,
            ref FoxRunEncoding publishDefault,
            ref FoxRunEncoding subscriptionDefault)
        {
            if (serializationVersion >= DirectionalSerializationVersion)
                return;

            // Unity can invoke this deserialization callback off the main thread, so this
            // player-safe migration must recover without logging or throwing. Old Inherit
            // and malformed enum values both fall back to the historic safe default.
            var concreteLegacyDefault = legacyDefault == FoxRunEncoding.JSON
                ? FoxRunEncoding.JSON
                : FoxRunEncoding.Protobuf;
            publishDefault = concreteLegacyDefault;
            subscriptionDefault = concreteLegacyDefault;
            serializationVersion = DirectionalSerializationVersion;
        }

        /// <summary>
        /// Adds subscription source, ROS2 QoS, and native copy-budget policy
        /// while preserving the directional encoding migration.
        /// </summary>
        public static void Migrate(
            ref int serializationVersion,
            FoxRunEncoding legacyDefault,
            ref FoxRunEncoding publishDefault,
            ref FoxRunEncoding subscriptionDefault,
            ref FoxRunEndpoint sourceDefault,
            ref FoxRunRos2QosPreset qosDefault,
            ref int nativeCopyBudgetBytes)
        {
            Migrate(
                ref serializationVersion,
                legacyDefault,
                ref publishDefault,
                ref subscriptionDefault);

            if (serializationVersion < CurrentSerializationVersion)
            {
                sourceDefault = FoxRunEndpoint.Foxglove;
                qosDefault = FoxRunRos2QosPreset.Default;
                nativeCopyBudgetBytes = DefaultRos2NativeCopyBudgetBytes;
                serializationVersion = CurrentSerializationVersion;
                return;
            }

            sourceDefault = NormalizeSubscriptionSource(sourceDefault);
            qosDefault = NormalizeRos2Qos(qosDefault);
            nativeCopyBudgetBytes = NormalizeRos2NativeCopyBudgetBytes(nativeCopyBudgetBytes);
        }

        /// <summary>Defaults missing budgets and clamps positive values to the portable range.</summary>
        public static int NormalizeRos2NativeCopyBudgetBytes(int configuredBytes)
            => FoxRunRos2NativeCopyBudgetPolicy.NormalizeSerializedBytes(configuredBytes);

        private static FoxRunEndpoint NormalizeSubscriptionSource(
            FoxRunEndpoint source)
            => source == FoxRunEndpoint.Ros2Native
                ? FoxRunEndpoint.Ros2Native
                : FoxRunEndpoint.Foxglove;

        private static FoxRunRos2QosPreset NormalizeRos2Qos(FoxRunRos2QosPreset qos)
            => FoxRunRos2QosResolver.NormalizeSerializedManagerDefault(qos);
    }
}
