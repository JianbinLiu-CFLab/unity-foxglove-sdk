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
    /// <summary>
    /// Scans MCAP channel metadata for a <c>coordinate_mode</c> entry and
    /// compares it against the coordinate convention for the matching data
    /// direction, returning a human-readable mismatch warning when they differ.
    /// </summary>
    internal static class ReplayCoordinateModeGuard
    {
        /// <summary>
        /// Scan MCAP channel metadata for a coordinate_mode entry.
        /// Returns null when values match or no coordinate_mode metadata is found;
        /// returns a human-readable warning message when a mismatch is detected.
        /// </summary>
        internal static string FindMismatch(
            IEnumerable<McapChannel> channels, string currentCoordinateMode, string filePath)
            => FindMismatch(channels, currentCoordinateMode, currentCoordinateMode, filePath);

        /// <summary>
        /// Scan channel metadata using separate output and input coordinate
        /// conventions. Direction-less channels are legacy records and are
        /// accepted only when both current conventions agree with their metadata.
        /// </summary>
        internal static string FindMismatch(
            IEnumerable<McapChannel> channels,
            string currentOutputCoordinateMode,
            string currentInputCoordinateMode,
            string filePath)
        {
            if (channels == null)
                return null;

            foreach (var ch in channels)
            {
                if (ch.Metadata == null
                    || !ch.Metadata.TryGetValue(McapRecorder.CoordinateModeMetadataKey, out var mcapMode)
                    || string.IsNullOrEmpty(mcapMode))
                    continue;

                if (ch.Metadata.TryGetValue(McapRecorder.DataDirectionMetadataKey, out var direction))
                {
                    var currentMode = direction == "output"
                        ? currentOutputCoordinateMode
                        : direction == "input"
                            ? currentInputCoordinateMode
                            : null;
                    if (!string.IsNullOrEmpty(currentMode) && mcapMode != currentMode)
                    {
                        return $"MCAP '{Path.GetFileName(filePath)}' {direction} channel was recorded with " +
                               $"coordinate_mode '{mcapMode}', but current {direction} mode is '{currentMode}'. " +
                               "Mismatch may cause incorrect object transforms.";
                    }

                    continue;
                }

                if ((!string.IsNullOrEmpty(currentOutputCoordinateMode)
                     && mcapMode != currentOutputCoordinateMode)
                    || (!string.IsNullOrEmpty(currentInputCoordinateMode)
                        && mcapMode != currentInputCoordinateMode))
                {
                    return $"Legacy MCAP '{Path.GetFileName(filePath)}' channel was recorded with " +
                           $"coordinate_mode '{mcapMode}', but current output/input modes are " +
                           $"'{currentOutputCoordinateMode}'/'{currentInputCoordinateMode}'. " +
                           "Mismatch may cause incorrect object transforms.";
                }
            }

            return null;
        }
    }
}
