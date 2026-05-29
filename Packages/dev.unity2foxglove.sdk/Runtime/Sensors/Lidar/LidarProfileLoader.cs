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
    /// Parses LiDAR metadata JSON and provides built-in fallback profiles.
    /// Built-in presets now live in <see cref="LidarModelRegistry"/>.
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

        /// <summary>Parse a lidar mode string "&lt;columns&gt;x&lt;rateHz&gt;" (e.g. "1024x10").</summary>
        public static bool TryParseMode(string mode, out int columns, out double rateHz)
        {
            columns = 0;
            rateHz = 0;
            if (string.IsNullOrWhiteSpace(mode)) return false;
            var parts = mode.Split('x');
            return parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out columns)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rateHz)
                && columns > 0 && rateHz > 0;
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

            // Schema-tolerant: handles legacy flat metadata (beam angles + lidar_mode
            // at top level, pixels under "data_format") and v2/v3 nested metadata
            // ("beam_intrinsics", "lidar_data_format", "config_params", "sensor_info").
            var beam = root["beam_intrinsics"] as JObject;
            var df = (root["lidar_data_format"] ?? root["data_format"]) as JObject;
            var cfg = root["config_params"] as JObject;
            var info = root["sensor_info"] as JObject;

            JToken FromBeam(string key) => beam?[key] ?? root[key];
            JToken FromFormat(string key) => df?[key] ?? root[key];

            // lidar_mode: JSON value wins; fall back to the mode argument.
            var lidarModeStr = (cfg?["lidar_mode"] ?? root["lidar_mode"])?.Value<string>();
            if (string.IsNullOrWhiteSpace(lidarModeStr))
                lidarModeStr = mode;
            if (string.IsNullOrWhiteSpace(lidarModeStr))
            {
                error = "No \"lidar_mode\" in JSON and no mode argument provided.";
                return false;
            }
            if (!TryParseMode(lidarModeStr, out var modeColumns, out var scanRateHz))
            {
                error = $"Unknown lidar mode format: \"{lidarModeStr}\". Expected \"<columns>x<rate>\" (e.g. \"1024x10\").";
                return false;
            }
            if (scanRateHz > 30.0)
            {
                error = $"Unsupported lidar mode \"{lidarModeStr}\": scan rate above 30 Hz.";
                return false;
            }

            // beam_altitude_angles (required)
            if (!(FromBeam("beam_altitude_angles") is JArray altArray) || altArray.Count == 0)
            {
                error = "Missing or invalid \"beam_altitude_angles\" array.";
                return false;
            }
            var altitude = new double[altArray.Count];
            for (var i = 0; i < altArray.Count; i++)
                altitude[i] = altArray[i].Value<double>();

            // pixels_per_column: from data_format/lidar_data_format/top level; else infer.
            var pixelsPerColumn = FromFormat("pixels_per_column")?.Value<int?>() ?? altArray.Count;
            if (pixelsPerColumn <= 0)
                pixelsPerColumn = altArray.Count;
            if (pixelsPerColumn != altArray.Count)
            {
                error = $"beam_altitude_angles length ({altArray.Count}) does not match pixels_per_column ({pixelsPerColumn}).";
                return false;
            }

            // beam_azimuth_angles (optional → zeros)
            double[] azimuth;
            if (FromBeam("beam_azimuth_angles") is JArray azmArray && azmArray.Count > 0)
            {
                if (azmArray.Count != pixelsPerColumn)
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
                azimuth = new double[pixelsPerColumn];
            }

            var columnsPerFrame = FromFormat("columns_per_frame")?.Value<int?>() ?? modeColumns;
            var columnsPerPacket = FromFormat("columns_per_packet")?.Value<int?>() ?? 16;
            var originMm = FromBeam("lidar_origin_to_beam_origin_mm")?.Value<double?>();
            var originOffset = originMm.HasValue ? originMm.Value / 1000.0 : 0.03618;
            var prodLine = (info?["prod_line"] ?? root["prod_line"])?.Value<string>() ?? "OS-1";

            profile = new LidarProfile
            {
                ProductLine = prodLine,
                LidarMode = lidarModeStr,
                PixelsPerColumn = pixelsPerColumn,
                ColumnsPerFrame = columnsPerFrame,
                ColumnsPerPacket = columnsPerPacket,
                ScanRateHz = scanRateHz,
                MinRangeMeters = 0.7, // metadata rarely carries min_range; sensible default
                LidarOriginToBeamOriginMeters = originOffset,
                BeamAltitudeAngles = altitude,
                BeamAzimuthAngles = azimuth
            };

            return true;
        }
    }
}
