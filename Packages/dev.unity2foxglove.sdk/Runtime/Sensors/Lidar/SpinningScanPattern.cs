// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Spinning 360-degree LiDAR scan pattern (Ouster, Velodyne, RoboSense).
    /// Rays are indexed by (column, ring) pairs, derived from beam-angle arrays
    /// or a uniform FOV distribution.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public class SpinningScanPattern : ILidarScanPattern
    {
        private readonly double[] _altRad;
        private readonly double[] _azmRad;
        private readonly int _columns;
        private readonly int _columnStep;

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public string ProductLine { get; }
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public double ScanRateHz { get; }
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public double MinRangeMeters { get; }
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public int RayCount { get; }
        /// <summary>Number of beam rings in the scan.</summary>
        public int Rings => _altRad.Length;

        /// <summary>
        /// Creates a spinning pattern from exact beam-angle arrays (metadata).
        /// </summary>
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public SpinningScanPattern(string productLine, double scanRateHz, double minRangeMeters,
            int columns, int columnStep, double[] altitudeRad, double[] azimuthRad)
        {
            ProductLine = productLine;
            ScanRateHz = scanRateHz;
            MinRangeMeters = minRangeMeters;
            _columns = Math.Max(1, columns);
            _columnStep = Math.Max(1, columnStep);
            _altRad = altitudeRad ?? throw new ArgumentNullException(nameof(altitudeRad));
            _azmRad = azimuthRad ?? throw new ArgumentNullException(nameof(azimuthRad));
            RayCount = _altRad.Length * (_columns / _columnStep);
        }

        /// <summary>
        /// Creates a spinning pattern from uniform FOV distribution (presets without exact angles).
        /// </summary>
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public static SpinningScanPattern FromUniformFov(string productLine, double scanRateHz, double minRangeMeters,
            int rings, int columns, int columnStep, double fovTopDeg, double fovBottomDeg)
        {
            var alt = UniformAngles(rings, fovTopDeg, fovBottomDeg);
            var azm = new double[rings]; // all zero for co-axial beams
            return new SpinningScanPattern(productLine, scanRateHz, minRangeMeters, columns, columnStep, alt, azm);
        }

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public bool TryGetRay(int index, int frameIndex,
            out Vector3 direction, out float timeOffset)
        {
            var rings = _altRad.Length;
            var effectiveColumns = _columns / _columnStep;
            var ring = index / effectiveColumns;
            var column = (index % effectiveColumns) * _columnStep;

            if (ring < 0 || ring >= rings || column < 0 || column >= _columns)
            {
                direction = default;
                timeOffset = 0;
                return false;
            }

            var alt = _altRad[ring];
            var ringAzm = _azmRad[ring];

            // Column sweep: 360 degrees over columns_per_frame.
            // (column, ring) 鈫?beam direction matched against the original
            // LidarRayGenerator (Phase 138 verified in Foxglove).
            var columnAzm = column * (2.0 * Math.PI) / _columns;
            var totalAzm = columnAzm + ringAzm;

            // Sensor frame: x-right, y-up, z-forward (Unity left-handed).
            // Positive altitude 鈫?beam points up (+Y). Azimuth sweeps CW
            // around +Y (column 0 forward, column N/4 = +X right).
            direction = new Vector3(
                (float)(Math.Cos(alt) * Math.Sin(totalAzm)),
                (float)(Math.Sin(alt)),
                (float)(Math.Cos(alt) * Math.Cos(totalAzm)));
            timeOffset = (float)column / _columns;
            return true;
        }

        private static double[] UniformAngles(int count, double topDeg, double bottomDeg)
        {
            var result = new double[count];
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.5 : (double)i / (count - 1);
                result[i] = (topDeg + t * (bottomDeg - topDeg)) * Math.PI / 180.0;
            }
            return result;
        }
    }
}

