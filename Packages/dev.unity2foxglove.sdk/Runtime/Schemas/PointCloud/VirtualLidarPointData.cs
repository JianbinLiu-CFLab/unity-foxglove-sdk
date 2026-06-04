// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/PointCloud
// Purpose: Shared native LiDAR point snapshot record for Draco worker handoff.

namespace Unity.FoxgloveSDK.Schemas.PointCloud
{
    /// <summary>Unmanaged output record shared by VirtualLidar and Draco snapshot encoding.</summary>
#pragma warning disable CS0649
    internal struct VirtualLidarPointData
    {
        // XYZ is already in the scan-reference Foxglove frame expected by the
        // legacy native Draco and PointCloudFrame visualization paths.
        public float X;
        public float Y;
        public float Z;
        // Acquisition XYZ is expressed in the sensor frame at this point's
        // acquisition time. PointCloud2 Native uses it for raw SLAM input and
        // for motion-compensated visualization.
        public float AcquisitionX;
        public float AcquisitionY;
        public float AcquisitionZ;
        public float Intensity;
        public float Reflectivity;
        // Relative to scan start; ROS2 raw paths can convert this to absolute ns.
        public float TimeOffsetSeconds;
        public ushort Ring;
        // 0 keeps ray-slot layout for misses while letting the encoder compact valids.
        public byte IsValid;
        public byte HasAcquisitionFrame;
    }
#pragma warning restore CS0649
}
