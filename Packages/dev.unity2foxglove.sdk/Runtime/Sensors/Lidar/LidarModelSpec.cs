// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar
// Purpose: Defines immutable LiDAR model metadata used by scan pattern creation.

using System;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Declares a known LiDAR model with preset parameters.
    /// Used by LidarModelRegistry to populate the Inspector dropdown.
    /// </summary>
    public sealed class LidarModelSpec
    {
        /// <summary>LiDAR vendor family.</summary>
        public readonly LidarVendor Vendor;

        /// <summary>Human-readable model name, for example <c>OS-1-32</c> or <c>VLP-16</c>.</summary>
        public readonly string Model;

        /// <summary>Scan pattern family used to build synthetic LiDAR frames.</summary>
        public readonly LidarScanKind Kind;

        /// <summary>Vertical channel count for spinning scanners.</summary>
        public readonly int Rings;

        /// <summary>Horizontal sample count per revolution for spinning scanners.</summary>
        public readonly int Columns;

        /// <summary>Nominal scan rate in hertz.</summary>
        public readonly double RateHz;

        /// <summary>Top vertical field-of-view angle in degrees for uniform spinning scan patterns.</summary>
        public readonly double FovTopDeg;

        /// <summary>Bottom vertical field-of-view angle in degrees for uniform spinning scan patterns.</summary>
        public readonly double FovBottomDeg;

        /// <summary>Optional per-ring beam altitude angles in degrees; <c>null</c> means use a uniform vertical distribution.</summary>
        public readonly double[] BeamAltitudeAnglesDeg;

        /// <summary>Optional vendor mode names such as <c>1024x10</c> or <c>2048x10</c>.</summary>
        public readonly string[] Modes;

        /// <summary>Horizontal field-of-view angle in degrees for non-repetitive scanners.</summary>
        public readonly double FovHDeg;

        /// <summary>Vertical field-of-view angle in degrees for non-repetitive scanners.</summary>
        public readonly double FovVDeg;

        /// <summary>Nominal beam count per published frame for non-repetitive scanners.</summary>
        public readonly int BeamsPerFrame;

        /// <summary>Minimum valid return range in meters.</summary>
        public readonly double MinRangeMeters;

        /// <summary>Maximum valid return range in meters.</summary>
        public readonly double MaxRangeMeters;

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

        /// <summary>Create immutable LiDAR model metadata for registry and scan-pattern use.</summary>
        public LidarModelSpec(LidarVendor vendor, string model, LidarScanKind kind,
            int rings, int columns, double rateHz, double fovTopDeg, double fovBottomDeg,
            double[] beamAltitudeAnglesDeg, string[] modes,
            double fovHDeg, double fovVDeg, int beamsPerFrame,
            double minRangeMeters, double maxRangeMeters,
            Vector3? lidarToSensorTranslationMeters = null, Quaternion? lidarToSensorRotation = null,
            Vector3? imuToSensorTranslationMeters = null, Quaternion? imuToSensorRotation = null,
            Vector3? tIlTranslationMeters = null, Quaternion? tIlRotation = null)
        {
            if (kind == LidarScanKind.Spinning && (rings <= 0 || columns <= 0 || rateHz <= 0.0))
                throw new ArgumentException("Spinning LiDAR requires positive rings, columns, and rateHz.");

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

        /// <summary>Create an Ouster-style spinning LiDAR model.</summary>
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

        /// <summary>Create a Velodyne-style spinning LiDAR model using revolutions per minute.</summary>
        public static LidarModelSpec Velodyne(string model, int rings, int columns, double rpm,
            double[] beamAltDeg, double minRange = 0.3, double maxRange = 100)
        {
            if (rpm <= 0.0)
                throw new ArgumentException("Velodyne LiDAR rpm must be positive.", nameof(rpm));

            return new LidarModelSpec(LidarVendor.Velodyne, model, LidarScanKind.Spinning,
                rings, columns, rpm / 60.0, 0, 0,
                beamAltDeg, null, 0, 0, 0, minRange, maxRange);
        }

        /// <summary>Create a RoboSense-style spinning LiDAR model.</summary>
        public static LidarModelSpec RoboSense(string model, int rings, int columns,
            double fovTopDeg, double fovBottomDeg, double minRange = 0.2, double maxRange = 150)
            => new(LidarVendor.RoboSense, model, LidarScanKind.Spinning,
                rings, columns, 10.0, fovTopDeg, fovBottomDeg,
                null, null, 0, 0, 0, minRange, maxRange);

        /// <summary>Create a Livox-style non-repetitive LiDAR model.</summary>
        public static LidarModelSpec Livox(string model, double fovHDeg, double fovVDeg,
            int beamsPerFrame, double minRange = 0.1, double maxRange = 260)
            => new(LidarVendor.Livox, model, LidarScanKind.NonRepetitive,
                0, 0, 10.0, 0, 0, null, null,
                fovHDeg, fovVDeg, beamsPerFrame, minRange, maxRange);
    }
}

