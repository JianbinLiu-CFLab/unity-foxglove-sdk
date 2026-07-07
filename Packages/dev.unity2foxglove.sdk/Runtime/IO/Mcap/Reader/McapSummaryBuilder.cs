// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Reader
// Purpose: MCAP summary and data-section aggregation helpers.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapSummaryBuilder
    {
        private readonly McapFileSummary _summary;
        private readonly bool _collectInventory;
        private readonly bool _collectMessages;
        private readonly McapSequentialReadLimits _sequentialLimits;
        private readonly Dictionary<ushort, ulong> _channelMessageCounts = new Dictionary<ushort, ulong>();

        private ulong _messageCount;
        private long _retainedPayloadBytes;
        private uint _attachmentCount;
        private uint _metadataCount;
        private uint _chunkCount;
        private ulong _messageStart = ulong.MaxValue;
        private ulong _messageEnd;

        internal McapSummaryBuilder(
            ulong dataSectionEndOffset,
            bool collectInventory,
            bool collectMessages,
            McapSequentialReadLimits sequentialLimits)
        {
            if (collectMessages)
            {
                sequentialLimits = sequentialLimits ?? McapSequentialReadLimits.Default;
                sequentialLimits.Validate();
            }

            _summary = new McapFileSummary
            {
                DataSectionEndOffset = dataSectionEndOffset
            };
            _collectInventory = collectInventory;
            _collectMessages = collectMessages;
            _sequentialLimits = sequentialLimits;
        }

        internal static McapFileSummary FromSummarySection(
            byte[] summaryBytes,
            ulong summaryStart,
            ulong summaryOffsetStart,
            uint summaryCrc,
            ulong recordSizeLimit)
        {
            if (summaryBytes == null)
                throw new ArgumentNullException(nameof(summaryBytes));

            var summary = new McapFileSummary
            {
                DataSectionEndOffset = summaryStart
            };

            var summaryOffset = 0;
            while (summaryOffset < summaryBytes.Length)
            {
                var recordStart = summaryStart + (ulong)summaryOffset;
                var op = summaryBytes[summaryOffset++];
                if (op == 0x00)
                    throw new InvalidDataException("MCAP opcode 0x00 is invalid in summary.");
                var contentLength = McapBinaryReader.ReadU64LE(summaryBytes, ref summaryOffset);
                if (contentLength > recordSizeLimit)
                    throw new InvalidDataException($"Record content length {contentLength} exceeds limit {recordSizeLimit}");
                if (contentLength > int.MaxValue)
                    throw new InvalidDataException($"Record content length {contentLength} exceeds int.MaxValue");
                var recordLength = (int)contentLength;
                if (recordLength > summaryBytes.Length - summaryOffset)
                    throw new InvalidDataException($"MCAP summary record at offset {recordStart} extends past the footer.");

                switch (op)
                {
                    case McapWriter.OpcodeSchema:
                        summary.Schemas.Add(McapRecordDecoder.DecodeSchema(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeChannel:
                        summary.Channels.Add(McapRecordDecoder.DecodeChannel(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        summary.ChunkIndexes.Add(McapRecordDecoder.DecodeChunkIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeStatistics:
                        summary.Statistics = McapRecordDecoder.DecodeStatistics(summaryBytes, summaryOffset, recordLength);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        summary.MetadataIndexes.Add(McapRecordDecoder.DecodeMetadataIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeAttachment:
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        summary.AttachmentIndexes.Add(McapRecordDecoder.DecodeAttachmentIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeSummaryOffset:
                        break;
                    default:
                        break; // unknown, skip
                }

                summaryOffset += recordLength;
            }

            ValidateSummaryCrc(summaryBytes, summaryStart, summaryOffsetStart, summaryCrc);
            return summary;
        }

        internal static void ValidateSummaryCrc(
            byte[] summaryBytes,
            ulong summaryStart,
            ulong summaryOffsetStart,
            uint summaryCrc)
        {
            if (summaryCrc == 0)
                return;

            var footerPrefix = McapWriter.BuildFooterCrcPrefix(summaryStart, summaryOffsetStart);

            var crc = Crc32Helper.Initialize();
            crc = Crc32Helper.Update(crc, summaryBytes);
            crc = Crc32Helper.Update(crc, footerPrefix);
            var recomputed = Crc32Helper.Finalize(crc);
            if (recomputed != summaryCrc)
                throw new InvalidDataException("MCAP summary CRC mismatch");
        }

        internal bool ApplyRecord(
            byte opcode,
            byte[] content,
            int contentLength,
            ulong recordStart,
            ulong recordEnd,
            bool validateCrcs,
            ulong chunkUncompressedSizeLimit)
        {
            switch (opcode)
            {
                case McapWriter.OpcodeHeader:
                    throw new InvalidDataException("MCAP Header record appeared after the first data-section record.");
                case McapWriter.OpcodeSchema:
                    if (_collectInventory)
                        McapRecordDecoder.AddSchema(_summary.Schemas, McapRecordDecoder.DecodeSchema(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeChannel:
                    if (_collectInventory)
                        McapRecordDecoder.AddChannel(_summary.Channels, McapRecordDecoder.DecodeChannel(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeMessage:
                    ApplyMessage(content, contentLength);
                    break;
                case McapWriter.OpcodeChunk:
                    _chunkCount++;
                    var records = McapChunkReader.DecodeChunkRecordsContent(
                        content,
                        0,
                        contentLength,
                        out var crcValid,
                        chunkUncompressedSizeLimit);
                    McapChunkReader.EnsureCrcValid(crcValid, validateCrcs);
                    McapRecordDecoder.ScanChunkRecords(
                        records,
                        _summary,
                        _collectInventory,
                        _collectMessages,
                        _sequentialLimits,
                        ref _retainedPayloadBytes,
                        ref _messageCount,
                        ref _messageStart,
                        ref _messageEnd,
                        _channelMessageCounts);
                    break;
                case McapWriter.OpcodeAttachment:
                    _attachmentCount++;
                    if (_collectInventory)
                    {
                        var attachment = McapRecordDecoder.DecodeAttachment(content, 0, contentLength);
                        _summary.AttachmentIndexes.Add(new McapAttachmentIndex
                        {
                            Offset = recordStart,
                            Length = recordEnd - recordStart,
                            LogTime = attachment.LogTime,
                            CreateTime = attachment.CreateTime,
                            DataSize = (ulong)(attachment.Data?.Length ?? 0),
                            Name = attachment.Name,
                            MediaType = attachment.MediaType
                        });
                    }
                    break;
                case McapWriter.OpcodeMetadata:
                    _metadataCount++;
                    if (_collectInventory)
                    {
                        var metadata = McapRecordDecoder.DecodeMetadata(content, 0, contentLength);
                        _summary.MetadataIndexes.Add(new McapMetadataIndex
                        {
                            Offset = recordStart,
                            Length = recordEnd - recordStart,
                            Name = metadata.Name
                        });
                    }
                    break;
                case McapWriter.OpcodeDataEnd:
                    McapRecordDecoder.DecodeDataEnd(content, 0, contentLength);
                    return false;
                default:
                    break;
            }

            return true;
        }

        internal McapFileSummary Build()
        {
            _summary.Statistics = new McapStatistics
            {
                MessageCount = _messageCount,
                SchemaCount = (ushort)_summary.Schemas.Count,
                ChannelCount = (uint)_summary.Channels.Count,
                AttachmentCount = _attachmentCount,
                MetadataCount = _metadataCount,
                ChunkCount = _chunkCount,
                MessageStartTime = _messageCount > 0 ? _messageStart : 0,
                MessageEndTime = _messageCount > 0 ? _messageEnd : 0,
                ChannelMessageCounts = _channelMessageCounts
            };

            return _summary;
        }

        private void ApplyMessage(byte[] content, int contentLength)
        {
            if (_collectMessages)
            {
                McapRecordDecoder.AddSequentialMessage(
                    _summary,
                    McapRecordDecoder.DecodeMessage(content, 0, contentLength),
                    _sequentialLimits,
                    ref _retainedPayloadBytes,
                    ref _messageCount,
                    ref _messageStart,
                    ref _messageEnd,
                    _channelMessageCounts);
            }
            else
            {
                McapRecordDecoder.AddMessageStats(
                    McapRecordDecoder.DecodeMessageHeader(content, 0, contentLength),
                    ref _messageCount,
                    ref _messageStart,
                    ref _messageEnd,
                    _channelMessageCounts);
            }
        }
    }
}
