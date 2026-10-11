// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: High-level MCAP recorder that wraps McapWriter. Handles chunk
// management, schema/channel deduplication, metadata indexes, compression,
// and final summary/statistics output on close. Attaches to FoxgloveSession
// via dual-write hooks so live publish data is simultaneously recorded.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// MCAP recorder that attaches to a FoxgloveSession via dual-write hooks.
    /// Manages chunk lifecycle, schema/channel deduplication, metadata
    /// indexes, and final summary/statistics output on close.
    /// </summary>
    /// <remarks>Phase192 source sections: ChannelAdmission.cs, MessageWriting.cs, MetadataAndAttachments.cs, CloseAndRecovery.cs, ChunkLifecycle.cs</remarks>
    public partial class McapRecorder : IDisposable
    {
        private readonly McapWriter _writer;
        private readonly IFoxgloveLogger _log;
        private readonly McapWriterOptions _options;
        private readonly string _compression;
        private readonly Dictionary<(string name, string enc, string hash), ushort> _schemaIdsBySignature = new();
        private readonly Dictionary<(uint clientId, uint chId), ChannelWriteState> _clientChannelWriteState = new();
        private readonly HashSet<(uint clientId, uint chId)> _skippedClientChannels = new();
        private readonly Dictionary<uint, ChannelWriteState> _serverChannelWriteStates = new();
        private readonly Dictionary<uint, (string Topic, TopicSignature Signature)> _rejectedServerChannelAdmissions = new();
        private readonly Dictionary<(string topic, McapChannelDirection direction), ChannelWriteState> _topicChannelWriteState = new();
        private readonly Dictionary<string, TopicSignature> _topicSignatures = new();
        private readonly HashSet<ushort> _seenChannelIds = new();
        private readonly List<ChannelWriteState> _allChannelWriteStates = new();
        private readonly Dictionary<ushort, ulong> _messageIndexOffsetsScratch = new();
        private readonly List<SchemaRecordState> _schemas = new();
        private readonly HashSet<ushort> _outputSchemaIds = new();
        private readonly List<SdkWireSchemaComponentIdentity> _recordedComponentContracts = new();
        private readonly List<ChannelRecordState> _channels = new();
        private readonly List<ChunkIndexState> _chunkIdx = new();
        private readonly List<MetadataIndexState> _metaIdx = new();
        private readonly List<McapAttachmentIndex> _attachmentIdx = new();
        private uint _attachmentCount;
        private readonly MemoryStream _chunkBuf;
        private readonly MemoryStream _compressionBuf = new();
        private readonly byte[] _messageRecordHeader = new byte[McapWriter.RecordHeaderLength + 2 + 4 + 8 + 8];
        private readonly object _lock = new object();
        private Compressor _zstdCompressor;
        private byte[] _zstdCompressionBuffer;
        private ushort _nextSchemaId = 1;
        private ushort _nextChannelId = 1;
        private ulong _chunkSt, _chunkEt;
        private ulong _msgSt = ulong.MaxValue, _msgEt;
        private ulong _msgCount, _chunkCount;
        private uint _metadataCount;
        private bool _chunkHasMessages;
        private bool _closed, _recordingFailed, _disposed;
        private long? _failedChunkStartPosition;
        private Exception _chunkFlushFailure;
        private readonly int _chunkSz;

        /// <summary>
        /// Default chunk size in bytes (1 MiB).
        /// </summary>
        public const int DefaultChunkSizeBytes = McapWriterOptions.DefaultChunkSizeBytes;

        /// <summary>
        /// Creates a new MCAP recorder writing to the given stream.
        /// Optional compression controls per-chunk compression (e.g. "zstd").
        /// </summary>
        public McapRecorder(Stream stream, IFoxgloveLogger logger = null, int chunkSizeBytes = DefaultChunkSizeBytes, string compression = "", bool leaveOpen = true)
            : this(stream, logger, new McapWriterOptions { ChunkSizeBytes = chunkSizeBytes, Compression = compression }, leaveOpen)
        {
        }

        /// <summary>
        /// Creates a new MCAP recorder with advanced writer options.
        /// </summary>
        public McapRecorder(Stream stream, IFoxgloveLogger logger, McapWriterOptions options, bool leaveOpen = true)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek)
                throw new NotSupportedException("MCAP recorder requires a seekable output stream.");

            _log = logger ?? new ConsoleLogger();
            _options = McapWriterOptions.Normalize(options);
            _writer = new McapWriter(stream, leaveOpen);
            _chunkSz = _options.ChunkSizeBytes;
            _chunkBuf = new MemoryStream(_chunkSz);
            _compression = _options.Compression;
            try
            {
                _writer.WriteMagic();
                _writer.WriteHeader("", "unity-foxglove-sdk");
            }
            catch
            {
                _writer.Dispose();
                throw;
            }
        }

        /// <summary>Metadata key for the external coordinate convention of a channel payload.</summary>
        public const string CoordinateModeMetadataKey = "coordinate_mode";

        /// <summary>Metadata key for the data direction represented by a channel payload.</summary>
        public const string DataDirectionMetadataKey = "unity2foxglove.direction";

        /// <summary>
        /// Compatibility coordinate setting. Reading returns the output convention;
        /// assigning it configures both directions for legacy one-value callers.
        /// </summary>
        public string CoordinateMode
        {
            get => OutputCoordinateMode;
            set
            {
                OutputCoordinateMode = value;
                InputCoordinateMode = value;
            }
        }

        /// <summary>Coordinate convention of new Unity-to-external output channels.</summary>
        public string OutputCoordinateMode { get; set; }

        /// <summary>Coordinate convention of new external-to-Unity input channels.</summary>
        public string InputCoordinateMode { get; set; }

    }
}
