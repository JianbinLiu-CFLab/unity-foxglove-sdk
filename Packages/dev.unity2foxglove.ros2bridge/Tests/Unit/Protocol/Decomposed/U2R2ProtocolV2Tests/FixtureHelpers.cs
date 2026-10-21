// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2Bridge.Tests/Protocol
// Purpose: Cross-language authority for the strict U2R2 v2 envelope and dialect model.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.Tests.Unit.Protocol
{
    public sealed partial class U2R2ProtocolV2Tests
    {
        private static U2R2Message ParseVector(string id)
        {
            var vector = Vector(id);
            return U2R2ProtocolCodec.ParseV2(
                U2R2ProtocolCodec.DecodeFrame(
                    HexToBytes(vector.Value<string>("frameHex"))));
        }

        private static JObject Vector(string id)
            => Assert.IsType<JArray>(LoadV2Authority()["operations"])
                .Values<JObject>()
                .Single(vector => string.Equals(
                    vector.Value<string>("id"),
                    id,
                    StringComparison.Ordinal));

        private static JObject LoadV2Authority()
            => Assert.IsType<JObject>(LoadFixture()["v2"]);

        private static JObject LoadFixture()
            => JObject.Parse(File.ReadAllText(FindFixture()));

        private static string FindFixture()
        {
            const string relative =
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/fixtures/" +
                "u2r2_protocol_vectors.json";
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "Packages"))
                        && Directory.Exists(Path.Combine(current.FullName, "Tools")))
                    {
                        return Path.Combine(
                            current.FullName,
                            relative.Replace('/', Path.DirectorySeparatorChar));
                    }
                    current = current.Parent;
                }
            }
            throw new DirectoryNotFoundException("Could not locate the U2R2 authority fixture.");
        }

        private static void AssertProtocolError(
            string code,
            bool terminal,
            Action action)
        {
            var exception = Assert.Throws<U2R2ProtocolException>(action);
            Assert.Equal(code, exception.ErrorCode);
            Assert.Equal(terminal, exception.Terminal);
        }

        private static byte[] Mutate(byte[] source, Action<byte[]> mutation)
        {
            var clone = (byte[])source.Clone();
            mutation(clone);
            return clone;
        }

        private static byte[] BuildFrame(byte[] header, byte[] payload)
        {
            payload ??= Array.Empty<byte>();
            var result = new byte[16 + header.Length + payload.Length];
            result[0] = (byte)'U';
            result[1] = (byte)'2';
            result[2] = (byte)'R';
            result[3] = (byte)'2';
            result[4] = 1;
            WriteUInt32(result, 8, checked((uint)header.Length));
            WriteUInt32(result, 12, checked((uint)payload.Length));
            Buffer.BlockCopy(header, 0, result, 16, header.Length);
            Buffer.BlockCopy(payload, 0, result, 16 + header.Length, payload.Length);
            return result;
        }

        private static string HeaderJson(byte[] frame)
        {
            var length = checked((int)ReadUInt32(frame, 8));
            return Encoding.UTF8.GetString(frame, 16, length);
        }

        private static byte[] HexToBytes(string hex)
        {
            var result = new byte[hex.Length / 2];
            for (var index = 0; index < result.Length; index++)
                result[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            return result;
        }

        private static string BytesToHex(IEnumerable<byte> bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static uint ReadUInt32(byte[] data, int offset)
            => (uint)(data[offset]
                      | data[offset + 1] << 8
                      | data[offset + 2] << 16
                      | data[offset + 3] << 24);

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }
    }
}
