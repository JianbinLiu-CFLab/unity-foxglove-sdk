// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Scans MCAP channel metadata for a coordinate_mode entry and compares
// it against the caller's current coordinate mode, providing a mismatch message
// when they differ.

using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Core
{
    internal static class ReplayCoordinateModeGuard
    {
        /// <summary>
        /// Scan MCAP channel metadata for a coordinate_mode entry.
        /// Returns null when values match or no coordinate_mode metadata is found;
        /// returns a human-readable warning message when a mismatch is detected.
        /// </summary>
        internal static string FindMismatch(
            IEnumerable<McapChannel> channels, string currentCoordinateMode, string filePath)
        {
            if (channels == null || string.IsNullOrEmpty(currentCoordinateMode))
                return null;

            foreach (var ch in channels)
            {
                if (ch.Metadata != null && ch.Metadata.TryGetValue("coordinate_mode", out var mcapMode)
                    && !string.IsNullOrEmpty(mcapMode))
                {
                    if (mcapMode != currentCoordinateMode)
                        return $"MCAP '{Path.GetFileName(filePath)}' was recorded with coordinate_mode '{mcapMode}', " +
                               $"but current mode is '{currentCoordinateMode}'. Mismatch may cause incorrect object transforms.";
                    break;
                }
            }

            return null;
        }
    }
}
