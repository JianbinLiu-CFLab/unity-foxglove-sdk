// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>LiDAR vendor identifier.</summary>
    public enum LidarVendor
    {
        Ouster = 0,
        Velodyne = 1,
        RoboSense = 2,
        Livox = 3,
        Custom = 4,
    }

    /// <summary>LiDAR scan pattern family.</summary>
    public enum LidarScanKind
    {
        Spinning = 0,
        NonRepetitive = 1,
    }
}
