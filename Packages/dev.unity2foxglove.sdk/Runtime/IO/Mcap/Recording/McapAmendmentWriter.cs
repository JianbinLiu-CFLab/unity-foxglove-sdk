// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: Post-recording MCAP metadata and attachment amendment writer.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Appends post-recording metadata, attachments, and private records to an
    /// indexed MCAP file. Close writes a complete sibling temp file first, then
    /// replaces the original and leaves the previous file at a unique
    /// <c>*.bak</c> sibling path.
    /// </summary>
    public sealed class McapAmendmentWriter : IDisposable
    {
        private readonly string _filePath;
        private readonly McapFileSummary _summary;
        private readonly McapTrailerInfo _trailer;
        private readonly bool _enableCrcs;
        private FileStream _sourceStream;
        private readonly List<PendingMetadata> _metadata = new List<PendingMetadata>();
        private readonly List<PendingAttachment> _attachments = new List<PendingAttachment>();
        private readonly List<PendingPrivateRecord> _privateRecords = new List<PendingPrivateRecord>();
        private bool _closed;
        private bool _failed;
        private bool _disposed;

        public McapAmendmentWriter(string filePath)
            : this(filePath, enableCrcs: true)
        {
        }

        public McapAmendmentWriter(string filePath, bool enableCrcs)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("MCAP file path is required.", nameof(filePath));

            _filePath = Path.GetFullPath(filePath);
            _enableCrcs = enableCrcs;
            _sourceStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            try
            {
                var reader = new McapReader(_sourceStream);
                _summary = reader.ReadSummary();
                _trailer = reader.ReadTrailerInfo();
                if (_summary.Statistics == null)
                    _summary.Statistics = reader.ReadDataSectionSummary(_trailer.DataEndEndOffset).Statistics;
            }
            catch
            {
                _sourceStream.Dispose();
                _sourceStream = null;
                throw;
            }
        }

        public void AddAttachment(
            string name,
            string mediaType,
            byte[] data,
            ulong logTimeNs,
            ulong createTimeNs = 0)
        {
            ThrowIfClosedOrDisposed();
            _attachments.Add(new PendingAttachment
            {
                Name = name ?? string.Empty,
                MediaType = mediaType ?? string.Empty,
                Data = data == null ? Array.Empty<byte>() : (byte[])data.Clone(),
                LogTimeNs = logTimeNs,
                CreateTimeNs = createTimeNs
            });
        }

        public void AddMetadata(string name, Dictionary<string, string> metadata)
        {
            ThrowIfClosedOrDisposed();
            _metadata.Add(new PendingMetadata
            {
                Name = name ?? string.Empty,
                Metadata = metadata == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(metadata, StringComparer.Ordinal)
            });
        }

        public void AddPrivateRecord(byte opcode, byte[] data)
        {
            ThrowIfClosedOrDisposed();
            if (!McapWriter.IsPrivateOpcode(opcode))
                throw new ArgumentOutOfRangeException(nameof(opcode), "MCAP private record opcodes must be in the 0x80-0xFF range.");

            _privateRecords.Add(new PendingPrivateRecord
            {
                Opcode = opcode,
                Data = data == null ? Array.Empty<byte>() : (byte[])data.Clone()
            });
        }

        public void Close()
        {
            ThrowIfDisposed();
            if (_closed)
                return;
            if (_failed)
                throw new InvalidOperationException("MCAP amendment writer is in a failed terminal state; create a new writer to retry.");

            if (_metadata.Count == 0 && _attachments.Count == 0 && _privateRecords.Count == 0)
            {
                CloseSourceStream();
                _closed = true;
                return;
            }

            var tempPath = CreateTempPath(_filePath);
            try
            {
                WriteAmendedTempFile(tempPath);
                ReplaceOriginalWithTemp(tempPath);
                _closed = true;
            }
            catch
            {
                _failed = true;
                TryDelete(tempPath);
                throw;
            }
        }

        private void WriteAmendedTempFile(string tempPath)
        {
            if (_sourceStream == null)
                throw new InvalidOperationException("MCAP amendment writer source stream is no longer available after a failed close.");

            _sourceStream.Seek(0, SeekOrigin.Begin);
            using var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var writer = new McapWriter(tempStream, leaveOpen: true);
            try
            {
                CopyExact(_sourceStream, tempStream, _trailer.DataEndOffset);

                var newMetadataIndexes = new List<McapMetadataIndex>();
                for (var i = 0; i < _metadata.Count; i++)
                {
                    var item = _metadata[i];
                    var offset = (ulong)writer.Position;
                    writer.WriteMetadata(item.Name, item.Metadata);
                    var length = (ulong)writer.Position - offset;
                    newMetadataIndexes.Add(new McapMetadataIndex
                    {
                        Offset = offset,
                        Length = length,
                        Name = item.Name
                    });
                }

                var newAttachmentIndexes = new List<McapAttachmentIndex>();
                for (var i = 0; i < _attachments.Count; i++)
                {
                    var item = _attachments[i];
                    newAttachmentIndexes.Add(writer.WriteAttachment(
                        item.LogTimeNs,
                        item.CreateTimeNs,
                        item.Name,
                        item.MediaType,
                        item.Data,
                        _enableCrcs));
                }

                for (var i = 0; i < _privateRecords.Count; i++)
                {
                    var item = _privateRecords[i];
                    writer.WritePrivateRecord(item.Opcode, item.Data);
                }

                var dataSectionCrc = _trailer.DataSectionCrc == 0
                    ? 0
                    : writer.ComputeCrc32FromStartToCurrent();
                writer.WriteDataEnd(dataSectionCrc);
                McapSummarySerializer.WriteSummaryAndFooter(
                    writer,
                    BuildAmendedSummary(newMetadataIndexes, newAttachmentIndexes),
                    writeSummaryOffsets: true,
                    enableSummaryCrc: true);
                writer.WriteMagic();
                writer.Flush();
                tempStream.Flush(true);
            }
            finally
            {
                writer.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (!_closed && !_failed)
                    Close();
            }
            finally
            {
                CloseSourceStream();
                _disposed = true;
            }
        }

        private McapFileSummary BuildAmendedSummary(
            IReadOnlyList<McapMetadataIndex> newMetadataIndexes,
            IReadOnlyList<McapAttachmentIndex> newAttachmentIndexes)
        {
            var amended = new McapFileSummary();
            amended.Schemas.AddRange(_summary.Schemas);
            amended.Channels.AddRange(_summary.Channels);
            amended.ChunkIndexes.AddRange(_summary.ChunkIndexes);

            var allMetadataIndexes = new List<McapMetadataIndex>(_summary.MetadataIndexes.Count + newMetadataIndexes.Count);
            allMetadataIndexes.AddRange(_summary.MetadataIndexes);
            allMetadataIndexes.AddRange(newMetadataIndexes);
            amended.MetadataIndexes.AddRange(allMetadataIndexes);

            var allAttachmentIndexes = new List<McapAttachmentIndex>(_summary.AttachmentIndexes.Count + newAttachmentIndexes.Count);
            allAttachmentIndexes.AddRange(_summary.AttachmentIndexes);
            allAttachmentIndexes.AddRange(newAttachmentIndexes);
            amended.AttachmentIndexes.AddRange(allAttachmentIndexes);

            amended.Statistics = CreateAmendedStatistics(
                (uint)allMetadataIndexes.Count,
                (uint)allAttachmentIndexes.Count);
            return amended;
        }

        private McapStatistics CreateAmendedStatistics(uint metadataCount, uint attachmentCount)
        {
            var statistics = _summary.Statistics;
            if (statistics == null)
                return null;

            return new McapStatistics
            {
                MessageCount = statistics.MessageCount,
                SchemaCount = statistics.SchemaCount,
                ChannelCount = statistics.ChannelCount,
                AttachmentCount = attachmentCount,
                MetadataCount = metadataCount,
                ChunkCount = statistics.ChunkCount,
                MessageStartTime = statistics.MessageStartTime,
                MessageEndTime = statistics.MessageEndTime,
                ChannelMessageCounts = statistics.ChannelMessageCounts ?? new Dictionary<ushort, ulong>()
            };
        }

        private void ThrowIfClosedOrDisposed()
        {
            ThrowIfDisposed();
            if (_closed)
                throw new InvalidOperationException("MCAP amendment writer is already closed.");
            if (_failed)
                throw new InvalidOperationException("MCAP amendment writer is in a failed terminal state; create a new writer to retry.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(McapAmendmentWriter));
        }

        private void ReplaceOriginalWithTemp(string tempPath)
        {
            CloseSourceStream();
            var backupPath = CreateBackupPath(_filePath);
            try
            {
                File.Replace(tempPath, _filePath, backupPath, ignoreMetadataErrors: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(_filePath, backupPath);
                try
                {
                    File.Move(tempPath, _filePath);
                }
                catch (Exception replaceError)
                {
                    try
                    {
                        if (!File.Exists(_filePath) && File.Exists(backupPath))
                            File.Move(backupPath, _filePath);
                    }
                    catch (Exception restoreError)
                    {
                        throw new IOException(
                            $"MCAP amendment failed after moving the original file to backup '{backupPath}', and restoring that backup also failed.",
                            new AggregateException(replaceError, restoreError));
                    }

                    throw new IOException(
                        $"MCAP amendment failed while replacing the original file; the original file was restored from backup '{backupPath}'.",
                        replaceError);
                }
            }
        }

        private void CloseSourceStream()
        {
            if (_sourceStream == null)
                return;

            _sourceStream.Dispose();
            _sourceStream = null;
        }

        private static void CopyExact(Stream source, Stream destination, ulong byteCount)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                var remaining = byteCount;
                while (remaining > 0)
                {
                    var count = remaining > (ulong)buffer.Length ? buffer.Length : (int)remaining;
                    var read = source.Read(buffer, 0, count);
                    if (read <= 0)
                        throw new EndOfStreamException("MCAP source ended while copying the data section.");

                    destination.Write(buffer, 0, read);
                    remaining -= (ulong)read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static string CreateTempPath(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                directory = Directory.GetCurrentDirectory();

            return Path.Combine(
                directory,
                "." + Path.GetFileName(filePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        }

        private static string CreateBackupPath(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                directory = Directory.GetCurrentDirectory();

            return Path.Combine(
                directory,
                Path.GetFileName(filePath) + "." + Guid.NewGuid().ToString("N") + ".bak");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for temp and previous backup files.
            }
        }

        private sealed class PendingMetadata
        {
            public string Name;
            public Dictionary<string, string> Metadata;
        }

        private sealed class PendingAttachment
        {
            public string Name;
            public string MediaType;
            public byte[] Data;
            public ulong LogTimeNs;
            public ulong CreateTimeNs;
        }

        private sealed class PendingPrivateRecord
        {
            public byte Opcode;
            public byte[] Data;
        }
    }
}
