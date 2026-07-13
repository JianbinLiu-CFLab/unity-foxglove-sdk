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

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
#pragma warning disable CS0618
            FoxRunWireEncodingPolicyMigration.Migrate(
                ref _foxRunPolicySerializationVersion,
                _defaultFoxRunWireEncoding,
                ref _defaultFoxRunPublishEncoding,
                ref _defaultFoxRunSubscriptionEncoding);
#pragma warning restore CS0618
        }
    }
}
