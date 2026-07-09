// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: Nested state types for McapRecorder.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    public partial class McapRecorder
    {
        /// <summary>
        /// Immutable signature combining encoding, schema name, schema encoding,
        /// and content hash. Used to detect incompatible topic schema conflicts.
        /// </summary>
        struct TopicSignature : IEquatable<TopicSignature>
        {
            /// <summary>Message encoding (e.g. "json", "protobuf").</summary>
            public string Encoding;
            /// <summary>Schema name.</summary>
            public string SchemaName;
            /// <summary>Schema encoding (e.g. "jsonschema").</summary>
            public string SchemaEncoding;
            /// <summary>Hex-encoded SHA-256 hash of schema content.</summary>
            public string Hash;

            public bool Equals(TopicSignature other) =>
                Encoding == other.Encoding &&
                SchemaName == other.SchemaName &&
                SchemaEncoding == other.SchemaEncoding &&
                Hash == other.Hash;

            public override bool Equals(object obj) =>
                obj is TopicSignature other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(Encoding, SchemaName, SchemaEncoding, Hash);
        }

        /// <summary>
        /// Per-channel write accumulator tracking MCAP channel ID, sequence
        /// number, durable message count, and pending index entries for the current chunk.
        /// </summary>
        class ChannelWriteState
        {
            /// <summary>MCAP channel ID.</summary>
            public ushort McapId;
            /// <summary>Topic name.</summary>
            public string Topic;
            /// <summary>Per-channel MCAP message sequence number. Wrap-around is allowed by MCAP.</summary>
            public uint Seq;
            /// <summary>Total messages recorded for statistics; kept separate from wrapping Seq.</summary>
            public ulong MsgCount;
            /// <summary>Pending (log-time, chunk-offset) entries for the chunk message index.</summary>
            public List<(ulong LogTime, ulong Offset)> Pending = new();
        }

        /// <summary>
        /// Schema record captured for the summary section.
        /// </summary>
        struct SchemaRecordState
        {
            /// <summary>Schema ID.</summary>
            public ushort Id;
            /// <summary>Schema name.</summary>
            public string Name;
            /// <summary>Schema encoding (e.g. "jsonschema", "protobuf").</summary>
            public string Encoding;
            /// <summary>Raw schema content bytes.</summary>
            public byte[] Data;
        }

        /// <summary>
        /// Channel record captured for the summary section.
        /// </summary>
        struct ChannelRecordState
        {
            /// <summary>Channel ID.</summary>
            public ushort Id;
            /// <summary>Referenced schema ID.</summary>
            public ushort SchemaId;
            /// <summary>Topic name.</summary>
            public string Topic;
            /// <summary>Message encoding string.</summary>
            public string Encoding;
            /// <summary>Optional metadata key-value pairs.</summary>
            public Dictionary<string, string> Metadata;
        }

        /// <summary>
        /// Chunk index entry backed up for the summary section.
        /// </summary>
        struct ChunkIndexState
        {
            /// <summary>Earliest log time in the chunk.</summary>
            public ulong StartTime;
            /// <summary>Latest log time in the chunk.</summary>
            public ulong EndTime;
            /// <summary>File offset of the chunk record.</summary>
            public ulong Offset;
            /// <summary>Chunk record length in bytes.</summary>
            public ulong Length;
            /// <summary>Total size of the message index records following the chunk.</summary>
            public ulong MessageIndexLength;
            /// <summary>Compressed chunk data size in bytes.</summary>
            public ulong CompressedSize;
            /// <summary>Uncompressed chunk data size in bytes.</summary>
            public ulong UncompressedSize;
            /// <summary>Compression algorithm name (empty for none).</summary>
            public string Compression;
            /// <summary>Per-channel offset map into the message index records.</summary>
            public Dictionary<ushort, ulong> MessageIndexOffsets;
        }

        /// <summary>
        /// Metadata index entry backed up for the summary section.
        /// </summary>
        struct MetadataIndexState
        {
            /// <summary>File offset of the metadata record.</summary>
            public ulong Offset;
            /// <summary>Metadata record byte length.</summary>
            public ulong Length;
            /// <summary>Metadata name.</summary>
            public string Name;
        }
    }
}
