// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Plain data models for MCAP records — Header, Schema, Channel,
// Message, Chunk, Index, Statistics, Metadata, Footer, and Summary.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>MCAP Header record — identifies the profile and library that generated the file.</summary>
    public class McapHeader
    {
        /// <summary>Profile identifier (e.g. <c>"x-foxglove-ros1"</c> or <c>"x-foxglove-ros2"</c>).</summary>
        public string Profile;
        /// <summary>Library identifier (e.g. <c>"unity2foxglove"</c>).</summary>
        public string Library;
    }

    /// <summary>MCAP Schema record — describes a message schema registered by a channel.</summary>
    public class McapSchema
    {
        /// <summary>Unique schema ID.</summary>
        public ushort Id;
        /// <summary>Human-readable schema name.</summary>
        public string Name;
        /// <summary>Encoding format (e.g. <c>"ros1msg"</c>, <c>"ros2msg"</c>, <c>"protobuf"</c>, <c>"json"</c>).</summary>
        public string Encoding;
        /// <summary>Raw schema definition data.</summary>
        public byte[] Data;
    }

    /// <summary>MCAP Channel record — declares a topic with its schema and metadata.</summary>
    public class McapChannel
    {
        /// <summary>Unique channel ID.</summary>
        public ushort Id;
        /// <summary>Schema ID this channel uses.</summary>
        public ushort SchemaId;
        /// <summary>Topic name (e.g. <c>"/odom"</c>).</summary>
        public string Topic;
        /// <summary>Message encoding (e.g. <c>"ros1"</c>, <c>"cdr"</c>).</summary>
        public string MessageEncoding;
        /// <summary>User-provided key-value metadata.</summary>
        public Dictionary<string, string> Metadata = new();
    }

    /// <summary>MCAP Message record — a single message on a channel at a given time.</summary>
    public class McapMessage
    {
        /// <summary>Channel ID this message belongs to.</summary>
        public ushort ChannelId;
        /// <summary>Monotonically increasing sequence number per channel.</summary>
        public uint Sequence;
        /// <summary>Log timestamp (nanoseconds).</summary>
        public ulong LogTime;
        /// <summary>Publish timestamp (nanoseconds).</summary>
        public ulong PublishTime;
        /// <summary>Message payload bytes.</summary>
        public byte[] Data;
    }

    /// <summary>MCAP Chunk record — compressed container holding multiple records.</summary>
    public class McapChunk
    {
        /// <summary>Earliest <c>LogTime</c> of messages in the chunk.</summary>
        public ulong StartTime;
        /// <summary>Latest <c>LogTime</c> of messages in the chunk.</summary>
        public ulong EndTime;
        /// <summary>Uncompressed byte size of the chunk records payload.</summary>
        public ulong UncompressedSize;
        /// <summary>CRC32 of the uncompressed data.</summary>
        public uint UncompressedCrc;
        /// <summary>Compression algorithm used (<c>""</c>, <c>"lz4"</c>, or <c>"zstd"</c>).</summary>
        public string Compression;
        /// <summary>Compressed byte size of the chunk records payload.</summary>
        public ulong CompressedSize;
        /// <summary>Raw compressed or uncompressed record bytes.</summary>
        public byte[] Records;
    }

    /// <summary>MCAP Message Index record — maps (timestamp, offset) pairs for a channel.</summary>
    public class McapMessageIndex
    {
        /// <summary>Channel ID this index covers.</summary>
        public ushort ChannelId;
        /// <summary>List of (timestamp, file offset) pairs for rapid seeking.</summary>
        public List<(ulong timestamp, ulong offset)> Records = new();
    }

    /// <summary>MCAP Chunk Index record — summary of a chunk's time range, offsets, and message index pointers.</summary>
    public class McapChunkIndex
    {
        /// <summary>Earliest message timestamp in the chunk.</summary>
        public ulong MessageStartTime;
        /// <summary>Latest message timestamp in the chunk.</summary>
        public ulong MessageEndTime;
        /// <summary>Byte offset in the file where the chunk record begins.</summary>
        public ulong ChunkStartOffset;
        /// <summary>Total byte length of the chunk record.</summary>
        public ulong ChunkLength;
        /// <summary>Per-channel offsets into the message index section.</summary>
        public Dictionary<ushort, ulong> MessageIndexOffsets = new();
        /// <summary>Total byte length of the combined message index for this chunk.</summary>
        public ulong MessageIndexLength;
        /// <summary>Compression algorithm used by the chunk.</summary>
        public string Compression;
        /// <summary>Compressed byte size of the chunk payload.</summary>
        public ulong CompressedSize;
        /// <summary>Uncompressed byte size of the chunk payload.</summary>
        public ulong UncompressedSize;
    }

    /// <summary>MCAP Statistics record — aggregate counts and time bounds for the entire file.</summary>
    public class McapStatistics
    {
        /// <summary>Total number of messages.</summary>
        public ulong MessageCount;
        /// <summary>Number of distinct schemas.</summary>
        public ushort SchemaCount;
        /// <summary>Number of distinct channels.</summary>
        public uint ChannelCount;
        /// <summary>Number of attachments.</summary>
        public uint AttachmentCount;
        /// <summary>Number of metadata records.</summary>
        public uint MetadataCount;
        /// <summary>Number of chunks.</summary>
        public uint ChunkCount;
        /// <summary>Earliest <c>LogTime</c> across all messages.</summary>
        public ulong MessageStartTime;
        /// <summary>Latest <c>LogTime</c> across all messages.</summary>
        public ulong MessageEndTime;
        /// <summary>Per-channel message counts (channel ID to count).</summary>
        public Dictionary<ushort, ulong> ChannelMessageCounts = new();
    }

    /// <summary>MCAP Metadata record — arbitrary named key-value data attached to the file.</summary>
    public class McapMetadata
    {
        /// <summary>Metadata name (e.g. <c>"ros2 bag info"</c>).</summary>
        public string Name;
        /// <summary>Metadata key-value pairs.</summary>
        public Dictionary<string, string> Metadata = new();
    }

    /// <summary>MCAP Metadata Index record — offset and length of a Metadata record in the file.</summary>
    public class McapMetadataIndex
    {
        /// <summary>Byte offset of the Metadata record in the file.</summary>
        public ulong Offset;
        /// <summary>Byte length of the Metadata record.</summary>
        public ulong Length;
        /// <summary>Metadata name.</summary>
        public string Name;
    }

    /// <summary>MCAP Footer record — contains summary section offsets and a CRC32.</summary>
    public class McapFooter
    {
        /// <summary>Byte offset where the summary section starts.</summary>
        public ulong SummaryStart;
        /// <summary>Byte offset where the summary offset section starts.</summary>
        public ulong SummaryOffsetStart;
        /// <summary>CRC32 checksum of the summary section.</summary>
        public uint SummaryCrc;
    }

    /// <summary>MCAP Attachment record — arbitrary binary artifact stored outside chunks.</summary>
    public class McapAttachment
    {
        /// <summary>Log timestamp (nanoseconds).</summary>
        public ulong LogTime;
        /// <summary>Creation timestamp (nanoseconds).</summary>
        public ulong CreateTime;
        /// <summary>Attachment name.</summary>
        public string Name;
        /// <summary>MIME media type (e.g. <c>"text/plain"</c>).</summary>
        public string MediaType;
        /// <summary>Raw attachment data.</summary>
        public byte[] Data;
        /// <summary>CRC32 of the attachment record content before the CRC field.</summary>
        public uint Crc;
        /// <summary>Whether the CRC was non-zero and matched the recomputed checksum.</summary>
        public bool CrcValid;
    }

    /// <summary>MCAP Attachment Index record — offset and metadata for an attachment in the file.</summary>
    public class McapAttachmentIndex
    {
        /// <summary>Absolute byte offset of the Attachment record in the file.</summary>
        public ulong Offset;
        /// <summary>Total byte length of the Attachment record (opcode + 8-byte length + content).</summary>
        public ulong Length;
        /// <summary>Log timestamp (nanoseconds).</summary>
        public ulong LogTime;
        /// <summary>Creation timestamp (nanoseconds).</summary>
        public ulong CreateTime;
        /// <summary>Size of the attachment data payload in bytes (excluding uint64 prefix).</summary>
        public ulong DataSize;
        /// <summary>Attachment name.</summary>
        public string Name;
        /// <summary>MIME media type.</summary>
        public string MediaType;
    }

    /// <summary>Aggregated view of all summary records in an MCAP file.</summary>
    public class McapFileSummary
    {
        /// <summary>All schemas known to the file.</summary>
        public List<McapSchema> Schemas = new();
        /// <summary>All channels declared in the file.</summary>
        public List<McapChannel> Channels = new();
        /// <summary>Aggregate statistics (may be <c>null</c>).</summary>
        public McapStatistics Statistics;
        /// <summary>Chunk index entries for every chunk.</summary>
        public List<McapChunkIndex> ChunkIndexes = new();
        /// <summary>Metadata index entries for every metadata record.</summary>
        public List<McapMetadataIndex> MetadataIndexes = new();
        /// <summary>Attachment index entries for every attachment.</summary>
        public List<McapAttachmentIndex> AttachmentIndexes = new();

        /// <summary>Absolute byte offset where the data section ends.</summary>
        public ulong DataSectionEndOffset;

        /// <summary>Messages discovered by sequential data-section scanning when no chunk index is available.</summary>
        public List<McapMessage> SequentialMessages = new();
    }
}
