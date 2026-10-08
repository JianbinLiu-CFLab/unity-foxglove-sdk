// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-10 regression coverage for MCAP DataLoader query budgets.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_10Validation.
    /// </summary>
    public static partial class Phase134_10Validation
    {
        private static void WriteChunkIndex(McapWriter writer, ulong timeNs, ulong chunkOffset, ulong chunkLength, int rawLength)
        {
            writer.WriteChunkIndex(timeNs, timeNs, chunkOffset, chunkLength,
                new Dictionary<ushort, ulong>(), 0, "", (ulong)rawLength, (ulong)rawLength);
        }

        private static byte[] BuildChunkMessages(params (ushort ChannelId, uint Sequence, ulong LogTime, ulong PublishTime, string Payload)[] messages)
        {
            var chunkData = new MemoryStream();
            for (var i = 0; i < messages.Length; i++)
            {
                var message = messages[i];
                WriteInnerMessage(
                    chunkData,
                    message.ChannelId,
                    message.Sequence,
                    message.LogTime,
                    message.PublishTime,
                    Encoding.UTF8.GetBytes(message.Payload));
            }

            return chunkData.ToArray();
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

        private static string TempMcapPath()
            => Path.Combine(Path.GetTempPath(), "phase134_10_" + Guid.NewGuid().ToString("N") + ".mcap");

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    _cleanupFailures++;
            }
            catch
            {
                _cleanupFailures++;
                Console.Error.WriteLine("[FAIL] 134-10 cleanup could not delete temporary MCAP fixture: " + path);
            }
        }

        private sealed class RecordingLogger : IFoxgloveLogger
        {
            internal readonly List<string> Warnings = new List<string>();

            public void LogWarning(string message) => Warnings.Add(message);

            public void LogError(string message)
            {
            }
        }

        private sealed class CountingDecoderFactory : IMcapMessageDecoderFactory
        {
            public int TryCreateCount;

            /// <summary>
            /// Validation method for TryCreate.
            /// </summary>
            /// <param name="schema">Schema metadata supplied to the recorder stub.</param>
            /// <param name="channel">Channel metadata supplied to the recorder stub.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
            {
                TryCreateCount++;
                return new CountingDecoder();
            }
        }

        private sealed class NullDecoderFactory : IMcapMessageDecoderFactory
        {
            /// <summary>
            /// Validation method for TryCreate.
            /// </summary>
            /// <param name="schema">Schema metadata supplied to the recorder stub.</param>
            /// <param name="channel">Channel metadata supplied to the recorder stub.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public IMcapMessageDecoder TryCreate(McapSchema schema, McapChannel channel)
                => null;
        }

        private sealed class CountingDecoder : IMcapMessageDecoder
        {
            /// <summary>
            /// Validation method for Decode.
            /// </summary>
            /// <param name="message">Diagnostic message recorded by the test double.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public McapDecodedPayload Decode(McapDataLoaderMessage message)
            {
                return new McapDecodedPayload
                {
                    Kind = McapDecodedPayloadKind.Json,
                    Text = "{}",
                    RawData = message?.Data ?? Array.Empty<byte>()
                };
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
