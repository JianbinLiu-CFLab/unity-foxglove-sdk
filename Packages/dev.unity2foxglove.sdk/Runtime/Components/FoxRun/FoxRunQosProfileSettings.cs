// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Serializable Manager-side ROS 2 QoS profile with optional policy overrides.

using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity.FoxgloveSDK.Components
{
    [Serializable]
    internal sealed class FoxRunQosProfileSettings
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosProfile _profile = FoxRunQosProfile.Default;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideReliability;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosReliability _reliability = FoxRunQosReliability.Reliable;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideDurability;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosDurability _durability = FoxRunQosDurability.Volatile;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideHistory;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private FoxRunQosHistory _history = FoxRunQosHistory.KeepLast;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        private bool _overrideDepth;
#if UNITY_5_3_OR_NEWER
        [SerializeField, Min(1)]
#endif
        private int _depth = 10;

        internal FoxRunQosProfile Profile
        {
            get => _profile;
            set => _profile = value;
        }

        internal FoxRunResolvedQos Resolve()
        {
            var resolution = FoxRunRos2QosProfileResolver.Resolve(
                _profile,
                hasProfile: true,
                _reliability,
                _overrideReliability,
                _durability,
                _overrideDurability,
                _history,
                _overrideHistory,
                _depth,
                _overrideDepth,
                FoxRunResolvedQos.Default);
            if (!resolution.Success)
            {
                throw new InvalidOperationException(
                    "FoxRun Manager ROS 2 QoS is invalid: " + resolution.DiagnosticMessage);
            }

            return resolution.Qos;
        }

        internal void MigrateLegacyPreset(int legacyPreset)
        {
            ResetToDefault();

            switch (legacyPreset)
            {
                case 3:
                    _profile = FoxRunQosProfile.SensorData;
                    return;
                case 4:
                    _profile = FoxRunQosProfile.Default;
                    _overrideDurability = true;
                    _durability = FoxRunQosDurability.TransientLocal;
                    _overrideDepth = true;
                    _depth = 1;
                    return;
                default:
                    _profile = FoxRunQosProfile.Default;
                    return;
            }
        }

        internal void MigrateLegacyBridgePreset(
            int legacyPreset,
            int legacyReliability,
            int legacyDurability,
            int legacyDepth)
        {
            ResetToDefault();
            switch (legacyPreset)
            {
                case 1:
                    _profile = FoxRunQosProfile.SensorData;
                    return;
                case 2:
                    _overrideDurability = true;
                    _durability = FoxRunQosDurability.TransientLocal;
                    _overrideDepth = true;
                    _depth = 1;
                    return;
                case 3:
                    _overrideReliability = true;
                    _reliability = legacyReliability == 1
                        ? FoxRunQosReliability.BestEffort
                        : FoxRunQosReliability.Reliable;
                    _overrideDurability = true;
                    _durability = legacyDurability == 1
                        ? FoxRunQosDurability.TransientLocal
                        : FoxRunQosDurability.Volatile;
                    _overrideDepth = true;
                    _depth = legacyDepth > 0 ? legacyDepth : 1;
                    return;
                default:
                    return;
            }
        }

        private void ResetToDefault()
        {
            _profile = FoxRunQosProfile.Default;
            _overrideReliability = false;
            _overrideDurability = false;
            _overrideHistory = false;
            _overrideDepth = false;
            _reliability = FoxRunQosReliability.Reliable;
            _durability = FoxRunQosDurability.Volatile;
            _history = FoxRunQosHistory.KeepLast;
            _depth = 10;
        }
    }
}
