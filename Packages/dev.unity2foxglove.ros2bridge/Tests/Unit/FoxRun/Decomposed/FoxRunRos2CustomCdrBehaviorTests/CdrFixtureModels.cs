// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Editor;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunRos2CustomCdrBehaviorTests
    {
        private sealed class FixtureValues
        {
            public byte[] Bytes;
            public int Count;
            public ushort Kind;
            public int[] Kinds;
            public List<string> Labels;
            public string Message;
            public NestedValues Nested;
            public int? OptionalCount;
            public string OptionalText;
            public List<long> Values;
        }

        private sealed class NestedValues
        {
            public bool Enabled;
            public string Label;
        }

        private readonly struct BuildResult
        {
            public BuildResult(
                bool success,
                byte[] payload,
                string reason,
                int optionalTextReadCount)
            {
                Success = success;
                Payload = payload;
                Reason = reason;
                OptionalTextReadCount = optionalTextReadCount;
            }

            public bool Success { get; }
            public byte[] Payload { get; }
            public string Reason { get; }
            public int OptionalTextReadCount { get; }
        }

        private readonly struct DecodeResult
        {
            public DecodeResult(
                bool success,
                FixtureValues values,
                string reason,
                bool remoteOwned,
                string remoteTransportId,
                ulong remoteGeneration)
            {
                Success = success;
                Values = values;
                Reason = reason;
                RemoteOwned = remoteOwned;
                RemoteTransportId = remoteTransportId;
                RemoteGeneration = remoteGeneration;
            }

            public bool Success { get; }
            public FixtureValues Values { get; }
            public string Reason { get; }
            public bool RemoteOwned { get; }
            public string RemoteTransportId { get; }
            public ulong RemoteGeneration { get; }
        }

        private readonly struct OracleResult
        {
            public OracleResult(byte[] bytes, IReadOnlyDictionary<string, int> presenceOffsets)
            {
                Bytes = bytes;
                PresenceOffsets = presenceOffsets;
            }

            public byte[] Bytes { get; }
            public IReadOnlyDictionary<string, int> PresenceOffsets { get; }
        }

        private interface IOracleWriter
        {
            int Position { get; }
            void WriteBool(bool value);
            void WriteUInt16(ushort value);
            void WriteInt32(int value);
            void WriteUInt32(uint value);
            void WriteInt64(long value);
            void WriteUInt64(ulong value);
            void WriteString(string value);
            void WriteByteSequence(byte[] values, int count);
            void WriteSequenceLength(int count);
        }

        private sealed class OracleByteWriter : IOracleWriter
        {
            private const int AlignmentOrigin = 4;
            private readonly List<byte> _bytes = new List<byte>
            {
                0x00, 0x01, 0x00, 0x00,
            };

            public int Position => _bytes.Count;

            public void WriteBool(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

            public void WriteUInt16(ushort value)
            {
                Align(2);
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteInt32(int value)
            {
                Align(4);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteUInt32(uint value)
            {
                Align(4);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteInt64(long value)
            {
                Align(8);
                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteUInt64(ulong value)
            {
                Align(8);
                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                WriteUInt32(checked((uint)bytes.Length + 1U));
                _bytes.AddRange(bytes);
                _bytes.Add(0);
            }

            public void WriteByteSequence(byte[] values, int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                WriteSequenceLength(count);
                if (values == null)
                {
                    for (var index = 0; index < count; index++)
                        _bytes.Add(0);
                    return;
                }

                if (values.Length != count)
                    throw new ArgumentException("Byte sequence count did not match its oracle data.", nameof(count));
                _bytes.AddRange(values);
            }

            public void WriteSequenceLength(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                WriteUInt32((uint)count);
            }

            public byte[] ToArray() => _bytes.ToArray();

            private void Align(int alignment)
            {
                while (((_bytes.Count - AlignmentOrigin) % alignment) != 0)
                    _bytes.Add(0);
            }

            private void Append(ReadOnlySpan<byte> bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    _bytes.Add(bytes[index]);
            }
        }

    }
}
