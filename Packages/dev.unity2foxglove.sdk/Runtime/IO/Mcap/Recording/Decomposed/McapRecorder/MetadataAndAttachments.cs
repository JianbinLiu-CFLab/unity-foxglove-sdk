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
    public partial class McapRecorder
    {
        /// <summary>
        /// Write a standalone metadata record to the MCAP file.
        /// </summary>
        public void WriteMetadata(string name, string jsonValue)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                var off = (ulong)_writer.Position;
                try
                {
                    _writer.WriteMetadata(name, new Dictionary<string, string> { ["value"] = jsonValue });
                }
                catch (Exception ex)
                {
                    HandleTopLevelRecordWriteFailure((long)off, ex, "Metadata");
                    throw;
                }
                var len = (ulong)_writer.Position - off;
                _metaIdx.Add(new MetadataIndexState { Offset = off, Length = len, Name = name });
                _metadataCount++;
            }
        }

        private void RecordComponentContract(
            string topic,
            string encoding,
            string logicalSchema,
            string shapeIdentity)
        {
            if (string.IsNullOrEmpty(topic)
                || !string.Equals(encoding, "msgpack", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(logicalSchema)
                || string.IsNullOrEmpty(shapeIdentity))
                return;

            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                foreach (var existing in _recordedComponentContracts)
                {
                    if (string.Equals(existing.Topic, topic, StringComparison.Ordinal)
                        && string.Equals(existing.Encoding, encoding, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.LogicalSchema, logicalSchema, StringComparison.Ordinal)
                        && string.Equals(existing.ShapeIdentity, shapeIdentity, StringComparison.Ordinal))
                        return;
                }

                _recordedComponentContracts.Add(new SdkWireSchemaComponentIdentity
                {
                    Topic = topic,
                    Encoding = encoding,
                    LogicalSchema = logicalSchema,
                    ShapeIdentity = shapeIdentity
                });
            }
        }

        internal IReadOnlyList<SdkWireSchemaComponentIdentity> GetRecordedComponentContractSnapshot()
        {
            lock (_lock)
            {
                return _recordedComponentContracts
                    .Select(component => new SdkWireSchemaComponentIdentity
                    {
                        Topic = component.Topic,
                        Encoding = component.Encoding,
                        LogicalSchema = component.LogicalSchema,
                        ShapeIdentity = component.ShapeIdentity
                    })
                    .ToList();
            }
        }

        internal IReadOnlyList<SchemaEntry> GetRecordedSchemaSnapshot()
        {
            lock (_lock)
            {
                var snapshot = new List<SchemaEntry>(_schemas.Count);
                foreach (var schema in _schemas)
                {
                    if (!_outputSchemaIds.Contains(schema.Id))
                        continue;
                    snapshot.Add(new SchemaEntry
                    {
                        Name = schema.Name,
                        Encoding = schema.Encoding,
                        Content = schema.Encoding == "protobuf"
                            ? Convert.ToBase64String(schema.Data ?? Array.Empty<byte>())
                            : System.Text.Encoding.UTF8.GetString(schema.Data ?? Array.Empty<byte>()),
                        RawContent = schema.Encoding == "protobuf" && schema.Data != null
                            ? (byte[])schema.Data.Clone()
                            : null
                    });
                }

                return snapshot;
            }
        }

        /// <summary>
        /// Write an attachment outside chunks. Flushes the active chunk first.
        /// Safe no-op if recording failed or already closed.
        /// </summary>
        public void AddAttachment(string name, string mediaType, byte[] data, ulong logTimeNs, ulong createTimeNs = 0)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                FlushChunk();
                var attachmentRecordStart = _writer.Position;
                McapAttachmentIndex index;
                try
                {
                    index = _writer.WriteAttachment(logTimeNs, createTimeNs, name, mediaType, data, _options.EnableCrcs);
                }
                catch (Exception ex)
                {
                    HandleTopLevelRecordWriteFailure(attachmentRecordStart, ex, "Attachment");
                    throw;
                }
                _attachmentIdx.Add(index);
                _attachmentCount++;
            }
        }

        /// <summary>
        /// Write a server-side message by Foxglove channel ID to the current chunk.
        /// </summary>
        public void WriteMessage(uint fId, ulong logNs, byte[] payload)
            => WriteMessage(fId, logNs, payload, string.Empty, string.Empty, string.Empty);

        internal void WriteMessage(
            uint fId,
            ulong logNs,
            byte[] payload,
            string encoding,
            string logicalSchema,
            string shapeIdentity)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed || !_serverChannelWriteStates.TryGetValue(fId, out var map)) return;
                var previousCount = map.MsgCount;
                WriteMessageToChannelWriteState(map, logNs, payload);
                if (map.MsgCount > previousCount)
                    RecordComponentContract(map.Topic, encoding, logicalSchema, shapeIdentity);
            }
        }

        /// <summary>
        /// Writes a server message while preserving the source MCAP sequence
        /// and publish timestamp. Used by lossless range re-emission paths;
        /// ordinary live recording continues to derive both values locally.
        /// </summary>
        internal void WriteMessagePreservingMcapMetadata(
            uint fId,
            uint sequence,
            ulong logNs,
            ulong publishNs,
            byte[] payload)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed || !_serverChannelWriteStates.TryGetValue(fId, out var map)) return;
                WriteMessageToChannelWriteState(map, sequence, logNs, publishNs, payload, advanceSequence: false);
            }
        }

        // Lifecycle
    }
}
