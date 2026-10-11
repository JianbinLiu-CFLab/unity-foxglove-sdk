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
        /// Write a client-published message to the current chunk, lazily creating
        /// the channel record on first use.
        /// </summary>
        public void WriteClientMessage(uint clientId, uint chId, ulong logNs, byte[] payload, string topic,
            string enc = "json", string sName = "", string sEnc = "", string sContent = "")
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                var key = (clientId, chId);
                if (_skippedClientChannels.Contains(key)) return;
                if (!_clientChannelWriteState.TryGetValue(key, out var map))
                {
                    var messageEncoding = NormalizeMessageEncoding(enc);
                    var signature = CreateTopicSignature(messageEncoding, sName, sEnc, sContent);
                    if (TryReuseExistingTopicChannel(
                            topic,
                            McapChannelDirection.Input,
                            signature,
                            sContent,
                            out map))
                    {
                        _clientChannelWriteState[key] = map;
                    }
                    else
                    {
                        if (WouldMixTopicSignature(topic, signature)
                            && !TryGetCompatibleTopicSchemaId(topic, signature, sContent, out _))
                        {
                            _skippedClientChannels.Add(key);
                            _log.LogWarning(
                                $"MCAP: skipping client-published topic '{topic}' because its schema signature is incompatible with an existing recorded channel.");
                            return;
                        }

                        var sid = TryGetCompatibleTopicSchemaId(topic, signature, sContent, out var compatibleSchemaId)
                            ? compatibleSchemaId
                            : GetOrCreateSchema(sName, sEnc, sContent);
                        if (_recordingFailed) return;
                        if (_nextChannelId == 0) { Fail("Channel ID overflow"); return; }
                        var mcapId = _nextChannelId++;
                        map = new ChannelWriteState
                        {
                            McapId = mcapId,
                            SchemaId = sid,
                            Topic = topic,
                            Signature = signature
                        };
                        var meta = CreateChannelMetadata(McapChannelDirection.Input);
                        var channelRecordStart = _writer.Position;
                        try
                        {
                            _writer.WriteChannel(mcapId, sid, topic, messageEncoding, meta);
                        }
                        catch (Exception ex)
                        {
                            HandleTopLevelRecordWriteFailure(channelRecordStart, ex, "Client channel");
                            throw;
                        }
                        _clientChannelWriteState[key] = map;
                        if (!_topicChannelWriteState.ContainsKey((topic, McapChannelDirection.Input)))
                            _topicChannelWriteState[(topic, McapChannelDirection.Input)] = map;
                        _channels.Add(new ChannelRecordState { Id = mcapId, SchemaId = sid, Topic = topic, Encoding = messageEncoding, Metadata = SnapshotChannelMetadata(meta) });
                        RecordTopicSignature(topic, signature);
                    }
                }
                WriteMessageToChannelWriteState(map, logNs, payload);
            }
        }

        // Message writing
        private void WriteMessageToChannelWriteState(ChannelWriteState map, ulong logNs, byte[] payload)
        {
            WriteMessageToChannelWriteState(map, map.Seq, logNs, logNs, payload, advanceSequence: true);
        }

        private void WriteMessageToChannelWriteState(
            ChannelWriteState map,
            uint seq,
            ulong logNs,
            ulong publishNs,
            byte[] payload,
            bool advanceSequence)
        {
            if (_recordingFailed || _closed) return;
            var payloadLength = payload?.Length ?? 0;
            if (!_options.UseChunking)
            {
                var recordStartPosition = _writer.Position;
                try
                {
                    _writer.WriteMessage(map.McapId, seq, logNs, publishNs, payload);
                }
                catch (Exception writeError)
                {
                    try
                    {
                        _writer.TruncateToPosition(recordStartPosition);
                    }
                    catch (Exception rollbackError)
                    {
                        _closed = true;
                        Fail("Direct message write and rollback failed: " + rollbackError.Message);
                        throw new IOException(
                            "MCAP recorder could not roll back an incomplete direct Message record.",
                            new AggregateException(writeError, rollbackError));
                    }

                    throw;
                }

                if (advanceSequence)
                    map.Seq++;
                map.MsgCount++;
                TrackMessageTimes(logNs);
                return;
            }

            const int messagePrefixLength = 2 + 4 + 8 + 8;
            if (payloadLength > int.MaxValue - messagePrefixLength - McapWriter.RecordHeaderLength)
            {
                Fail("Message payload is too large for a single MCAP record.");
                return;
            }

            var contentLength = checked(messagePrefixLength + payloadLength);
            var recordLength = checked(McapWriter.RecordHeaderLength + contentLength);
            if (!IsChunkedMessageRecordWithinLimit(
                    payloadLength,
                    McapReader.DefaultChunkUncompressedSizeLimit))
            {
                Fail(
                    $"Message record size {recordLength} exceeds the paired reader chunk limit {McapReader.DefaultChunkUncompressedSizeLimit}.");
                return;
            }
            FlushChunkBeforeLargeWriteIfNeeded(recordLength);
            var off = (ulong)_chunkBuf.Position;
            var header = _messageRecordHeader;
            header[0] = McapWriter.OpcodeMessage;
            McapWriter.WriteU64(header, 1, (ulong)contentLength);
            McapWriter.WriteU16(header, McapWriter.RecordHeaderLength, map.McapId);
            McapWriter.WriteU32(header, McapWriter.RecordHeaderLength + 2, seq);
            McapWriter.WriteU64(header, McapWriter.RecordHeaderLength + 2 + 4, logNs);
            McapWriter.WriteU64(header, McapWriter.RecordHeaderLength + 2 + 4 + 8, publishNs);
            _chunkBuf.Write(header, 0, header.Length);
            if (payloadLength > 0)
                _chunkBuf.Write(payload, 0, payloadLength);
            if (advanceSequence)
                map.Seq++;
            map.MsgCount++;
            map.Pending.Add((logNs, off));
            if (_msgSt == ulong.MaxValue || logNs < _msgSt) _msgSt = logNs;
            if (logNs > _msgEt) _msgEt = logNs;
            if (!_chunkHasMessages)
            {
                _chunkSt = logNs;
                _chunkEt = logNs;
                _chunkHasMessages = true;
            }
            else
            {
                if (logNs < _chunkSt) _chunkSt = logNs;
                if (logNs > _chunkEt) _chunkEt = logNs;
            }
            _msgCount++;
            if (_chunkBuf.Length >= _chunkSz) FlushChunk();
        }

        private void TrackMessageTimes(ulong logNs)
        {
            if (_msgSt == ulong.MaxValue || logNs < _msgSt) _msgSt = logNs;
            if (logNs > _msgEt) _msgEt = logNs;
            _msgCount++;
        }

    }
}
