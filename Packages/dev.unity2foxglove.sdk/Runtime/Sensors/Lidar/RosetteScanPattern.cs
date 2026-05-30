// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Non-repetitive Livox-style scan pattern using a Lissajous sweep.
    /// Coverage fills in across frames via a golden-angle per-frame rotation.
    /// This is a coverage approximation, not an exact Livox trajectory.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public class RosetteScanPattern : ILidarScanPattern
    {
        private const double GoldenAngleDeg = 137.50776405;

        private readonly double _fovHDeg, _fovVDeg;
        private readonly int _beamsPerFrame;
        private readonly double _loopsPerFrame = 3.2;
        private readonly double _a = 7.0, _b = 11.0; // incommensurate Lissajous coefficients

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

        public int RayCount => _beamsPerFrame;

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public RosetteScanPattern(string productLine, double scanRateHz, double minRangeMeters,
            double fovHDeg, double fovVDeg, int beamsPerFrame)
        {
            ProductLine = productLine;
            ScanRateHz = scanRateHz;
            MinRangeMeters = minRangeMeters;
            _fovHDeg = fovHDeg;
            _fovVDeg = fovVDeg;
            _beamsPerFrame = Math.Max(1, beamsPerFrame);
        }

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public bool TryGetRay(int index, int frameIndex,
            out Vector3 direction, out float timeOffset)
        {
            if (index < 0 || index >= _beamsPerFrame)
            {
                direction = default;
                timeOffset = 0;
                return false;
            }

            var tau = (double)index / _beamsPerFrame * 2.0 * Math.PI * _loopsPerFrame;
            var phi = frameIndex * GoldenAngleDeg * Math.PI / 180.0;

            var azDeg = (_fovHDeg / 2.0) * Math.Sin(_a * tau + phi);
            var elDeg = (_fovVDeg / 2.0) * Math.Sin(_b * tau);

            var azRad = azDeg * Math.PI / 180.0;
            var elRad = elDeg * Math.PI / 180.0;

            direction = new Vector3(
                (float)(Math.Cos(elRad) * Math.Sin(-azRad)),
                (float)(-Math.Sin(elRad)),
                (float)(Math.Cos(elRad) * Math.Cos(azRad)));

            timeOffset = (float)index / _beamsPerFrame;
            return true;
        }
    }
}

