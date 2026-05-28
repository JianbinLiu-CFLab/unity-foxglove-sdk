// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Describes a LiDAR sensor's scan geometry — ring count, column spacing,
    /// beam angles, scan rate, and range limits.
    /// </summary>
    public class LidarProfile
    {
        /// <summary>Product line identifier, e.g. "OS-1".</summary>
        public string ProductLine;

        /// <summary>Lidar operating mode, e.g. "1024x10".</summary>
        public string LidarMode;

        /// <summary>Number of rings / beams per column.</summary>
        public int PixelsPerColumn;

        /// <summary>Number of columns per full rotation.</summary>
        public int ColumnsPerFrame;

        /// <summary>Number of columns per UDP packet (informational).</summary>
        public int ColumnsPerPacket;

        /// <summary>Scan rate in Hz.</summary>
        public double ScanRateHz;

        /// <summary>Minimum valid range in meters.</summary>
        public double MinRangeMeters;

        /// <summary>Offset from sensor center to beam origin in meters.</summary>
        public double LidarOriginToBeamOriginMeters;

        /// <summary>Altitude (elevation) angle per ring, in radians.</summary>
        public double[] BeamAltitudeAngles;

        /// <summary>Azimuth angle per ring, in radians. Same length as altitude.</summary>
        public double[] BeamAzimuthAngles;
    }
}
