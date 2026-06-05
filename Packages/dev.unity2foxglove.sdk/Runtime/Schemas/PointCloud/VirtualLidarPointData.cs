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
        /// <summary>X coordinate in the scan-reference Foxglove frame.</summary>
        public float X;

        /// <summary>Y coordinate in the scan-reference Foxglove frame.</summary>
        public float Y;

        /// <summary>Z coordinate in the scan-reference Foxglove frame.</summary>
        public float Z;

        /// <summary>X coordinate in the sensor frame at this point's acquisition time.</summary>
        public float AcquisitionX;

        /// <summary>Y coordinate in the sensor frame at this point's acquisition time.</summary>
        public float AcquisitionY;

        /// <summary>Z coordinate in the sensor frame at this point's acquisition time.</summary>
        public float AcquisitionZ;

        /// <summary>Synthetic or source-provided intensity value.</summary>
        public float Intensity;

        /// <summary>Synthetic or source-provided reflectivity value.</summary>
        public float Reflectivity;

        /// <summary>Per-point acquisition time offset, in seconds relative to scan start.</summary>
        public float TimeOffsetSeconds;

        /// <summary>LiDAR ring index for organized scan consumers.</summary>
        public ushort Ring;

        /// <summary>Nonzero when this ray slot contains a valid hit.</summary>
        public byte IsValid;

        /// <summary>Nonzero when acquisition-frame coordinates are available.</summary>
        public byte HasAcquisitionFrame;
    }
#pragma warning restore CS0649
}
