// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Directional FoxRun publish wire policy.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField] private FoxRunEndpoint _defaultFoxRunPublishTargets = FoxRunEndpoint.Foxglove;
        [SerializeField] private FoxRunEncoding _defaultFoxRunPublishEncoding = FoxRunEncoding.Protobuf;
        [SerializeField] private FoxRunRos2QosPreset _defaultFoxRunNativePublishRos2Qos =
            FoxRunRos2QosPreset.Default;
        private readonly FoxRunPublishSessionState _foxRunPublishSessionState = new();

        /// <summary>Current immutable publish-profile snapshot.</summary>
        public FoxRunPublishSessionPolicy ActiveFoxRunPublishSessionPolicy =>
            _foxRunPublishSessionState.Current;

        /// <summary>Serialized default targets used by inherited Publish contracts.</summary>
        public FoxRunEndpoint DefaultFoxRunPublishTargets
        {
            get => FoxRunEndpointResolver.ValidateProfileTargets(_defaultFoxRunPublishTargets);
            set => _defaultFoxRunPublishTargets =
                FoxRunEndpointResolver.ValidateProfileTargets(value);
        }

        /// <summary>Serialized default used by inherited Publish contracts.</summary>
        public FoxRunEncoding DefaultFoxRunPublishEncoding
        {
            get => _defaultFoxRunPublishEncoding == (FoxRunEncoding)0
                ? FoxRunEncoding.Protobuf
                : FoxRunEncodingResolver.ValidateProfileDefault(_defaultFoxRunPublishEncoding);
            set => _defaultFoxRunPublishEncoding = FoxRunEncodingResolver.ValidateProfileDefault(value);
        }

        /// <summary>Effective publish targets for the active Manager lifetime.</summary>
        public FoxRunEndpoint ActiveFoxRunPublishTargets =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.DefaultTargets
                : DefaultFoxRunPublishTargets;

        /// <summary>Effective publish encoding for the active Manager lifetime.</summary>
        public FoxRunEncoding ActiveFoxRunPublishEncoding =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.FoxgloveEncoding
                : DefaultFoxRunPublishEncoding;

        /// <summary>Effective default cadence for the active Manager lifetime.</summary>
        public float ActiveFoxRunDefaultPublishRateHz =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.DefaultPublishRateHz
                : DefaultPublishRateHz;

        /// <summary>Serialized native publish QoS default retained until Phase184-C resolves the official profile.</summary>
        public FoxRunRos2QosPreset DefaultFoxRunNativePublishRos2Qos
        {
            get => FoxRunRos2QosResolver.NormalizeManagerDefault(
                _defaultFoxRunNativePublishRos2Qos);
            set => _defaultFoxRunNativePublishRos2Qos =
                FoxRunRos2QosResolver.NormalizeManagerDefault(value);
        }

        internal void BeginFoxRunPublishSessionIfNeeded()
        {
            _foxRunPublishSessionState.BeginIfNeeded(
                DefaultFoxRunPublishTargets,
                DefaultFoxRunPublishEncoding,
                DefaultPublishRateHz,
                DefaultFoxRunNativePublishRos2Qos,
                ResolveRos2BridgeQos());
        }

        internal void EndFoxRunPublishSession()
        {
            _foxRunPublishSessionState.End();
        }
    }
}
