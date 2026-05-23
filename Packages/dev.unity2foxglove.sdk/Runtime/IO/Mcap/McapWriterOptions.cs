// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Advanced MCAP writer option policy shared by recorder and
// conformance tooling.

using System;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Summary/index record groups that an MCAP writer may emit.
    /// </summary>
    [Flags]
    public enum McapIndexTypes
    {
        /// <summary>Do not emit message, chunk, attachment, or metadata indexes.</summary>
        None = 0,
        /// <summary>Emit Attachment Index records in the summary section.</summary>
        Attachment = 1,
        /// <summary>Emit Chunk Index records in the summary section.</summary>
        Chunk = 2,
        /// <summary>Emit Message Index records after chunk records.</summary>
        Message = 4,
        /// <summary>Emit Metadata Index records in the summary section.</summary>
        Metadata = 8,
        /// <summary>Emit every supported index type.</summary>
        All = Attachment | Chunk | Message | Metadata
    }

    /// <summary>
    /// Advanced MCAP writer options aligned with the core official writer
    /// knobs. Unity's Inspector recording path keeps using default values.
    /// </summary>
    public sealed class McapWriterOptions
    {
        /// <summary>Default chunk size in bytes (1 MiB).</summary>
        public const int DefaultChunkSizeBytes = 1024 * 1024;
        /// <summary>Maximum uncompressed chunk payload size in bytes.</summary>
        public int ChunkSizeBytes = DefaultChunkSizeBytes;
        /// <summary>Chunk compression algorithm: empty, "lz4", or "zstd".</summary>
        public string Compression = "";
        /// <summary>Write messages into Chunk records when true, or directly into the data section when false.</summary>
        public bool UseChunking = true;
        /// <summary>Index groups to emit. Message and Chunk indexes only apply when chunking is enabled.</summary>
        public McapIndexTypes IndexTypes = McapIndexTypes.All;
        /// <summary>Repeat Channel records in the summary section.</summary>
        public bool RepeatChannels = true;
        /// <summary>Repeat Schema records in the summary section.</summary>
        public bool RepeatSchemas = true;
        /// <summary>Emit a Statistics record in the summary section.</summary>
        public bool UseStatistics = true;
        /// <summary>Emit Summary Offset records after the summary section.</summary>
        public bool UseSummaryOffsets = true;
        /// <summary>Compute chunk, attachment, and summary CRC fields.</summary>
        public bool EnableCrcs = true;
        /// <summary>Compute DataEnd.data_section_crc. Defaults off for the historical Unity recording layout.</summary>
        public bool EnableDataCrcs = false;

        /// <summary>
        /// Returns a normalized defensive copy. Invalid compression values fail
        /// before the recorder writes a partial file.
        /// </summary>
        public static McapWriterOptions Normalize(McapWriterOptions source)
        {
            var copy = new McapWriterOptions();
            if (source != null)
            {
                copy.ChunkSizeBytes = source.ChunkSizeBytes;
                copy.Compression = source.Compression;
                copy.UseChunking = source.UseChunking;
                copy.IndexTypes = source.IndexTypes;
                copy.RepeatChannels = source.RepeatChannels;
                copy.RepeatSchemas = source.RepeatSchemas;
                copy.UseStatistics = source.UseStatistics;
                copy.UseSummaryOffsets = source.UseSummaryOffsets;
                copy.EnableCrcs = source.EnableCrcs;
                copy.EnableDataCrcs = source.EnableDataCrcs;
            }

            copy.ChunkSizeBytes = copy.ChunkSizeBytes > 0
                ? copy.ChunkSizeBytes
                : DefaultChunkSizeBytes;
            copy.Compression = copy.Compression ?? "";
            switch (copy.Compression)
            {
                case "":
                case "lz4":
                case "zstd":
                    break;
                default:
                    throw new NotSupportedException("Unsupported MCAP compression: '" + copy.Compression + "'");
            }

            if (!copy.UseChunking)
                copy.IndexTypes &= ~(McapIndexTypes.Chunk | McapIndexTypes.Message);

            return copy;
        }

        /// <summary>True when the given index group is enabled after normalization.</summary>
        public bool HasIndex(McapIndexTypes type) => (IndexTypes & type) == type;
    }
}
