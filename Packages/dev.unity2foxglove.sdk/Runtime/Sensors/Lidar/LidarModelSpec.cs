// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Declares a known LiDAR model with preset parameters.
    /// Used by LidarModelRegistry to populate the Inspector dropdown.
    /// </summary>
    public sealed class LidarModelSpec
    {
        public readonly LidarVendor Vendor;
        public readonly string Model;              // e.g. "OS-1-32", "VLP-16"
        public readonly LidarScanKind Kind;

        // Spinning
        public readonly int Rings;
        public readonly int Columns;
        public readonly double RateHz;
        public readonly double FovTopDeg, FovBottomDeg;
        public readonly double[] BeamAltitudeAnglesDeg; // null -> uniform distribution
        public readonly string[] Modes;                  // null or e.g. {"1024x10", "2048x10"}

        // Non-repetitive (Livox)
        public readonly double FovHDeg, FovVDeg;
        public readonly int BeamsPerFrame;

        // Shared
        public readonly double MinRangeMeters, MaxRangeMeters;

        public LidarModelSpec(LidarVendor vendor, string model, LidarScanKind kind,
            int rings, int columns, double rateHz, double fovTopDeg, double fovBottomDeg,
            double[] beamAltitudeAnglesDeg, string[] modes,
            double fovHDeg, double fovVDeg, int beamsPerFrame,
            double minRangeMeters, double maxRangeMeters)
        {
            Vendor = vendor;
            Model = model;
            Kind = kind;
            Rings = rings;
            Columns = columns;
            RateHz = rateHz;
            FovTopDeg = fovTopDeg;
            FovBottomDeg = fovBottomDeg;
            BeamAltitudeAnglesDeg = beamAltitudeAnglesDeg;
            Modes = modes;
            FovHDeg = fovHDeg;
            FovVDeg = fovVDeg;
            BeamsPerFrame = beamsPerFrame;
            MinRangeMeters = minRangeMeters;
            MaxRangeMeters = maxRangeMeters;
        }

        // Factory helpers
        public static LidarModelSpec Ouster(string model, int rings, int columns, string[] modes,
            double fovTopDeg, double fovBottomDeg, double[] beamAltDeg = null,
            double minRange = 0.5, double maxRange = 120)
            => new(LidarVendor.Ouster, model, LidarScanKind.Spinning,
                rings, columns, 10.0, fovTopDeg, fovBottomDeg,
                beamAltDeg, modes, 0, 0, 0, minRange, maxRange);

        public static LidarModelSpec Velodyne(string model, int rings, int columns, double rpm,
            double[] beamAltDeg, double minRange = 0.3, double maxRange = 100)
            => new(LidarVendor.Velodyne, model, LidarScanKind.Spinning,
                rings, columns, rpm / 60.0, 0, 0,
                beamAltDeg, null, 0, 0, 0, minRange, maxRange);

        public static LidarModelSpec RoboSense(string model, int rings, int columns,
            double fovTopDeg, double fovBottomDeg, double minRange = 0.2, double maxRange = 150)
            => new(LidarVendor.RoboSense, model, LidarScanKind.Spinning,
                rings, columns, 10.0, fovTopDeg, fovBottomDeg,
                null, null, 0, 0, 0, minRange, maxRange);

        public static LidarModelSpec Livox(string model, double fovHDeg, double fovVDeg,
            int beamsPerFrame, double minRange = 0.1, double maxRange = 260)
            => new(LidarVendor.Livox, model, LidarScanKind.NonRepetitive,
                0, 0, 10.0, 0, 0, null, null,
                fovHDeg, fovVDeg, beamsPerFrame, minRange, maxRange);
    }
}
