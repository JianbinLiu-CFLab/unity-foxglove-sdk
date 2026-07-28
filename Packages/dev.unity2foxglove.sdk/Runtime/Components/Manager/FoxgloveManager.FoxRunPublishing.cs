// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Directional FoxRun publish wire policy.

using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField] private FoxRunEncoding _defaultFoxRunPublishEncoding = FoxRunEncoding.Protobuf;
        [FormerlySerializedAs("_defaultFoxRunNativePublishRos2Qos")]
        [SerializeField, HideInInspector] private int _legacyDefaultFoxRunNativePublishRos2Qos = 1;
        [SerializeField] private FoxRunQosProfileSettings _defaultFoxRunNativePublishQos = new();
        private readonly FoxRunPublishSessionState _foxRunPublishSessionState = new();

        /// <summary>Current immutable publish-profile snapshot.</summary>
        public FoxRunPublishSessionPolicy ActiveFoxRunPublishSessionPolicy =>
            _foxRunPublishSessionState.Current;

        /// <summary>
        /// Raised synchronously after the immutable publish session begins or
        /// ends. Optional publisher providers use this to stop owned endpoints
        /// before the Manager disable transition completes.
        /// </summary>
        public event Action<FoxRunPublishSessionPolicy> FoxRunPublishSessionChanged;

        /// <summary>
        /// Effective default targets used by inherited Publish contracts.
        /// These are the Manager Publish Destinations; assigning this
        /// compatibility property updates the same three destination switches.
        /// </summary>
        public FoxRunEndpoint DefaultFoxRunPublishTargets
        {
            get => FoxRunPublishTargetPolicy.FromPublishDestinations(
                _foxgloveOutputEnabled,
                _ros2NativeEnabled,
                _ros2BridgeEnabled);
            set
            {
                var targets = FoxRunEndpointResolver.ValidateProfileTargets(value);
                _foxgloveOutputEnabled =
                    (targets & FoxRunEndpoint.Foxglove) != 0;
                _ros2NativeEnabled =
                    (targets & FoxRunEndpoint.Ros2Native) != 0;
                _ros2BridgeEnabled =
                    (targets & FoxRunEndpoint.Ros2Bridge) != 0;
            }
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

        /// <summary>
        /// Effective Bridge publish QoS for the active Manager lifetime.
        /// Without an active session, this exposes the configured next-session value.
        /// </summary>
        public FoxRunResolvedQos ActiveFoxRunBridgePublishQos =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.BridgeRos2Qos
                : ResolveConfiguredRos2BridgeQos();

        /// <summary>Resolved native publish QoS default.</summary>
        public FoxRunResolvedQos DefaultFoxRunNativePublishQos
        {
            get
            {
                _defaultFoxRunNativePublishQos ??= new FoxRunQosProfileSettings();
                return _defaultFoxRunNativePublishQos.Resolve();
            }
        }

        /// <summary>
        /// Effective Native ROS 2 publish QoS for the active Manager lifetime.
        /// Inspector edits configure the next session and cannot mutate an
        /// already resolved target contract.
        /// </summary>
        public FoxRunResolvedQos ActiveFoxRunNativePublishQos =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy.NativeRos2Qos
                : DefaultFoxRunNativePublishQos;

        internal void BeginFoxRunPublishSessionIfNeeded()
        {
            if (_foxRunPublishSessionState.Current.SessionActive)
                return;

            var policy = _foxRunPublishSessionState.BeginIfNeeded(
                DefaultFoxRunPublishTargets,
                DefaultFoxRunPublishEncoding,
                DefaultPublishRateHz,
                DefaultFoxRunNativePublishQos,
                ResolveConfiguredRos2BridgeQos());
            NotifyFoxRunPublishSessionChanged(policy);
        }

        internal void EndFoxRunPublishSession()
        {
            try
            {
                if (_foxRunPublishSessionState.Current.SessionActive)
                {
                    var policy = _foxRunPublishSessionState.End();
                    NotifyFoxRunPublishSessionChanged(policy);
                }
            }
            finally
            {
                ReleaseFoxRunRos2BridgeRuntimeDemand();
            }
        }

        private void NotifyFoxRunPublishSessionChanged(
            FoxRunPublishSessionPolicy policy)
        {
            var handlers = FoxRunPublishSessionChanged;
            if (handlers == null)
                return;

            foreach (var subscriber in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<FoxRunPublishSessionPolicy>)subscriber)(policy);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
