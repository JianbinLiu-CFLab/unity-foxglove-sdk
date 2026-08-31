// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Linear MCAP reader for summaryless, unindexed, and
// non-seekable streams.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Result returned by <see cref="McapStreamingReader"/> after a linear scan.
    /// </summary>
    public sealed class McapStreamingReadResult
    {
        /// <summary>Inventory and summary-like records discovered during the scan.</summary>
        public McapFileSummary Summary = new McapFileSummary();
        /// <summary>Messages matching the supplied read options.</summary>
        public List<McapMessage> Messages = new List<McapMessage>();
        /// <summary>Metadata body records discovered during the scan.</summary>
        public List<McapMetadata> Metadata = new List<McapMetadata>();
        /// <summary>Attachment body records discovered during the scan.</summary>
        public List<McapAttachment> Attachments = new List<McapAttachment>();
    }

    /// <summary>
    /// Linear MCAP reader that only requires a readable stream. It does not
    /// depend on footer seeks or summary/index records.
    /// </summary>
    public sealed class McapStreamingReader : IDisposable
    {
        private readonly Stream _stream;
        private readonly bool _ownsStream;
        private readonly McapSequentialReadLimits _limits;
        private readonly byte[] _recordHeaderBuffer = new byte[McapWriter.RecordHeaderLength];
        private readonly byte[] _magicProbeBuffer = new byte[McapWriter.MagicLength];
        private byte[] _contentBuffer;
        private long _bytesRead;
        private bool _sawTrailingMagic;
        private bool _disposed;

        private enum StreamingSection
        {
            Data,
            Summary,
            Footer
        }

        /// <summary>Create a streaming reader over any readable MCAP stream.</summary>
        public McapStreamingReader(Stream stream, bool leaveOpen = false, McapSequentialReadLimits sequentialReadLimits = null)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (!_stream.CanRead)
                throw new ArgumentException("MCAP streaming reader requires a readable stream.", nameof(stream));
            _ownsStream = !leaveOpen;
            _limits = sequentialReadLimits ?? McapSequentialReadLimits.Default;
            _limits.Validate();
        }

        /// <summary>Scan the stream and return messages plus discovered inventory.</summary>
        public McapStreamingReadResult Read(McapReadOptions options = null)
        {
            options = options ?? new McapReadOptions();
            var result = new McapStreamingReadResult();
            if (options.EndTimeNs < options.StartTimeNs)
                return result;

            var filter = new StreamingReadFilter(options);
            var dataCrc = Crc32Helper.Initialize();
            var leadingMagic = _magicProbeBuffer;
            ReadExact(leadingMagic, 0, McapWriter.MagicLength);
            ValidateMagic(leadingMagic, "leading");
            dataCrc = Crc32Helper.Update(dataCrc, leadingMagic);

            var section = StreamingSection.Data;
            _sawTrailingMagic = false;
            var isFirstRecord = true;
            var retainedPayloadBytes = 0L;
            var retainedMetadataBytes = 0L;
            var retainedAttachmentBytes = 0L;
            while (TryReadRecordHeader(out var opcode, out var headerBytes, out var contentLength, out var recordStart))
            {
                if (contentLength > McapReader.DefaultRecordSizeLimit)
                    throw new InvalidDataException("MCAP record content exceeds streaming reader limit.");
                if (contentLength > int.MaxValue)
                    throw new InvalidDataException("MCAP record content exceeds int.MaxValue.");

                var contentLengthInt = (int)contentLength;
                var content = ReadExactContent(contentLengthInt);
                if (section == StreamingSection.Data && opcode != McapWriter.OpcodeDataEnd)
                {
                    dataCrc = Crc32Helper.Update(dataCrc, headerBytes);
                    dataCrc = Crc32Helper.Update(dataCrc, new ReadOnlySpan<byte>(content, 0, contentLengthInt));
                }

                if (isFirstRecord)
                {
                    if (opcode != McapWriter.OpcodeHeader)
                        throw new InvalidDataException($"Expected Header (0x01) as the first MCAP record, got 0x{opcode:X2}.");

                    McapRecordDecoder.DecodeHeader(content, 0, contentLengthInt);
                    isFirstRecord = false;
                    continue;
                }

                ValidateRecordPlacement(opcode, section);
                ProcessRecord(
                    result,
                    options,
                    filter,
                    opcode,
                    content,
                    contentLengthInt,
                    (ulong)recordStart,
                    (ulong)(headerBytes.Length + contentLengthInt),
                    ref section,
                    ref dataCrc,
                    ref retainedPayloadBytes,
                    ref retainedMetadataBytes,
                    ref retainedAttachmentBytes);
            }

            if (section == StreamingSection.Data)
                throw new InvalidDataException("MCAP streaming read requires a DataEnd record.");
            if (section != StreamingSection.Footer)
                throw new InvalidDataException("MCAP streaming read requires a Footer record after DataEnd.");
            if (!_sawTrailingMagic)
                throw new InvalidDataException("MCAP streaming read requires exact trailing magic after Footer.");

            McapIndexedReaderHelpers.ApplyOrderingAndLimit(result.Messages, options);
            return result;
        }

        private void ProcessRecord(
            McapStreamingReadResult result,
            McapReadOptions options,
            StreamingReadFilter filter,
            byte opcode,
            byte[] content,
            int contentLength,
            ulong recordStart,
            ulong recordLength,
            ref StreamingSection section,
            ref uint dataCrc,
            ref long retainedPayloadBytes,
            ref long retainedMetadataBytes,
            ref long retainedAttachmentBytes)
        {
            switch (opcode)
            {
                case McapWriter.OpcodeHeader:
                    McapRecordDecoder.DecodeHeader(content, 0, contentLength);
                    break;
                case McapWriter.OpcodeSchema:
                    AddSchema(result.Summary.Schemas, McapRecordDecoder.DecodeSchema(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeChannel:
                {
                    var channel = McapRecordDecoder.DecodeChannel(content, 0, contentLength);
                    AddChannel(result.Summary.Channels, channel);
                    filter.AddChannel(channel);
                    break;
                }
                case McapWriter.OpcodeMessage:
                    AddMessage(result, options, filter, McapRecordDecoder.DecodeMessage(content, 0, contentLength), ref retainedPayloadBytes);
                    break;
                case McapWriter.OpcodeChunk:
                    result.Summary.Statistics ??= new McapStatistics();
                    result.Summary.Statistics.ChunkCount++;
                    var records = McapRecordDecoder.DecodeChunkRecordsContent(
                        content,
                        0,
                        contentLength,
                        out var crcValid,
                        options.ChunkUncompressedSizeLimit);
                    if (!crcValid && options.ValidateCrcs)
                        throw new InvalidDataException("MCAP chunk CRC mismatch.");
                    ProcessChunkRecords(
                        result,
                        options,
                        filter,
                        records,
                        ref retainedPayloadBytes,
                        ref retainedMetadataBytes,
                        ref retainedAttachmentBytes);
                    break;
                case McapWriter.OpcodeAttachment:
                    var attachment = McapRecordDecoder.DecodeAttachment(content, 0, contentLength);
                    if (options.ValidateCrcs && !attachment.CrcValid)
                        throw new InvalidDataException("MCAP attachment CRC mismatch.");
                    AddAttachment(result, attachment, ref retainedAttachmentBytes);
                    AddAttachmentIndex(result.Summary, new McapAttachmentIndex
                    {
                        Offset = recordStart,
                        Length = recordLength,
                        LogTime = attachment.LogTime,
                        CreateTime = attachment.CreateTime,
                        DataSize = (ulong)(attachment.Data?.Length ?? 0),
                        Name = attachment.Name,
                        MediaType = attachment.MediaType
                    });
                    break;
                case McapWriter.OpcodeMetadata:
                    var metadata = McapRecordDecoder.DecodeMetadata(content, 0, contentLength);
                    AddMetadata(result, metadata, ref retainedMetadataBytes);
                    AddMetadataIndex(result.Summary, new McapMetadataIndex
                    {
                        Offset = recordStart,
                        Length = recordLength,
                        Name = metadata.Name
                    });
                    break;
                case McapWriter.OpcodeStatistics:
                    result.Summary.Statistics = McapRecordDecoder.DecodeStatistics(content, 0, contentLength);
                    break;
                case McapWriter.OpcodeChunkIndex:
                    result.Summary.ChunkIndexes.Add(McapRecordDecoder.DecodeChunkIndex(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeAttachmentIndex:
                    AddAttachmentIndex(result.Summary, McapRecordDecoder.DecodeAttachmentIndex(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeMetadataIndex:
                    AddMetadataIndex(result.Summary, McapRecordDecoder.DecodeMetadataIndex(content, 0, contentLength));
                    break;
                case McapWriter.OpcodeSummaryOffset:
                    break;
                case McapWriter.OpcodeDataEnd:
                    ValidateDataEnd(content, contentLength, dataCrc, options.ValidateCrcs);
                    section = StreamingSection.Summary;
                    break;
                case McapWriter.OpcodeFooter:
                    McapRecordDecoder.DecodeFooter(content, 0, contentLength);
                    section = StreamingSection.Footer;
                    break;
                default:
                    break;
            }
        }

        private static void ValidateRecordPlacement(byte opcode, StreamingSection section)
        {
            if (opcode == McapWriter.OpcodeHeader)
                throw new InvalidDataException("MCAP contains more than one Header record.");
            if (section == StreamingSection.Footer)
                throw new InvalidDataException("No MCAP records may follow Footer before trailing magic.");

            if (section == StreamingSection.Data)
            {
                if (opcode == McapWriter.OpcodeFooter || IsSummaryOnlyOpcode(opcode))
                    throw new InvalidDataException($"MCAP summary opcode 0x{opcode:X2} appears before DataEnd.");
                return;
            }

            if (opcode == McapWriter.OpcodeDataEnd)
                throw new InvalidDataException("MCAP contains more than one DataEnd record.");
            if (IsDataOnlyOpcode(opcode) || McapWriter.IsPrivateOpcode(opcode))
                throw new InvalidDataException($"MCAP data opcode 0x{opcode:X2} appears after DataEnd.");
        }

        private static bool IsDataOnlyOpcode(byte opcode)
        {
            return opcode == McapWriter.OpcodeMessage ||
                   opcode == McapWriter.OpcodeChunk ||
                   opcode == McapWriter.OpcodeMessageIndex ||
                   opcode == McapWriter.OpcodeAttachment ||
                   opcode == McapWriter.OpcodeMetadata;
        }

        private static bool IsSummaryOnlyOpcode(byte opcode)
        {
            return opcode == McapWriter.OpcodeChunkIndex ||
                   opcode == McapWriter.OpcodeAttachmentIndex ||
                   opcode == McapWriter.OpcodeStatistics ||
                   opcode == McapWriter.OpcodeMetadataIndex ||
                   opcode == McapWriter.OpcodeSummaryOffset;
        }

        private void ProcessChunkRecords(
            McapStreamingReadResult result,
            McapReadOptions options,
            StreamingReadFilter filter,
            byte[] uncompressedRecords,
            ref long retainedPayloadBytes,
            ref long retainedMetadataBytes,
            ref long retainedAttachmentBytes)
        {
            var off = 0;
            while (off < uncompressedRecords.Length)
            {
                if (uncompressedRecords.Length - off < McapWriter.RecordHeaderLength)
                    throw new InvalidDataException("Chunk inner record is truncated.");

                var opcode = uncompressedRecords[off++];
                if (opcode == 0)
                    throw new InvalidDataException("MCAP opcode 0x00 is invalid inside chunk.");
                var len = McapBinaryReader.ReadU64LE(uncompressedRecords, ref off);
                if (len > int.MaxValue || (int)len > uncompressedRecords.Length - off)
                    throw new InvalidDataException("Chunk inner record content is truncated.");

                var recordLength = (int)len;
                switch (opcode)
                {
                    case McapWriter.OpcodeSchema:
                        AddSchema(result.Summary.Schemas, McapRecordDecoder.DecodeSchema(uncompressedRecords, off, recordLength));
                        break;
                    case McapWriter.OpcodeChannel:
                    {
                        var channel = McapRecordDecoder.DecodeChannel(uncompressedRecords, off, recordLength);
                        AddChannel(result.Summary.Channels, channel);
                        filter.AddChannel(channel);
                        break;
                    }
                    case McapWriter.OpcodeMessage:
                        AddMessage(result, options, filter, McapRecordDecoder.DecodeMessage(uncompressedRecords, off, recordLength), ref retainedPayloadBytes);
                        break;
                    case McapWriter.OpcodeMetadata:
                    {
                        AddMetadata(result, McapRecordDecoder.DecodeMetadata(uncompressedRecords, off, recordLength), ref retainedMetadataBytes);
                        break;
                    }
                    case McapWriter.OpcodeAttachment:
                        throw new InvalidDataException("MCAP Attachment records must not appear inside a Chunk.");
                }

                off += recordLength;
            }
        }

        private void AddMessage(
            McapStreamingReadResult result,
            McapReadOptions options,
            StreamingReadFilter filter,
            McapMessage message,
            ref long retainedPayloadBytes)
        {
            UpdateStatistics(result.Summary, message);
            if (!filter.Matches(message))
                return;

            var payloadBytes = message.Data?.LongLength ?? 0;
            if (_limits.MaxMessages > 0 && result.Messages.Count >= _limits.MaxMessages)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxMessages=" + _limits.MaxMessages + ".");
            if (_limits.MaxPayloadBytes > 0 && retainedPayloadBytes + payloadBytes > _limits.MaxPayloadBytes)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxPayloadBytes=" + _limits.MaxPayloadBytes + ".");

            if (!McapIndexedReaderHelpers.TryAddBoundedMessage(result.Messages, message, options, out var evicted))
                return;

            if (evicted != null)
                retainedPayloadBytes -= evicted.Data?.LongLength ?? 0L;
            retainedPayloadBytes += payloadBytes;
        }

        private void AddMetadata(
            McapStreamingReadResult result,
            McapMetadata metadata,
            ref long retainedMetadataBytes)
        {
            var metadataBytes = EstimateMetadataBytes(metadata);
            if (_limits.MaxMetadataRecords > 0 && result.Metadata.Count >= _limits.MaxMetadataRecords)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxMetadataRecords=" + _limits.MaxMetadataRecords + ".");
            if (_limits.MaxMetadataBytes > 0 && retainedMetadataBytes + metadataBytes > _limits.MaxMetadataBytes)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxMetadataBytes=" + _limits.MaxMetadataBytes + ".");

            result.Metadata.Add(metadata);
            retainedMetadataBytes += metadataBytes;
        }

        private void AddAttachment(
            McapStreamingReadResult result,
            McapAttachment attachment,
            ref long retainedAttachmentBytes)
        {
            var attachmentBytes = attachment?.Data?.LongLength ?? 0L;
            if (_limits.MaxAttachmentRecords > 0 && result.Attachments.Count >= _limits.MaxAttachmentRecords)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxAttachmentRecords=" + _limits.MaxAttachmentRecords + ".");
            if (_limits.MaxAttachmentBytes > 0 && retainedAttachmentBytes + attachmentBytes > _limits.MaxAttachmentBytes)
                throw new InvalidOperationException("Streaming MCAP read exceeded MaxAttachmentBytes=" + _limits.MaxAttachmentBytes + ".");

            result.Attachments.Add(attachment);
            retainedAttachmentBytes += attachmentBytes;
        }

        private static void AddAttachmentIndex(McapFileSummary summary, McapAttachmentIndex index)
        {
            for (var i = 0; i < summary.AttachmentIndexes.Count; i++)
                if (summary.AttachmentIndexes[i].Offset == index.Offset)
                    return;

            summary.AttachmentIndexes.Add(index);
        }

        private static void AddMetadataIndex(McapFileSummary summary, McapMetadataIndex index)
        {
            for (var i = 0; i < summary.MetadataIndexes.Count; i++)
                if (summary.MetadataIndexes[i].Offset == index.Offset)
                    return;

            summary.MetadataIndexes.Add(index);
        }

        private static long EstimateMetadataBytes(McapMetadata metadata)
        {
            if (metadata == null)
                return 0L;

            long total = Encoding.UTF8.GetByteCount(metadata.Name ?? string.Empty);
            if (metadata.Metadata == null)
                return total;

            foreach (var item in metadata.Metadata)
            {
                total += Encoding.UTF8.GetByteCount(item.Key ?? string.Empty);
                total += Encoding.UTF8.GetByteCount(item.Value ?? string.Empty);
            }

            return total;
        }

        private sealed class StreamingReadFilter
        {
            private readonly McapReadOptions _options;
            private readonly HashSet<ushort> _channelIds;
            private readonly HashSet<string> _topics;
            private readonly Dictionary<ushort, string> _channelTopics = new Dictionary<ushort, string>();

            public StreamingReadFilter(McapReadOptions options)
            {
                _options = options ?? throw new ArgumentNullException(nameof(options));
                if (options.ChannelIds != null && options.ChannelIds.Count > 0)
                    _channelIds = new HashSet<ushort>(options.ChannelIds);
                if (options.Topics != null && options.Topics.Count > 0)
                    _topics = new HashSet<string>(options.Topics, StringComparer.Ordinal);
            }

            public void AddChannel(McapChannel channel)
            {
                if (channel != null)
                    _channelTopics[channel.Id] = channel.Topic ?? string.Empty;
            }

            public bool Matches(McapMessage message)
            {
                if (message.LogTime < _options.StartTimeNs)
                    return false;
                if (_options.UseOfficialEndTimeSemantics)
                {
                    if (message.LogTime >= _options.EndTimeNs)
                        return false;
                }
                else if (message.LogTime > _options.EndTimeNs)
                {
                    return false;
                }

                if (_channelIds == null && _topics == null)
                    return true;
                if (_channelIds != null && _channelIds.Contains(message.ChannelId))
                    return true;
                if (_topics == null)
                    return false;

                return _channelTopics.TryGetValue(message.ChannelId, out var topic) &&
                       _topics.Contains(topic);
            }
        }

        private static void UpdateStatistics(McapFileSummary summary, McapMessage message)
        {
            summary.Statistics ??= new McapStatistics();
            var stats = summary.Statistics;
            stats.MessageCount++;
            if (stats.MessageCount == 1 || message.LogTime < stats.MessageStartTime)
                stats.MessageStartTime = message.LogTime;
            if (message.LogTime > stats.MessageEndTime)
                stats.MessageEndTime = message.LogTime;
            stats.ChannelMessageCounts.TryGetValue(message.ChannelId, out var count);
            stats.ChannelMessageCounts[message.ChannelId] = count + 1;
        }

        private static void ValidateDataEnd(byte[] content, int contentLength, uint dataCrc, bool validateCrcs)
        {
            if (content == null || contentLength != McapWriter.Crc32SizeBytes)
                throw new InvalidDataException("MCAP DataEnd content length must be 4 bytes.");
            var off = 0;
            var stored = McapBinaryReader.ReadU32LE(content, ref off);
            if (validateCrcs && stored != 0 && stored != Crc32Helper.Finalize(dataCrc))
                throw new InvalidDataException("MCAP DataEnd CRC mismatch.");
        }

        private bool TryReadRecordHeader(out byte opcode, out byte[] headerBytes, out ulong contentLength, out long recordStart)
        {
            opcode = 0;
            contentLength = 0;
            headerBytes = null;
            recordStart = _bytesRead;

            var first = _stream.ReadByte();
            if (first < 0)
                return false;
            _bytesRead++;

            var magic = McapWriter.MagicSpan;
            if ((byte)first == magic[0])
            {
                var probe = _magicProbeBuffer;
                probe[0] = (byte)first;
                ReadExact(probe, 1, McapWriter.MagicLength - 1);
                var isMagic = true;
                for (var i = 0; i < magic.Length; i++)
                {
                    if (probe[i] != magic[i])
                    {
                        isMagic = false;
                        break;
                    }
                }

                if (isMagic)
                {
                    _sawTrailingMagic = true;
                    if (_stream.ReadByte() >= 0)
                    {
                        _bytesRead++;
                        throw new InvalidDataException("MCAP contains bytes after trailing magic.");
                    }
                    return false;
                }

                headerBytes = _recordHeaderBuffer;
                Buffer.BlockCopy(probe, 0, headerBytes, 0, probe.Length);
                ReadExact(headerBytes, probe.Length, 1);
            }
            else
            {
                headerBytes = _recordHeaderBuffer;
                headerBytes[0] = (byte)first;
                ReadExact(headerBytes, 1, McapWriter.RecordHeaderLength - 1);
            }

            opcode = headerBytes[0];
            if (opcode == 0)
                throw new InvalidDataException("MCAP opcode 0x00 is invalid.");
            var off = 1;
            contentLength = McapBinaryReader.ReadU64LE(headerBytes, ref off);
            return true;
        }

        private byte[] ReadExactContent(int count)
        {
            if (_contentBuffer == null || _contentBuffer.Length < count)
                _contentBuffer = new byte[count];
            ReadExact(_contentBuffer, 0, count);
            return _contentBuffer;
        }

        private void ReadExact(byte[] buffer, int offset, int count)
        {
            var read = 0;
            while (read < count)
            {
                var n = _stream.Read(buffer, offset + read, count - read);
                if (n == 0)
                    throw new EndOfStreamException("MCAP stream ended unexpectedly.");
                read += n;
                _bytesRead += n;
            }
        }

        private static void ValidateMagic(byte[] actual, string name)
        {
            var magic = McapWriter.MagicSpan;
            if (actual == null || actual.Length != magic.Length)
                throw new InvalidDataException("MCAP " + name + " magic is truncated.");
            for (var i = 0; i < magic.Length; i++)
            {
                if (actual[i] != magic[i])
                    throw new InvalidDataException("MCAP " + name + " magic mismatch.");
            }
        }

        private static void AddSchema(List<McapSchema> schemas, McapSchema schema)
            => McapRecordDecoder.AddSchema(schemas, schema);

        private static void AddChannel(List<McapChannel> channels, McapChannel channel)
            => McapRecordDecoder.AddChannel(channels, channel);

        /// <summary>Dispose the owned stream when requested by the constructor.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _contentBuffer = null;
            if (_ownsStream)
                _stream.Dispose();
        }
    }
}
