// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native
// Purpose: R2FU-owned ordinary native-output admission policy.

using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Resolves ordinary R2FU output without retaining a ROS-specific setting
    /// in the core SDK. Standalone R2FU scenes remain enabled; Manager-owned
    /// scenes require the local R2FU Provider companion to be active.
    ///
    /// <para>This property must be queried from the Unity main thread because
    /// its fallback path performs Unity object discovery.</para>
    /// </summary>
    public static class Ros2NativeOutputPolicy
    {
        private static FoxgloveManager _manager;
        private static FoxRunRos2TransportProvider _provider;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _manager = null;
            _provider = null;
        }

        /// <summary>
        /// True for standalone R2FU scenes, or when the Manager's local R2FU
        /// Provider companion is present and active.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (_manager == null)
                {
                    _manager =
                        Object.FindFirstObjectByType<FoxgloveManager>();
                    _provider = null;
                }

                if (_manager == null)
                    return true;

                if (_provider == null)
                {
                    _provider =
                        _manager.GetComponent<
                            FoxRunRos2TransportProvider>();
                }

                return _provider != null
                       && _provider.isActiveAndEnabled;
            }
        }
    }
}
