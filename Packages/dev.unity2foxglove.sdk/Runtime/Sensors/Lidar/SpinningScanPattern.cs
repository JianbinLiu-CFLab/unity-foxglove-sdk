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
    /// When <c>columnStep</c> does not evenly divide <c>columns</c>, the final
    /// partial step is retained so the last physical column is not silently
    /// dropped from the scan.
    /// </summary>
    public class SpinningScanPattern : ILidarScanPattern
    {
        private readonly double[] _altRad;
        private readonly double[] _azmRad;
        private readonly int _columns;
        private readonly int _columnStep;
        private readonly double[] _sinAlt;
        private readonly double[] _cosAlt;
        private readonly double[] _sinRingAzm;
        private readonly double[] _cosRingAzm;
        private readonly double[] _sinColumnAzm;
        private readonly double[] _cosColumnAzm;
        private readonly int _effectiveColumns;

        public string ProductLine { get; }

        public double ScanRateHz { get; }

        public double MinRangeMeters { get; }

        public int RayCount { get; }
        /// <summary>Number of beam rings in the scan.</summary>
        public int Rings => _altRad.Length;

        /// <summary>
        /// Creates a spinning pattern from exact beam-angle arrays (metadata).
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
            _effectiveColumns = EffectiveColumnCount(_columns, _columnStep);
            _sinAlt = new double[_altRad.Length];
            _cosAlt = new double[_altRad.Length];
            _sinRingAzm = new double[_altRad.Length];
            _cosRingAzm = new double[_altRad.Length];
            for (var i = 0; i < _altRad.Length; i++)
            {
                _sinAlt[i] = Math.Sin(_altRad[i]);
                _cosAlt[i] = Math.Cos(_altRad[i]);
                var ringAzm = i < _azmRad.Length ? _azmRad[i] : 0d;
                _sinRingAzm[i] = Math.Sin(ringAzm);
                _cosRingAzm[i] = Math.Cos(ringAzm);
            }

            _sinColumnAzm = new double[_effectiveColumns];
            _cosColumnAzm = new double[_effectiveColumns];
            for (var i = 0; i < _effectiveColumns; i++)
            {
                var column = Math.Min(_columns - 1, i * _columnStep);
                var columnAzm = column * (2.0 * Math.PI) / _columns;
                _sinColumnAzm[i] = Math.Sin(columnAzm);
                _cosColumnAzm[i] = Math.Cos(columnAzm);
            }

            RayCount = _altRad.Length * _effectiveColumns;
        }

        /// <summary>
        /// Creates a spinning pattern from uniform FOV distribution (presets without exact angles).
        /// </summary>

        public static SpinningScanPattern FromUniformFov(string productLine, double scanRateHz, double minRangeMeters,
            int rings, int columns, int columnStep, double fovTopDeg, double fovBottomDeg)
        {
            var alt = UniformAngles(rings, fovTopDeg, fovBottomDeg);
            var azm = new double[rings]; // all zero for co-axial beams
            return new SpinningScanPattern(productLine, scanRateHz, minRangeMeters, columns, columnStep, alt, azm);
        }

        public bool TryGetRay(int index, int frameIndex,
            out Vector3 direction, out float timeOffset)
        {
            var rings = _altRad.Length;
            var columnSlot = index % _effectiveColumns;
            var ring = index / _effectiveColumns;
            var column = Math.Min(_columns - 1, columnSlot * _columnStep);

            if (ring < 0 || ring >= rings || column < 0 || column >= _columns)
            {
                direction = default;
                timeOffset = 0;
                return false;
            }

            // Column sweep: 360 degrees over columns_per_frame.
            // (column, ring) -> beam direction matched against the original
            // LidarRayGenerator (Phase 138 verified in Foxglove).
            var sinTotalAzm = _sinColumnAzm[columnSlot] * _cosRingAzm[ring]
                + _cosColumnAzm[columnSlot] * _sinRingAzm[ring];
            var cosTotalAzm = _cosColumnAzm[columnSlot] * _cosRingAzm[ring]
                - _sinColumnAzm[columnSlot] * _sinRingAzm[ring];

            // Sensor frame: x-right, y-up, z-forward (Unity left-handed).
            // Positive altitude -> beam points up (+Y). Azimuth sweeps CW
            // around +Y (column 0 forward, column N/4 = +X right).
            direction = new Vector3(
                (float)(_cosAlt[ring] * sinTotalAzm),
                (float)_sinAlt[ring],
                (float)(_cosAlt[ring] * cosTotalAzm));
            timeOffset = (float)column / _columns;
            return true;
        }

        private static int EffectiveColumnCount(int columns, int columnStep)
            => Math.Max(1, (columns + columnStep - 1) / columnStep);

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
