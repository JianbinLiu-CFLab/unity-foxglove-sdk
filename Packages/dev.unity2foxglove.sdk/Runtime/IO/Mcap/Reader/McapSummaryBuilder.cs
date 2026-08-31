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
            ulong recordSizeLimit,
            bool validateCrcs = true)
        {
            if (summaryBytes == null)
                throw new ArgumentNullException(nameof(summaryBytes));

            var summary = new McapFileSummary
            {
                DataSectionEndOffset = summaryStart
            };

            var summaryOffsetBoundary = summaryBytes.Length;
            if (summaryOffsetStart != 0)
            {
                if (summaryOffsetStart < summaryStart ||
                    summaryOffsetStart - summaryStart > (ulong)summaryBytes.Length)
                    throw new InvalidDataException("MCAP summary_offset_start is outside the supplied summary bytes.");
                summaryOffsetBoundary = (int)(summaryOffsetStart - summaryStart);
            }

            var groups = new Dictionary<byte, SummaryGroupRange>();
            var completedGroups = new HashSet<byte>();
            var summaryOffsets = new Dictionary<byte, SummaryGroupRange>();
            byte currentGroupOpcode = 0;
            var currentGroupStart = 0;
            var sawSummaryOffset = false;
            var sawStatistics = false;

            var summaryOffset = 0;
            while (summaryOffset < summaryBytes.Length)
            {
                var relativeRecordStart = summaryOffset;
                var recordStart = summaryStart + (ulong)relativeRecordStart;
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

                var relativeRecordEnd = summaryOffset + recordLength;
                var inSummaryOffsetSection = summaryOffsetStart != 0 &&
                                             relativeRecordStart >= summaryOffsetBoundary;
                if (!inSummaryOffsetSection && relativeRecordEnd > summaryOffsetBoundary)
                    throw new InvalidDataException("MCAP summary_offset_start splits a summary record.");

                if (inSummaryOffsetSection)
                {
                    sawSummaryOffset = true;
                    if (op != McapWriter.OpcodeSummaryOffset)
                        throw new InvalidDataException("MCAP Summary Offset section contains a non-Summary-Offset record.");

                    var decodedOffset = McapRecordDecoder.DecodeSummaryOffset(
                        summaryBytes,
                        summaryOffset,
                        recordLength);
                    if (decodedOffset.GroupOpcode == 0 ||
                        summaryOffsets.ContainsKey(decodedOffset.GroupOpcode))
                        throw new InvalidDataException("MCAP Summary Offset section contains an invalid or duplicate group opcode.");
                    summaryOffsets.Add(
                        decodedOffset.GroupOpcode,
                        new SummaryGroupRange(decodedOffset.GroupStart, decodedOffset.GroupLength));
                    summaryOffset = relativeRecordEnd;
                    continue;
                }

                if (op == McapWriter.OpcodeSummaryOffset)
                    throw new InvalidDataException("MCAP Summary Offset record appears outside the Summary Offset section.");
                if (IsKnownDataOnlyOpcode(op))
                    throw new InvalidDataException($"MCAP opcode 0x{op:X2} is not allowed in the Summary section.");

                if (currentGroupOpcode == 0)
                {
                    currentGroupOpcode = op;
                    currentGroupStart = relativeRecordStart;
                }
                else if (currentGroupOpcode != op)
                {
                    CompleteGroup(
                        groups,
                        currentGroupOpcode,
                        currentGroupStart,
                        relativeRecordStart,
                        summaryStart);
                    completedGroups.Add(currentGroupOpcode);
                    if (completedGroups.Contains(op))
                        throw new InvalidDataException($"MCAP Summary records for opcode 0x{op:X2} are not contiguous.");
                    currentGroupOpcode = op;
                    currentGroupStart = relativeRecordStart;
                }

                switch (op)
                {
                    case McapWriter.OpcodeSchema:
                        McapRecordDecoder.AddSchema(
                            summary.Schemas,
                            McapRecordDecoder.DecodeSchema(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeChannel:
                        McapRecordDecoder.AddChannel(
                            summary.Channels,
                            McapRecordDecoder.DecodeChannel(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeChunkIndex:
                        summary.ChunkIndexes.Add(McapRecordDecoder.DecodeChunkIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeStatistics:
                        if (sawStatistics)
                            throw new InvalidDataException("MCAP Summary contains more than one Statistics record.");
                        sawStatistics = true;
                        summary.Statistics = McapRecordDecoder.DecodeStatistics(summaryBytes, summaryOffset, recordLength);
                        break;
                    case McapWriter.OpcodeMetadataIndex:
                        summary.MetadataIndexes.Add(McapRecordDecoder.DecodeMetadataIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    case McapWriter.OpcodeAttachmentIndex:
                        summary.AttachmentIndexes.Add(McapRecordDecoder.DecodeAttachmentIndex(summaryBytes, summaryOffset, recordLength));
                        break;
                    default:
                        break; // Future or private summary record; preserve cursor compatibility.
                }

                summaryOffset = relativeRecordEnd;
            }

            if (currentGroupOpcode != 0)
            {
                CompleteGroup(
                    groups,
                    currentGroupOpcode,
                    currentGroupStart,
                    summaryOffsetBoundary,
                    summaryStart);
            }

            if (summaryOffsetStart != 0 && !sawSummaryOffset)
                throw new InvalidDataException("MCAP footer declares an empty Summary Offset section.");

            foreach (var pair in summaryOffsets)
            {
                if (!groups.TryGetValue(pair.Key, out var actual) ||
                    actual.Start != pair.Value.Start ||
                    actual.Length != pair.Value.Length)
                    throw new InvalidDataException($"MCAP Summary Offset for opcode 0x{pair.Key:X2} does not match its summary group.");
            }

            if (validateCrcs)
                ValidateSummaryCrc(summaryBytes, summaryStart, summaryOffsetStart, summaryCrc);
            return summary;
        }

        private static void CompleteGroup(
            Dictionary<byte, SummaryGroupRange> groups,
            byte opcode,
            int relativeStart,
            int relativeEnd,
            ulong summaryStart)
        {
            groups.Add(
                opcode,
                new SummaryGroupRange(
                    summaryStart + (ulong)relativeStart,
                    (ulong)(relativeEnd - relativeStart)));
        }

        private static bool IsKnownDataOnlyOpcode(byte opcode)
        {
            switch (opcode)
            {
                case McapWriter.OpcodeHeader:
                case McapWriter.OpcodeFooter:
                case McapWriter.OpcodeMessage:
                case McapWriter.OpcodeChunk:
                case McapWriter.OpcodeMessageIndex:
                case McapWriter.OpcodeAttachment:
                case McapWriter.OpcodeMetadata:
                case McapWriter.OpcodeDataEnd:
                    return true;
                default:
                    return false;
            }
        }

        private readonly struct SummaryGroupRange
        {
            internal SummaryGroupRange(ulong start, ulong length)
            {
                Start = start;
                Length = length;
            }

            internal ulong Start { get; }
            internal ulong Length { get; }
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
