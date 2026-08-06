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
            _foxRunPolicySerializationVersion =
                FoxRunEncodingPolicyMigration.CurrentSerializationVersion;
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            CoordinateTransportPolicy.Migrate(
                ref _coordinateTransportPolicySerializationVersion,
                _coordinateMode,
                ref _outputCoordinateMode,
                ref _inputCoordinateMode);

            // This hidden field is obsolete for normal callers but remains the
            // serialized source of truth for upgrading pre-directional assets.
#pragma warning disable CS0618
            var legacyFoxRunEncoding = _defaultFoxRunEncoding;
#pragma warning restore CS0618
            FoxRunEncodingPolicyMigration.Migrate(
                ref _foxRunPolicySerializationVersion,
                legacyFoxRunEncoding,
                ref _defaultFoxRunPublishEncoding,
                ref _defaultFoxRunSubscriptionEncoding);
        }
    }
}
