// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Declares a known LiDAR model with preset parameters.
    /// Used by LidarModelRegistry to populate the Inspector dropdown.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public sealed class LidarModelSpec
    {
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly LidarVendor Vendor;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly string Model;              // e.g. "OS-1-32", "VLP-16"
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly LidarScanKind Kind;

        // Spinning
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly int Rings;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly int Columns;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly double RateHz;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public readonly double FovTopDeg, FovBottomDeg;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly double[] BeamAltitudeAnglesDeg; // null -> uniform distribution
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly string[] Modes;                  // null or e.g. {"1024x10", "2048x10"}

        // Non-repetitive (Livox)
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public readonly double FovHDeg, FovVDeg;
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public readonly int BeamsPerFrame;

        // Shared
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public readonly double MinRangeMeters, MaxRangeMeters;

        /// <summary>Translation from LiDAR frame to sensor housing frame in meters.</summary>
        public readonly Vector3 LidarToSensorTranslationMeters;

        /// <summary>Rotation from LiDAR frame to sensor housing frame.</summary>
        public readonly Quaternion LidarToSensorRotation;

        /// <summary>Translation from IMU frame to sensor housing frame in meters.</summary>
        public readonly Vector3 ImuToSensorTranslationMeters;

        /// <summary>Rotation from IMU frame to sensor housing frame.</summary>
        public readonly Quaternion ImuToSensorRotation;

        /// <summary>
        /// Legacy alias for the IMU-to-sensor translation, retained for existing
        /// T_IL override UI and tests.
        /// </summary>
        public readonly Vector3 TIlTranslationMeters;

        /// <summary>
        /// Legacy alias for the IMU-to-sensor rotation, retained for existing
        /// T_IL override UI and tests.
        /// </summary>
        public readonly Quaternion TIlRotation;

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public LidarModelSpec(LidarVendor vendor, string model, LidarScanKind kind,
            int rings, int columns, double rateHz, double fovTopDeg, double fovBottomDeg,
            double[] beamAltitudeAnglesDeg, string[] modes,
            double fovHDeg, double fovVDeg, int beamsPerFrame,
            double minRangeMeters, double maxRangeMeters,
            Vector3? lidarToSensorTranslationMeters = null, Quaternion? lidarToSensorRotation = null,
            Vector3? imuToSensorTranslationMeters = null, Quaternion? imuToSensorRotation = null,
            Vector3? tIlTranslationMeters = null, Quaternion? tIlRotation = null)
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
            LidarToSensorTranslationMeters = lidarToSensorTranslationMeters ?? Vector3.Zero;
            LidarToSensorRotation = lidarToSensorRotation ?? Quaternion.Identity;
            ImuToSensorTranslationMeters = imuToSensorTranslationMeters ?? tIlTranslationMeters ?? Vector3.Zero;
            ImuToSensorRotation = imuToSensorRotation ?? tIlRotation ?? Quaternion.Identity;
            TIlTranslationMeters = ImuToSensorTranslationMeters;
            TIlRotation = ImuToSensorRotation;
        }

        // Factory helpers
        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public static LidarModelSpec Ouster(string model, int rings, int columns, string[] modes,
            double fovTopDeg, double fovBottomDeg, double[] beamAltDeg = null,
            double minRange = 0.5, double maxRange = 120,
            Vector3? lidarToSensorTranslationMeters = null, Quaternion? lidarToSensorRotation = null,
            Vector3? imuToSensorTranslationMeters = null, Quaternion? imuToSensorRotation = null,
            Vector3? tIlTranslationMeters = null, Quaternion? tIlRotation = null)
            => new(LidarVendor.Ouster, model, LidarScanKind.Spinning,
                rings, columns, 10.0, fovTopDeg, fovBottomDeg,
                beamAltDeg, modes, 0, 0, 0, minRange, maxRange,
                lidarToSensorTranslationMeters, lidarToSensorRotation,
                imuToSensorTranslationMeters, imuToSensorRotation,
                tIlTranslationMeters, tIlRotation);

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public static LidarModelSpec Velodyne(string model, int rings, int columns, double rpm,
            double[] beamAltDeg, double minRange = 0.3, double maxRange = 100)
            => new(LidarVendor.Velodyne, model, LidarScanKind.Spinning,
                rings, columns, rpm / 60.0, 0, 0,
                beamAltDeg, null, 0, 0, 0, minRange, maxRange);

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public static LidarModelSpec RoboSense(string model, int rings, int columns,
            double fovTopDeg, double fovBottomDeg, double minRange = 0.2, double maxRange = 150)
            => new(LidarVendor.RoboSense, model, LidarScanKind.Spinning,
                rings, columns, 10.0, fovTopDeg, fovBottomDeg,
                null, null, 0, 0, 0, minRange, maxRange);

        /// <summary>
        /// Summary text for this member.
        /// </summary>

        public static LidarModelSpec Livox(string model, double fovHDeg, double fovVDeg,
            int beamsPerFrame, double minRange = 0.1, double maxRange = 260)
            => new(LidarVendor.Livox, model, LidarScanKind.NonRepetitive,
                0, 0, 10.0, 0, 0, null, null,
                fovHDeg, fovVDeg, beamsPerFrame, minRange, maxRange);
    }
}
