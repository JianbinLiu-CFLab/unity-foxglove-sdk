// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Sensors.Lidar
{
    /// <summary>
    /// Built-in spinning-LiDAR presets (Ouster OS-0/OS-1/OS-2 at 32/64/128 rings).
    /// Beam angles are evenly distributed across the line's vertical FOV.
    /// </summary>
    public enum LidarPreset
    {
        Os0_32, Os0_64, Os0_128,
        Os1_32, Os1_64, Os1_128,
        Os2_32, Os2_64, Os2_128
    }

    /// <summary>
    /// Parses LiDAR metadata JSON and provides built-in fallback profiles.
    /// </summary>
    public static class LidarProfileLoader
    {
        /// <summary>
        /// Build a spinning-LiDAR profile with beams evenly spaced across a vertical
        /// FOV. Suitable for Ouster/Velodyne-style sensors; azimuth is co-axial (0).
        /// </summary>
        public static LidarProfile CreateUniform(
            string productLine, int pixelsPerColumn, int columnsPerFrame,
            double scanRateHz, double fovTopDeg, double fovBottomDeg, double minRangeMeters)
        {
            const double degToRad = Math.PI / 180.0;
            pixelsPerColumn = Math.Max(1, pixelsPerColumn);
            columnsPerFrame = Math.Max(16, columnsPerFrame);

            var altitude = new double[pixelsPerColumn];
            var azimuth = new double[pixelsPerColumn];
            for (var i = 0; i < pixelsPerColumn; i++)
            {
                var t = pixelsPerColumn == 1 ? 0.5 : (double)i / (pixelsPerColumn - 1);
                altitude[i] = (fovTopDeg + (fovBottomDeg - fovTopDeg) * t) * degToRad; // ring 0 = top
                azimuth[i] = 0.0;
            }

            return new LidarProfile
            {
                ProductLine = string.IsNullOrEmpty(productLine) ? "Custom" : productLine,
                LidarMode = $"{columnsPerFrame}x{(int)Math.Round(scanRateHz)}",
                PixelsPerColumn = pixelsPerColumn,
                ColumnsPerFrame = columnsPerFrame,
                ColumnsPerPacket = 16,
                ScanRateHz = scanRateHz <= 0 ? 10.0 : scanRateHz,
                MinRangeMeters = Math.Max(0.0, minRangeMeters),
                LidarOriginToBeamOriginMeters = 0.03618,
                BeamAltitudeAngles = altitude,
                BeamAzimuthAngles = azimuth
            };
        }

        /// <summary>Create a built-in preset profile (1024 columns, 10 Hz).</summary>
        public static LidarProfile CreatePreset(LidarPreset preset)
        {
            // (productLine, rings, fovTopDeg, fovBottomDeg, minRange)
            switch (preset)
            {
                case LidarPreset.Os0_32:  return CreateUniform("OS-0", 32,  1024, 10, 45.0, -45.0, 0.3);
                case LidarPreset.Os0_64:  return CreateUniform("OS-0", 64,  1024, 10, 45.0, -45.0, 0.3);
                case LidarPreset.Os0_128: return CreateUniform("OS-0", 128, 1024, 10, 45.0, -45.0, 0.3);
                case LidarPreset.Os1_64:  return CreateUniform("OS-1", 64,  1024, 10, 16.6, -16.6, 0.7);
                case LidarPreset.Os1_128: return CreateUniform("OS-1", 128, 1024, 10, 16.6, -16.6, 0.7);
                case LidarPreset.Os2_32:  return CreateUniform("OS-2", 32,  1024, 10, 11.25, -11.25, 0.5);
                case LidarPreset.Os2_64:  return CreateUniform("OS-2", 64,  1024, 10, 11.25, -11.25, 0.5);
                case LidarPreset.Os2_128: return CreateUniform("OS-2", 128, 1024, 10, 11.25, -11.25, 0.5);
                case LidarPreset.Os1_32:
                default:
                    // Exact datasheet angles for the canonical OS-1-32.
                    return CreateOs132Default();
            }
        }

        /// <summary>
        /// Create a built-in Ouster OS-1-32 fallback profile.
        /// 32 rings, 1024 columns, 10 Hz, min range 0.7 m.
        /// Beam altitude angles sourced from Ouster OS-1-32 datasheet:
        /// approximately -16.6 to +16.6 degrees.
        /// </summary>
        public static LidarProfile CreateOs132Default()
        {
            const double degToRad = Math.PI / 180.0;

            // OS-1-32 beam altitude angles in degrees (datasheet values)
            var altitudeDeg = new[]
            {
                 16.611, 15.517, 14.422, 13.329,
                 12.249, 11.147, 10.073,  8.969,
                  7.877,  6.789,  5.698,  4.606,
                  3.523,  2.435,  1.349,  0.261,
                 -0.829, -1.917, -3.010, -4.098,
                 -5.192, -6.282, -7.370, -8.461,
                 -9.543,-10.648,-11.725,-12.837,
                -13.914,-15.004,-16.086,-16.611
            };

            var altitude = new double[32];
            var azimuth = new double[32];
            for (var i = 0; i < 32; i++)
            {
                altitude[i] = altitudeDeg[i] * degToRad;
                azimuth[i] = 0.0; // OS-1 has co-axial beams
            }

            return new LidarProfile
            {
                ProductLine = "OS-1",
                LidarMode = "1024x10",
                PixelsPerColumn = 32,
                ColumnsPerFrame = 1024,
                ColumnsPerPacket = 16,
                ScanRateHz = 10.0,
                MinRangeMeters = 0.7,
                LidarOriginToBeamOriginMeters = 0.03618,
                BeamAltitudeAngles = altitude,
                BeamAzimuthAngles = azimuth
            };
        }

        /// <summary>
        /// Parse a metadata JSON string into a LidarProfile.
        /// </summary>
        /// <param name="json">Ouster metadata JSON string.</param>
        /// <param name="mode">Lidar mode string, e.g. "1024x10" or "1024x20".</param>
        /// <param name="profile">Parsed profile, or null on failure.</param>
        /// <param name="error">Error message on failure.</param>
        /// <returns>true if parsing succeeded.</returns>
        public static bool TryParseFromJson(string json, string mode, out LidarProfile profile, out string error)
        {
            profile = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON string is null or empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(mode))
            {
                error = "Lidar mode string is null or empty.";
                return false;
            }

            // Parse scan rate from mode string (e.g. "1024x10" → 10, "1024x20" → 20)
            // Expected format: "<columns>x<rate>"
            var parts = mode.Split('x');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var scanRateHz)
                || scanRateHz <= 0)
            {
                error = $"Unknown lidar mode format: \"{mode}\". Expected \"<columns>x<rate>\" (e.g. \"1024x10\").";
                return false;
            }

            // Reject unrealistic scan rates (>30 Hz is not a standard lidar mode).
            if (scanRateHz > 30.0)
            {
                error = $"Unsupported lidar mode \"{mode}\": expected 10 or 20 Hz.";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception ex)
            {
                error = $"Failed to parse JSON: {ex.Message}";
                return false;
            }

            var pixelsPerColumn = root["pixels_per_column"]?.Value<int?>();
            if (pixelsPerColumn == null || pixelsPerColumn <= 0)
            {
                error = "Missing or invalid \"pixels_per_column\" field.";
                return false;
            }

            var columnsPerFrame = root["columns_per_frame"]?.Value<int?>() ?? 1024;
            var columnsPerPacket = root["columns_per_packet"]?.Value<int?>() ?? 16;
            var minRange = root["lidar_mode"]?["min_range"]?.Value<double?>() ?? 0.7;
            var originOffset = root["lidar_origin_to_beam_origin_mm"]?.Value<double?>() / 1000.0 ?? 0.03618;

            // Parse beam altitude angles
            var altToken = root["beam_altitude_angles"];
            if (altToken == null || !(altToken is JArray altArray))
            {
                error = "Missing or invalid \"beam_altitude_angles\" array.";
                return false;
            }

            if (altArray.Count != pixelsPerColumn.Value)
            {
                error = $"beam_altitude_angles length ({altArray.Count}) does not match pixels_per_column ({pixelsPerColumn}).";
                return false;
            }

            var altitude = new double[altArray.Count];
            for (var i = 0; i < altArray.Count; i++)
                altitude[i] = altArray[i].Value<double>();

            // Parse beam azimuth angles (optional — fill with zeros if empty)
            var azmToken = root["beam_azimuth_angles"];
            double[] azimuth;

            if (azmToken != null && azmToken is JArray azmArray && azmArray.Count > 0)
            {
                if (azmArray.Count != pixelsPerColumn.Value)
                {
                    error = $"beam_azimuth_angles length ({azmArray.Count}) does not match pixels_per_column ({pixelsPerColumn}).";
                    return false;
                }

                azimuth = new double[azmArray.Count];
                for (var i = 0; i < azmArray.Count; i++)
                    azimuth[i] = azmArray[i].Value<double>();
            }
            else
            {
                azimuth = new double[pixelsPerColumn.Value];
                // Already zero-initialized
            }

            profile = new LidarProfile
            {
                ProductLine = root["prod_line"]?.Value<string>() ?? "OS-1",
                LidarMode = mode,
                PixelsPerColumn = pixelsPerColumn.Value,
                ColumnsPerFrame = columnsPerFrame,
                ColumnsPerPacket = columnsPerPacket,
                ScanRateHz = scanRateHz,
                MinRangeMeters = minRange,
                LidarOriginToBeamOriginMeters = originOffset,
                BeamAltitudeAngles = altitude,
                BeamAzimuthAngles = azimuth
            };

            return true;
        }
    }
}
