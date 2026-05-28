// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Linear MCAP reader for summaryless, unindexed, and
// non-seekable streams.

using System;
using System.Collections.Generic;
using System.IO;
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
        private long _bytesRead;
        private bool _disposed;

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
            var leadingMagic = ReadExact(McapWriter.MagicLength);
            ValidateMagic(leadingMagic, "leading");
            dataCrc = Crc32Helper.Update(dataCrc, leadingMagic);

            var beforeDataEnd = true;
            var retainedPayloadBytes = 0L;
            while (TryReadRecordHeader(out var opcode, out var headerBytes, out var contentLength, out var recordStart))
            {
                if (contentLength > McapReader.DefaultRecordSizeLimit)
                    throw new InvalidDataException("MCAP record content exceeds streaming reader limit.");
                if (contentLength > int.MaxValue)
                    throw new InvalidDataException("MCAP record content exceeds int.MaxValue.");

                var content = ReadExact((int)contentLength);
                if (beforeDataEnd && opcode != McapWriter.OpcodeDataEnd)
                {
                    dataCrc = Crc32Helper.Update(dataCrc, headerBytes);
                    dataCrc = Crc32Helper.Update(dataCrc, content);
                }

                ProcessRecord(
                    result,
                    options,
                    filter,
                    opcode,
                    content,
                    (ulong)recordStart,
                    (ulong)(headerBytes.Length + content.Length),
                    ref beforeDataEnd,
                    ref dataCrc,
                    ref retainedPayloadBytes);
            }

            ApplyOrderingAndLimit(result.Messages, options);
            return result;
        }

        private void ProcessRecord(
            McapStreamingReadResult result,
            McapReadOptions options,
            StreamingReadFilter filter,
            byte opcode,
            byte[] content,
            ulong recordStart,
            ulong recordLength,
            ref bool beforeDataEnd,
            ref uint dataCrc,
            ref long retainedPayloadBytes)
        {
            switch (opcode)
            {
                case McapWriter.OpcodeHeader:
                    McapRecordDecoder.DecodeHeader(content);
                    break;
                case McapWriter.OpcodeSchema:
                    AddSchema(result.Summary.Schemas, McapRecordDecoder.DecodeSchema(content));
                    break;
                case McapWriter.OpcodeChannel:
                {
                    var channel = McapRecordDecoder.DecodeChannel(content);
                    AddChannel(result.Summary.Channels, channel);
                    filter.AddChannel(channel);
                    break;
                }
                case McapWriter.OpcodeMessage:
                    AddMessage(result, options, filter, McapRecordDecoder.DecodeMessage(content, 0, content.Length), ref retainedPayloadBytes);
                    break;
                case McapWriter.OpcodeChunk:
                    result.Summary.Statistics ??= new McapStatistics();
                    result.Summary.Statistics.ChunkCount++;
                    var records = McapRecordDecoder.DecodeChunkRecordsContent(
                        content,
                        out var crcValid,
                        options.ChunkUncompressedSizeLimit);
                    if (!crcValid && options.ValidateCrcs)
                        throw new InvalidDataException("MCAP chunk CRC mismatch.");
                    ProcessChunkRecords(result, options, filter, records, ref retainedPayloadBytes);
                    break;
                case McapWriter.OpcodeAttachment:
                    var attachment = McapRecordDecoder.DecodeAttachment(content);
                    if (options.ValidateCrcs && !attachment.CrcValid)
                        throw new InvalidDataException("MCAP attachment CRC mismatch.");
                    result.Attachments.Add(attachment);
                    result.Summary.AttachmentIndexes.Add(new McapAttachmentIndex
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
                    var metadata = McapRecordDecoder.DecodeMetadata(content);
                    result.Metadata.Add(metadata);
                    result.Summary.MetadataIndexes.Add(new McapMetadataIndex
                    {
                        Offset = recordStart,
                        Length = recordLength,
                        Name = metadata.Name
                    });
                    break;
                case McapWriter.OpcodeStatistics:
                    result.Summary.Statistics = McapRecordDecoder.DecodeStatistics(content);
                    break;
                case McapWriter.OpcodeChunkIndex:
                    result.Summary.ChunkIndexes.Add(McapRecordDecoder.DecodeChunkIndex(content));
                    break;
                case McapWriter.OpcodeAttachmentIndex:
                    result.Summary.AttachmentIndexes.Add(McapRecordDecoder.DecodeAttachmentIndex(content));
                    break;
                case McapWriter.OpcodeMetadataIndex:
                    result.Summary.MetadataIndexes.Add(McapRecordDecoder.DecodeMetadataIndex(content));
                    break;
                case McapWriter.OpcodeSummaryOffset:
                    break;
                case McapWriter.OpcodeDataEnd:
                    ValidateDataEnd(content, dataCrc, options.ValidateCrcs);
                    beforeDataEnd = false;
                    break;
                case McapWriter.OpcodeFooter:
                    McapRecordDecoder.DecodeFooter(content);
                    break;
                default:
                    break;
            }
        }

        private void ProcessChunkRecords(
            McapStreamingReadResult result,
            McapReadOptions options,
            StreamingReadFilter filter,
            byte[] uncompressedRecords,
            ref long retainedPayloadBytes)
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
                        var content = Slice(uncompressedRecords, off, recordLength);
                        result.Metadata.Add(McapRecordDecoder.DecodeMetadata(content));
                        break;
                    }
                    case McapWriter.OpcodeAttachment:
                    {
                        var content = Slice(uncompressedRecords, off, recordLength);
                        var attachment = McapRecordDecoder.DecodeAttachment(content);
                        if (options.ValidateCrcs && !attachment.CrcValid)
                            throw new InvalidDataException("MCAP attachment CRC mismatch.");
                        result.Attachments.Add(attachment);
                        break;
                    }
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

            result.Messages.Add(message);
            retainedPayloadBytes += payloadBytes;
        }

        private static void ApplyOrderingAndLimit(List<McapMessage> messages, McapReadOptions options)
        {
            if (options.Order == McapReadOrder.LogTimeAscending)
                messages.Sort(CompareMessages);
            else if (options.Order == McapReadOrder.LogTimeDescending)
                messages.Sort((left, right) => CompareMessages(right, left));

            if (options.MaxMessages <= 0 || messages.Count <= options.MaxMessages)
                return;

            if (options.Order == McapReadOrder.LogTimeDescending || options.Order == McapReadOrder.FileOrder)
                messages.RemoveRange(options.MaxMessages, messages.Count - options.MaxMessages);
            else
                messages.RemoveRange(0, messages.Count - options.MaxMessages);
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

        private static void ValidateDataEnd(byte[] content, uint dataCrc, bool validateCrcs)
        {
            if (content == null || content.Length != McapWriter.Crc32SizeBytes)
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

            var magic = McapWriter.Magic;
            if ((byte)first == magic[0])
            {
                var probe = new byte[McapWriter.MagicLength];
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
                    return false;

                headerBytes = new byte[McapWriter.RecordHeaderLength];
                Buffer.BlockCopy(probe, 0, headerBytes, 0, probe.Length);
                ReadExact(headerBytes, probe.Length, 1);
            }
            else
            {
                headerBytes = new byte[McapWriter.RecordHeaderLength];
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

        private byte[] ReadExact(int count)
        {
            var data = new byte[count];
            ReadExact(data, 0, count);
            return data;
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
            var magic = McapWriter.Magic;
            if (actual == null || actual.Length != magic.Length)
                throw new InvalidDataException("MCAP " + name + " magic is truncated.");
            for (var i = 0; i < magic.Length; i++)
            {
                if (actual[i] != magic[i])
                    throw new InvalidDataException("MCAP " + name + " magic mismatch.");
            }
        }

        private static byte[] Slice(byte[] source, int offset, int count)
        {
            var copy = new byte[count];
            if (count > 0)
                Buffer.BlockCopy(source, offset, copy, 0, count);
            return copy;
        }

        private static void AddSchema(List<McapSchema> schemas, McapSchema schema)
        {
            for (var i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id == schema.Id)
                    return;
            }
            schemas.Add(schema);
        }

        private static void AddChannel(List<McapChannel> channels, McapChannel channel)
        {
            for (var i = 0; i < channels.Count; i++)
            {
                if (channels[i].Id == channel.Id)
                    return;
            }
            channels.Add(channel);
        }

        private static int CompareMessages(McapMessage left, McapMessage right)
        {
            var cmp = left.LogTime.CompareTo(right.LogTime);
            if (cmp != 0) return cmp;
            cmp = left.ChannelId.CompareTo(right.ChannelId);
            if (cmp != 0) return cmp;
            cmp = left.Sequence.CompareTo(right.Sequence);
            if (cmp != 0) return cmp;
            return left.PublishTime.CompareTo(right.PublishTime);
        }

        /// <summary>Dispose the owned stream when requested by the constructor.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_ownsStream)
                _stream.Dispose();
        }
    }
}
