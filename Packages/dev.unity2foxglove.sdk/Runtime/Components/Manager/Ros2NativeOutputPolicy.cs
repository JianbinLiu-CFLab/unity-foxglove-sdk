// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Central resolution of the Output Mode "ROS2 Native (R2FU)" policy flag.

using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Single source of truth for the Output Mode "ROS2 Native (R2FU)" toggle.
    /// R2FU components query <see cref="Enabled"/> instead of each re-implementing
    /// the FoxgloveManager lookup. Returns true when no manager is present so the
    /// R2FU samples still work standalone.
    /// </summary>
    public static class Ros2NativeOutputPolicy
    {
        private static FoxgloveManager _manager;

        /// <summary>True when R2FU native DDS output is enabled (or no manager exists).</summary>
        public static bool Enabled
        {
            get
            {
                // Unity's overloaded == treats a destroyed manager as null, so this
                // re-resolves after a scene reload without caching a dead reference.
                if (_manager == null)
                    _manager = Object.FindFirstObjectByType<FoxgloveManager>();
                return _manager == null || _manager.Ros2NativeEnabled;
            }
        }
    }
}
