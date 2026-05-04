using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    public class McapHeader
    {
        public string Profile;
        public string Library;
    }

    public class McapSchema
    {
        public ushort Id;
        public string Name;
        public string Encoding;
        public byte[] Data;
    }

    public class McapChannel
    {
        public ushort Id;
        public ushort SchemaId;
        public string Topic;
        public string MessageEncoding;
        public Dictionary<string, string> Metadata = new();
    }

    public class McapMessage
    {
        public ushort ChannelId;
        public uint Sequence;
        public ulong LogTime;
        public ulong PublishTime;
        public byte[] Data;
    }

    public class McapChunk
    {
        public ulong StartTime;
        public ulong EndTime;
        public ulong UncompressedSize;
        public uint UncompressedCrc;
        public string Compression;
        public ulong CompressedSize;
        public byte[] Records;
    }

    public class McapMessageIndex
    {
        public ushort ChannelId;
        public List<(ulong timestamp, ulong offset)> Records = new();
    }

    public class McapChunkIndex
    {
        public ulong MessageStartTime;
        public ulong MessageEndTime;
        public ulong ChunkStartOffset;
        public ulong ChunkLength;
        public Dictionary<ushort, ulong> MessageIndexOffsets = new();
        public ulong MessageIndexLength;
        public string Compression;
        public ulong CompressedSize;
        public ulong UncompressedSize;
    }

    public class McapStatistics
    {
        public ulong MessageCount;
        public ushort SchemaCount;
        public uint ChannelCount;
        public uint AttachmentCount;
        public uint MetadataCount;
        public uint ChunkCount;
        public ulong MessageStartTime;
        public ulong MessageEndTime;
        public Dictionary<ushort, ulong> ChannelMessageCounts = new();
    }

    public class McapMetadata
    {
        public string Name;
        public Dictionary<string, string> Metadata = new();
    }

    public class McapMetadataIndex
    {
        public ulong Offset;
        public ulong Length;
        public string Name;
    }

    public class McapFooter
    {
        public ulong SummaryStart;
        public ulong SummaryOffsetStart;
        public uint SummaryCrc;
    }

    public class McapFileSummary
    {
        public List<McapSchema> Schemas = new();
        public List<McapChannel> Channels = new();
        public McapStatistics Statistics;
        public List<McapChunkIndex> ChunkIndexes = new();
        public List<McapMetadataIndex> MetadataIndexes = new();
    }
}
