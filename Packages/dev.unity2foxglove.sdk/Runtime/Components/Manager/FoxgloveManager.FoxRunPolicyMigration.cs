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
                ref _defaultFoxRunRos2Qos,
                ref _foxRunRos2NativeCopyBudgetBytes);
#pragma warning restore CS0618
        }
    }
}
