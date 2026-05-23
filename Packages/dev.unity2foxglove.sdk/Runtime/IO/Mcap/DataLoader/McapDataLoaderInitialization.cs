// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Initialization DTOs returned by the local MCAP DataLoader facade.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Summary returned after opening and indexing one local MCAP file.</summary>
    public sealed class McapDataLoaderInitialization
    {
        /// <summary>Channels declared in the MCAP summary.</summary>
        public List<McapDataLoaderChannel> Channels = new List<McapDataLoaderChannel>();

        /// <summary>Schemas declared in the MCAP summary.</summary>
        public List<McapDataLoaderSchema> Schemas = new List<McapDataLoaderSchema>();

        /// <summary>Inclusive log-time range reported by summary statistics or chunk indexes.</summary>
        public McapDataLoaderTimeRange TimeRange = new McapDataLoaderTimeRange();

        /// <summary>Metadata index summaries available for targeted metadata reads.</summary>
        public List<McapDataLoaderMetadataIndex> MetadataIndexes = new List<McapDataLoaderMetadataIndex>();

        /// <summary>Attachment index summaries available without loading attachment payloads.</summary>
        public List<McapDataLoaderAttachmentIndex> AttachmentIndexes = new List<McapDataLoaderAttachmentIndex>();

        /// <summary>True when summary statistics include a total message count.</summary>
        public bool HasTotalMessageCount;

        /// <summary>Total message count from MCAP summary statistics.</summary>
        public ulong TotalMessageCount;

        /// <summary>Diagnostics discovered while opening and evaluating the local MCAP file.</summary>
        public List<McapDataLoaderProblem> Problems = new List<McapDataLoaderProblem>();
    }

    /// <summary>Metadata index summary exposed without reading arbitrary metadata payloads.</summary>
    public sealed class McapDataLoaderMetadataIndex
    {
        /// <summary>MCAP metadata record name.</summary>
        public string Name;

        /// <summary>Byte offset of the metadata record.</summary>
        public ulong Offset;

        /// <summary>Serialized metadata record length in bytes.</summary>
        public ulong Length;

        /// <summary>Creates an empty metadata index summary.</summary>
        public McapDataLoaderMetadataIndex()
        {
            Name = string.Empty;
        }
    }

    /// <summary>Attachment index summary exposed without reading attachment payloads.</summary>
    public sealed class McapDataLoaderAttachmentIndex
    {
        /// <summary>Attachment name recorded in the MCAP file.</summary>
        public string Name;

        /// <summary>Attachment media type recorded in the MCAP file.</summary>
        public string MediaType;

        /// <summary>Byte offset of the attachment record.</summary>
        public ulong Offset;

        /// <summary>Serialized attachment record length in bytes.</summary>
        public ulong Length;

        /// <summary>Attachment log timestamp in nanoseconds.</summary>
        public ulong LogTime;

        /// <summary>Attachment creation timestamp in nanoseconds.</summary>
        public ulong CreateTime;

        /// <summary>Attachment payload size in bytes.</summary>
        public ulong DataSize;

        /// <summary>Creates an empty attachment index summary.</summary>
        public McapDataLoaderAttachmentIndex()
        {
            Name = string.Empty;
            MediaType = string.Empty;
        }
    }
}
