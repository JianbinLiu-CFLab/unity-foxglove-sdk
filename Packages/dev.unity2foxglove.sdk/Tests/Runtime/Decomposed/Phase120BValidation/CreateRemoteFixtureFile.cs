// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 120B validation for MCAP DataLoader hardening review closure.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase120BValidation.
    /// </summary>
    public static partial class Phase120BValidation
    {
        private static string CreateRemoteFixtureFile()
        {
            var path = TempMcapHelper.CreatePath("phase120b_remote");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var source = CreateDirectFixture())
            {
                source.CopyTo(fs);
            }

            return path;
        }

        private static byte[] BuildChunkMcap(bool summaryStart, bool badCrc, bool includeSchemaChannel)
        {
            var ms = new MemoryStream();
            using (var writer = new McapWriter(ms, leaveOpen: true))
            {
                writer.WriteMagic();
                writer.WriteHeader("", "phase120b-chunk");

                var chunkData = new MemoryStream();
                if (includeSchemaChannel)
                {
                    WriteInnerSchema(chunkData, 1, "phase120b.Chunked", "jsonschema", Encoding.UTF8.GetBytes("{}"));
                    WriteInnerChannel(chunkData, 1, 1, "/phase120b/chunked", "json");
                }
                WriteInnerMessage(chunkData, 1, 1, 100, 100, Encoding.UTF8.GetBytes("{}"));
                var raw = chunkData.ToArray();

                var chunkOffset = (ulong)ms.Position;
                var crc = badCrc ? 0xDEADBEEFu : Crc32Helper.Compute(raw);
                writer.WriteChunk(100, 100, (ulong)raw.Length, crc, "", (ulong)raw.Length, raw);
                var chunkLength = (ulong)ms.Position - chunkOffset;

                var summaryOffset = summaryStart ? (ulong)ms.Position : 0UL;
                if (summaryStart)
                {
                    writer.WriteChunkIndex(100, 100, chunkOffset, chunkLength,
                        new Dictionary<ushort, ulong>(), 0, "", (ulong)raw.Length, (ulong)raw.Length);
                }

                writer.WriteFooter(summaryOffset, 0, 0);
                writer.WriteMagic();
                writer.Flush();
            }

            return ms.ToArray();
        }

        private static void WriteInnerSchema(Stream stream, ushort id, string name, string encoding, byte[] data)
        {
            WriteInnerRecord(stream, 0x03, BuildSchemaContent(id, name, encoding, data));
        }

        private static void WriteInnerChannel(Stream stream, ushort channelId, ushort schemaId, string topic, string encoding)
        {
            WriteInnerRecord(stream, 0x04, BuildChannelContent(channelId, schemaId, topic, encoding));
        }

        private static byte[] BuildSchemaContent(ushort id, string name, string encoding, byte[] data)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, id);
            McapWriter.WriteString(content, name);
            McapWriter.WriteString(content, encoding);
            McapWriter.WriteLengthPrefixedBytes(content, data);
            return content.ToArray();
        }

        private static byte[] BuildChannelContent(ushort channelId, ushort schemaId, string topic, string encoding)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, channelId);
            McapWriter.WriteU16(content, schemaId);
            McapWriter.WriteString(content, topic);
            McapWriter.WriteString(content, encoding);
            McapWriter.WriteStringMap(content, new Dictionary<string, string>());
            return content.ToArray();
        }

        private static byte[] WrapWithPadding(byte[] content)
        {
            var buffer = new byte[content.Length + 2];
            Buffer.BlockCopy(content, 0, buffer, 1, content.Length);
            return buffer;
        }

        private static void WriteInnerMessage(Stream stream, ushort channelId, uint sequence, ulong logTime, ulong publishTime, byte[] data)
        {
            var content = new MemoryStream();
            McapWriter.WriteU16(content, channelId);
            McapWriter.WriteU32(content, sequence);
            McapWriter.WriteU64(content, logTime);
            McapWriter.WriteU64(content, publishTime);
            if (data != null && data.Length > 0)
                content.Write(data, 0, data.Length);
            WriteInnerRecord(stream, 0x05, content.ToArray());
        }

        private static void WriteInnerRecord(Stream stream, byte opcode, byte[] content)
        {
            stream.WriteByte(opcode);
            McapWriter.WriteU64(stream, (ulong)(content?.Length ?? 0));
            if (content != null && content.Length > 0)
                stream.Write(content, 0, content.Length);
        }

        private static RemoteMcapRequest AuthorizedRequest()
        {
            return new RemoteMcapRequest
            {
                BearerToken = "Bearer " + Token,
                SourceId = SourceId
            };
        }

        private static bool ThrowsWith<TException>(Action action, string requiredText)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException ex)
            {
                return ex.Message.Contains(requiredText, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase120B file: " + relativePath, path);
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");
            return root;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
