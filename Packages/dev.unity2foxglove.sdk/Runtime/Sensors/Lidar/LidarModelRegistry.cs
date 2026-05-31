// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Registry of known LiDAR models with preset parameters.
    /// Used by VirtualLidarEditor to populate dropdowns and by
    /// LidarScanPatternFactory to create scan patterns from presets.
    /// </summary>
    /// <summary>
    /// Summary text for this member.
    /// </summary>

/// <summary>Summary text for this member.</summary>
    public static class LidarModelRegistry
    {
        // Ouster OS datasheet-style child-to-sensor extrinsics. The scene scaffold
        // uses these as os_lidar/os_imu -> os_sensor metadata and inverts them for
        // Unity child local transforms under the Lidar-IMU-Unit/os_sensor frame.
        private static readonly Vector3 OusterLidarToSensorMeters =
            new(0f, 0f, 0.038195f);
        private static readonly Quaternion OusterLidarToSensorRotation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI);
        private static readonly Vector3 OusterImuToSensorMeters =
            new(-0.002441f, -0.009725f, 0.007533f);
        private static readonly Quaternion OusterImuToSensorRotation =
            Quaternion.Identity;

        private static readonly List<LidarModelSpec> _all = new()
        {
            // Ouster OS-0 (ultra-wide FOV)
            LidarModelSpec.Ouster("OS-0-32", 32, 1024, M("1024x10", "2048x10"), +43.9, -45.4,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-0-64", 64, 1024, M("1024x10", "2048x10"), +43.9, -45.4,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-0-128", 128, 1024, M("1024x10", "2048x10"), +43.9, -45.4,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),

            // Ouster OS-1 (mid-range)
            LidarModelSpec.Ouster("OS-1-32", 32, 1024, M("1024x10", "2048x10", "512x20"), +20.95, -21.82,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-1-64", 64, 1024, M("1024x10", "2048x10", "512x20"), +20.95, -21.82,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-1-128", 128, 1024, M("1024x10", "2048x10", "512x20"), +20.95, -21.82,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),

            // Ouster OS-2 (long-range, narrow FOV)
            LidarModelSpec.Ouster("OS-2-32", 32, 2048, M("1024x10", "2048x10"), +10.76, -11.09,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-2-64", 64, 2048, M("1024x10", "2048x10"), +10.76, -11.09,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),
            LidarModelSpec.Ouster("OS-2-128", 128, 2048, M("1024x10", "2048x10"), +10.76, -11.09,
                lidarToSensorTranslationMeters: OusterLidarToSensorMeters, lidarToSensorRotation: OusterLidarToSensorRotation,
                imuToSensorTranslationMeters: OusterImuToSensorMeters, imuToSensorRotation: OusterImuToSensorRotation),

            // Velodyne
            LidarModelSpec.Velodyne("VLP-16", 16, 1800, 600, VlpAngles(16, +15.0, -15.0)),
            LidarModelSpec.Velodyne("HDL-32E", 32, 1800, 600, VlpAngles(32, +10.67, -30.67)),
            LidarModelSpec.Velodyne("HDL-64E", 64, 1800, 300, VlpAngles(64, +2.0, -24.8)),

            // RoboSense
            LidarModelSpec.RoboSense("RS-LiDAR-16", 16, 1800, +15.0, -15.0),
            LidarModelSpec.RoboSense("RS-LiDAR-32", 32, 1800, +15.0, -25.0),

            // Livox
            LidarModelSpec.Livox("Mid-360", 360.0, 59.0, 40000),
            LidarModelSpec.Livox("Horizon", 81.7, 25.1, 24000),
            LidarModelSpec.Livox("Tele-15", 14.5, 16.2, 48000),
        };

        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public static IReadOnlyList<LidarModelSpec> All => _all.AsReadOnly();
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public static IEnumerable<LidarModelSpec> ForVendor(LidarVendor v)
            => _all.Where(s => s.Vendor == v);
        /// <summary>
        /// Summary text for this member.
        /// </summary>

/// <summary>Summary text for this member.</summary>
        public static bool TryGet(LidarVendor v, string model, out LidarModelSpec spec)
        {
            spec = _all.FirstOrDefault(s => s.Vendor == v && s.Model == model);
            return spec != null;
        }

        private static string[] M(params string[] modes) => modes;

        private static double[] VlpAngles(int count, double topDeg, double bottomDeg)
        {
            // Velodyne uniform beam distribution
            var result = new double[count];
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.5 : (double)i / (count - 1);
                result[i] = topDeg + t * (bottomDeg - topDeg);
            }
            return result;
        }
    }
}
