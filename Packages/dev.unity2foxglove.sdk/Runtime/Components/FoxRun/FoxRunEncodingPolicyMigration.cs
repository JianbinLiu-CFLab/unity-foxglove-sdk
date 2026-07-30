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
        internal const int CurrentSerializationVersion =
            DirectionalSerializationVersion;

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

    }
}
