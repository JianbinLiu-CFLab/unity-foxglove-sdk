// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Generates deterministic ray directions from a LidarProfile.
    /// Uses Unity Vector3 for direct compatibility with Physics.Raycast.
    /// </summary>
    public class LidarRayGenerator
    {
        private readonly LidarProfile _profile;
        private readonly int _columnStep;

        public LidarRayGenerator(LidarProfile profile, int columnStep = 1)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _columnStep = Math.Max(1, columnStep);
        }

        /// <summary>Total number of rays for the configured profile and column step.</summary>
        public int RayCount => _profile.PixelsPerColumn * (_profile.ColumnsPerFrame / _columnStep);

        /// <summary>
        /// Get a unit-length ray direction in sensor-local space and normalized time offset.
        /// </summary>
        /// <param name="column">Column index [0, ColumnsPerFrame).</param>
        /// <param name="ring">Ring index [0, PixelsPerColumn).</param>
        /// <param name="direction">Unit direction in sensor-local space (x-right, y-up, z-forward).</param>
        /// <param name="timeOffset">Normalized time offset [0..1) within the scan.</param>
        /// <returns>true if the indices are valid.</returns>
        public bool TryGetRay(int column, int ring, out Vector3 direction, out float timeOffset)
        {
            if (column < 0 || column >= _profile.ColumnsPerFrame || ring < 0 || ring >= _profile.PixelsPerColumn)
            {
                direction = default;
                timeOffset = 0;
                return false;
            }

            var alt = _profile.BeamAltitudeAngles[ring];
            var azm = _profile.BeamAzimuthAngles[ring];

            // Sensor frame: x-right, y-up, z-forward
            // Azimuth rotation is around the y-axis (positive = rightward)
            direction = new Vector3(
                (float)(Math.Cos(alt) * Math.Sin(azm)),
                (float)(-Math.Sin(alt)),
                (float)(Math.Cos(alt) * Math.Cos(azm))
            );

            timeOffset = (float)column / _profile.ColumnsPerFrame;
            return true;
        }
    }
}
