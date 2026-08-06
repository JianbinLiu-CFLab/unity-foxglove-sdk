// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;

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

        /// <summary>Validates profile invariants before scan pattern creation.</summary>
        public bool Validate(out string error)
        {
            if (PixelsPerColumn <= 0)
            {
                error = "LiDAR profile PixelsPerColumn must be positive.";
                return false;
            }

            if (ColumnsPerFrame <= 0)
            {
                error = "LiDAR profile ColumnsPerFrame must be positive.";
                return false;
            }

            if (!IsFinite(ScanRateHz) || ScanRateHz <= 0)
            {
                error = "LiDAR profile ScanRateHz must be finite and positive.";
                return false;
            }

            if (!IsFinite(MinRangeMeters) || MinRangeMeters < 0)
            {
                error = "LiDAR profile MinRangeMeters must be finite and non-negative.";
                return false;
            }

            if (!IsFinite(LidarOriginToBeamOriginMeters))
            {
                error = "LiDAR profile LidarOriginToBeamOriginMeters must be finite.";
                return false;
            }

            if (BeamAltitudeAngles == null || BeamAltitudeAngles.Length != PixelsPerColumn)
            {
                error = "LiDAR profile BeamAltitudeAngles length must match PixelsPerColumn.";
                return false;
            }

            if (BeamAzimuthAngles == null || BeamAzimuthAngles.Length != PixelsPerColumn)
            {
                error = "LiDAR profile BeamAzimuthAngles length must match PixelsPerColumn.";
                return false;
            }

            for (var i = 0; i < PixelsPerColumn; i++)
            {
                if (!IsFinite(BeamAltitudeAngles[i]))
                {
                    error = $"LiDAR profile BeamAltitudeAngles[{i}] must be finite.";
                    return false;
                }

                if (!IsFinite(BeamAzimuthAngles[i]))
                {
                    error = $"LiDAR profile BeamAzimuthAngles[{i}] must be finite.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
