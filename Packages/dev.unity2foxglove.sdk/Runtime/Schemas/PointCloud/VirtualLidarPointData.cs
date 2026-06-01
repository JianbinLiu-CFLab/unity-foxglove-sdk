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
        public float X;
        public float Y;
        public float Z;
        public float Intensity;
        public float Reflectivity;
        public float TimeOffsetSeconds;
        public ushort Ring;
        public byte IsValid;
    }
#pragma warning restore CS0649
}
