// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>Shared admission limits for the int-indexed LiDAR geometry APIs.</summary>
    internal static class LidarGeometryLimits
    {
        internal const int MaxRings = 4096;
        internal const int MaxColumns = 1_048_576;
        internal const long MaxRayCount = 16L * 1024L * 1024L;

        internal static bool TryValidate(int rings, int columns, out string error)
        {
            if (rings <= 0 || rings > MaxRings)
            {
                error = $"LiDAR ring count must be in the range 1..{MaxRings}.";
                return false;
            }

            if (columns <= 0 || columns > MaxColumns)
            {
                error = $"LiDAR column count must be in the range 1..{MaxColumns}.";
                return false;
            }

            if ((long)rings * columns > MaxRayCount)
            {
                error = $"LiDAR geometry exceeds the {MaxRayCount}-ray limit.";
                return false;
            }

            error = null;
            return true;
        }

        internal static void ValidatePatternScalars(double scanRateHz, double minRangeMeters)
        {
            if (!IsFinite(scanRateHz) || scanRateHz <= 0d)
                throw new ArgumentOutOfRangeException(nameof(scanRateHz), "LiDAR scan rate must be finite and positive.");

            if (!IsFinite(minRangeMeters) || minRangeMeters < 0d)
                throw new ArgumentOutOfRangeException(nameof(minRangeMeters), "LiDAR minimum range must be finite and non-negative.");
        }

        internal static void ValidateAngles(double[] altitudeRad, double[] azimuthRad)
        {
            if (altitudeRad == null)
                throw new ArgumentNullException(nameof(altitudeRad));
            if (azimuthRad == null)
                throw new ArgumentNullException(nameof(azimuthRad));
            if (altitudeRad.Length == 0 || altitudeRad.Length != azimuthRad.Length)
                throw new ArgumentException("LiDAR altitude and azimuth arrays must be non-empty and have equal lengths.");

            for (var i = 0; i < altitudeRad.Length; i++)
            {
                if (!IsFinite(altitudeRad[i]) || !IsFinite(azimuthRad[i]))
                    throw new ArgumentException($"LiDAR beam angle at ring {i} must be finite.");
            }
        }

        internal static void ThrowIfInvalid(int rings, int columns)
        {
            if (!TryValidate(rings, columns, out var error))
                throw new ArgumentOutOfRangeException(nameof(columns), error);
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
