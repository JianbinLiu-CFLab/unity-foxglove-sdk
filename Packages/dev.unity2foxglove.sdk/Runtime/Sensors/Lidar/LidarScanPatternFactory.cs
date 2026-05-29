// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Linq;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Creates an ILidarScanPattern from a model spec, parsed metadata, or custom parameters.
    /// </summary>
    public static class LidarScanPatternFactory
    {
        /// <summary>Creates a scan pattern from a preset model spec.</summary>
        public static ILidarScanPattern Create(LidarModelSpec spec, string mode, int columnStep)
        {
            return spec.Kind switch
            {
                LidarScanKind.Spinning => CreateSpinning(spec, columnStep),
                LidarScanKind.NonRepetitive => CreateRosette(spec),
                _ => throw new ArgumentException($"Unknown scan kind: {spec.Kind}"),
            };
        }

        private static SpinningScanPattern CreateSpinning(LidarModelSpec spec, int columnStep)
        {
            if (spec.BeamAltitudeAnglesDeg != null)
            {
                var alt = spec.BeamAltitudeAnglesDeg.Select(d => d * Math.PI / 180.0).ToArray();
                var azm = new double[spec.Rings]; // co-axial: all zero
                return new SpinningScanPattern(spec.Model, spec.RateHz, spec.MinRangeMeters,
                    spec.Columns, columnStep, alt, azm);
            }
            return SpinningScanPattern.FromUniformFov(spec.Model, spec.RateHz, spec.MinRangeMeters,
                spec.Rings, spec.Columns, columnStep, spec.FovTopDeg, spec.FovBottomDeg);
        }

        private static RosetteScanPattern CreateRosette(LidarModelSpec spec)
            => new(spec.Model, spec.RateHz, spec.MinRangeMeters,
                spec.FovHDeg, spec.FovVDeg, spec.BeamsPerFrame);

        /// <summary>Creates a spinning pattern from parsed LidarProfile metadata.</summary>
        public static ILidarScanPattern FromProfile(LidarProfile profile, int columnStep)
        {
            return new SpinningScanPattern(profile.ProductLine, profile.ScanRateHz,
                profile.MinRangeMeters, profile.ColumnsPerFrame, columnStep,
                profile.BeamAltitudeAngles, profile.BeamAzimuthAngles);
        }
    }
}
