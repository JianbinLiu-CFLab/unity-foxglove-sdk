// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: External coordinate convention used at a Manager-owned transport boundary.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Coordinate convention of the external representation at one transport
    /// boundary. Numeric values are serialized and therefore frozen.
    /// </summary>
    public enum CoordinateMode
    {
        /// <summary>Unity-native left-handed coordinates with X right, Y up, and Z forward.</summary>
        LeftHand = 0,

        /// <summary>ROS/Foxglove-style right-handed coordinates with X forward, Y left, and Z up.</summary>
        RightHand = 1
    }
}
