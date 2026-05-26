// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Public options for local MCAP indexed message queries.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Output ordering for MCAP message queries.
    /// </summary>
    public enum McapReadOrder
    {
        /// <summary>Return messages in encountered file order.</summary>
        FileOrder = 0,
        /// <summary>Return messages sorted by log time ascending.</summary>
        LogTimeAscending = 1,
        /// <summary>Return messages sorted by log time descending.</summary>
        LogTimeDescending = 2
    }

    /// <summary>
    /// Options for filtering messages returned by <see cref="McapIndexedReader"/>.
    /// </summary>
    public class McapReadOptions
    {
        /// <summary>
        /// Inclusive lower log-time bound in nanoseconds.
        /// </summary>
        public ulong StartTimeNs = 0;

        /// <summary>
        /// Inclusive upper log-time bound in nanoseconds.
        /// </summary>
        public ulong EndTimeNs = ulong.MaxValue;

        /// <summary>
        /// Topic names to include. <c>null</c> and empty lists both mean this
        /// dimension is not filtered. When <see cref="Topics"/> and
        /// <see cref="ChannelIds"/> are both provided, a message may match
        /// either filter.
        /// </summary>
        public List<string> Topics = new List<string>();

        /// <summary>
        /// Channel IDs to include. <c>null</c> and empty lists both mean this
        /// dimension is not filtered. When <see cref="Topics"/> and
        /// <see cref="ChannelIds"/> are both provided, a message may match
        /// either filter.
        /// </summary>
        public List<ushort> ChannelIds = new List<ushort>();

        /// <summary>
        /// Maximum number of matching messages to keep. Values less than or
        /// equal to zero mean unlimited. <see cref="McapReadOrder.FileOrder"/>
        /// keeps the first matching messages encountered in the file;
        /// log-time orders keep the latest or earliest messages according to
        /// their sort direction.
        /// </summary>
        public int MaxMessages = 0;

        /// <summary>
        /// Output ordering. The default preserves historical log-time ascending
        /// query results.
        /// </summary>
        public McapReadOrder Order = McapReadOrder.LogTimeAscending;

        /// <summary>
        /// When true, EndTimeNs is exclusive. The default keeps the historical
        /// inclusive upper bound used by Unity replay/DataLoader paths.
        /// </summary>
        public bool UseOfficialEndTimeSemantics = false;

        /// <summary>
        /// Allows linear scanning when summary/index records are missing.
        /// Disable for strict indexed-reader conformance checks.
        /// </summary>
        public bool AllowLinearFallback = true;

        /// <summary>
        /// Validate non-zero CRC fields while reading chunks, attachments, and
        /// streaming DataEnd records.
        /// </summary>
        public bool ValidateCrcs = true;

        /// <summary>
        /// Maximum decompressed size accepted for a single chunk. A value of
        /// zero disables this chunk decompression guard.
        /// </summary>
        public ulong ChunkUncompressedSizeLimit = McapReader.DefaultChunkUncompressedSizeLimit;
    }
}
