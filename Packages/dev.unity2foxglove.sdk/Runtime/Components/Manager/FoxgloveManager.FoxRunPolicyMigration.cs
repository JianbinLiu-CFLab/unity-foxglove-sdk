// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Player-safe migration from the legacy single FoxRun wire policy.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private int _foxRunPolicySerializationVersion;
        [SerializeField, HideInInspector] private int _coordinateTransportPolicySerializationVersion;

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            _coordinateTransportPolicySerializationVersion =
                CoordinateTransportPolicy.CurrentSerializationVersion;
            FoxRunQosPolicySerializationMigration.MarkCurrent(
                ref _foxRunPolicySerializationVersion,
                ref _ros2BridgeQosSerializationVersion);
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            CoordinateTransportPolicy.Migrate(
                ref _coordinateTransportPolicySerializationVersion,
                _coordinateMode,
                ref _outputCoordinateMode,
                ref _inputCoordinateMode);

#pragma warning disable CS0618
            FoxRunEncodingPolicyMigration.Migrate(
                ref _foxRunPolicySerializationVersion,
                _defaultFoxRunEncoding,
                ref _defaultFoxRunPublishEncoding,
                ref _defaultFoxRunSubscriptionEncoding,
                ref _defaultFoxRunSubscriptionSource,
                ref _foxRunRos2NativeCopyBudgetBytes);
            FoxRunQosPolicySerializationMigration.MigrateNativeProfiles(
                ref _foxRunPolicySerializationVersion,
                ref _defaultFoxRunNativePublishQos,
                ref _defaultFoxRunNativeSubscribeQos,
                _legacyDefaultFoxRunNativePublishRos2Qos,
                _legacyDefaultFoxRunRos2Qos);
            FoxRunQosPolicySerializationMigration.MigrateBridgeProfile(
                ref _ros2BridgeQosSerializationVersion,
                ref _ros2BridgeQos,
                _legacyRos2BridgeQosPreset,
                _legacyRos2BridgeCustomReliability,
                _legacyRos2BridgeCustomDurability,
                _legacyRos2BridgeCustomDepth);
#pragma warning restore CS0618
        }
    }
}
