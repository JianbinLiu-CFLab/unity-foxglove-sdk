// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Protocol;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{



public sealed partial class Ros2BridgePublisherPreparationTests
    {
        private static byte[] ReadExecutableV1BaseFrame(
            JObject fixture,
            string baseFrame)
        {
            var vector = Assert.IsType<JObject>(fixture.SelectToken(baseFrame));
            return HexToBytes(vector.Value<string>("frameHex"));
        }

        private static void ReadExecutableV1Body(
            byte[] frame,
            out byte[] header,
            out byte[] payload)
        {
            Assert.True(frame != null && frame.Length >= 16);
            var headerLength = checked((int)ReadUInt32LE(frame, 8));
            var payloadLength = checked((int)ReadUInt32LE(frame, 12));
            Assert.Equal(
                checked(16 + headerLength + payloadLength),
                frame.Length);
            header = new byte[headerLength];
            payload = new byte[payloadLength];
            Buffer.BlockCopy(frame, 16, header, 0, headerLength);
            Buffer.BlockCopy(
                frame,
                16 + headerLength,
                payload,
                0,
                payloadLength);
        }

        private static byte[] RebuildExecutableV1Frame(
            byte[] baseFrame,
            byte[] header,
            byte[] payload)
        {
            var frame = new byte[checked(16 + header.Length + payload.Length)];
            Buffer.BlockCopy(baseFrame, 0, frame, 0, 16);
            WriteUInt32LE(frame, 8, checked((uint)header.Length));
            WriteUInt32LE(frame, 12, checked((uint)payload.Length));
            Buffer.BlockCopy(header, 0, frame, 16, header.Length);
            Buffer.BlockCopy(
                payload,
                0,
                frame,
                16 + header.Length,
                payload.Length);
            return frame;
        }

        private static int FindBytes(byte[] haystack, byte[] needle)
        {
            if (needle == null || needle.Length == 0)
                return -1;
            for (var offset = 0;
                 offset + needle.Length <= haystack.Length;
                 offset++)
            {
                var matches = true;
                for (var index = 0; index < needle.Length; index++)
                {
                    if (haystack[offset + index] == needle[index])
                        continue;
                    matches = false;
                    break;
                }
                if (matches)
                    return offset;
            }
            return -1;
        }

        private static FoxRunResolvedQos ReadAuthorityQos(JObject qos)
            => new(
                ParseAuthorityProfile(qos.Value<string>("profile")),
                ParseAuthorityReliability(qos.Value<string>("reliability")),
                ParseAuthorityDurability(qos.Value<string>("durability")),
                ParseAuthorityHistory(qos.Value<string>("history")),
                qos.Value<int>("depth"));

        private static FoxRunQosProfile ParseAuthorityProfile(string value)
            => value switch
            {
                "default" => FoxRunQosProfile.Default,
                "sensor_data" => FoxRunQosProfile.SensorData,
                "system_default" => FoxRunQosProfile.SystemDefault,
                _ => throw new FormatException("Unknown fixture QoS profile: " + value)
            };

        private static FoxRunQosReliability ParseAuthorityReliability(string value)
            => value switch
            {
                "system_default" => FoxRunQosReliability.SystemDefault,
                "reliable" => FoxRunQosReliability.Reliable,
                "best_effort" => FoxRunQosReliability.BestEffort,
                _ => throw new FormatException("Unknown fixture QoS reliability: " + value)
            };

        private static FoxRunQosDurability ParseAuthorityDurability(string value)
            => value switch
            {
                "system_default" => FoxRunQosDurability.SystemDefault,
                "volatile" => FoxRunQosDurability.Volatile,
                "transient_local" => FoxRunQosDurability.TransientLocal,
                _ => throw new FormatException("Unknown fixture QoS durability: " + value)
            };

        private static FoxRunQosHistory ParseAuthorityHistory(string value)
            => value switch
            {
                "system_default" => FoxRunQosHistory.SystemDefault,
                "keep_last" => FoxRunQosHistory.KeepLast,
                "keep_all" => FoxRunQosHistory.KeepAll,
                _ => throw new FormatException("Unknown fixture QoS history: " + value)
            };

        private static byte[] HexToBytes(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0)
                throw new FormatException("Fixture hex must contain complete bytes.");
            var bytes = new byte[hex.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static string BytesToHex(byte[] bytes)
            => string.Concat(bytes.Select(value => value.ToString("x2")));

        private static uint ReadUInt32LE(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static byte[] ReadWireFrame(Stream stream)
        {
            var fixedHeader = ReadExact(stream, 16);
            if (fixedHeader[0] != 'U'
                || fixedHeader[1] != '2'
                || fixedHeader[2] != 'R'
                || fixedHeader[3] != '2')
            {
                throw new FormatException("U2R2 magic is invalid.");
            }

            var headerLength = ReadUInt32LE(fixedHeader, 8);
            var payloadLength = ReadUInt32LE(fixedHeader, 12);
            if (headerLength == 0
                || headerLength > Ros2BridgeFrameWriter.MaxHeaderBytes
                || payloadLength > Ros2BridgeFrameWriter.MaxPayloadBytes)
            {
                throw new FormatException("U2R2 frame length is invalid.");
            }

            var bodyLength = checked((int)(headerLength + payloadLength));
            var frame = new byte[checked(16 + bodyLength)];
            Buffer.BlockCopy(fixedHeader, 0, frame, 0, fixedHeader.Length);
            var body = ReadExact(stream, bodyLength);
            Buffer.BlockCopy(body, 0, frame, 16, body.Length);
            return frame;
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            var data = new byte[length];
            var offset = 0;
            while (offset < data.Length)
            {
                var read = stream.Read(data, offset, data.Length - offset);
                if (read <= 0)
                    throw new EndOfStreamException("U2R2 stream closed before the frame completed.");
                offset += read;
            }

            return data;
        }

        private static void WriteUInt32LE(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value & 0xff);
            data[offset + 1] = (byte)((value >> 8) & 0xff);
            data[offset + 2] = (byte)((value >> 16) & 0xff);
            data[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        private sealed class LegacyTransport : IRos2BridgeSink
        {
            public List<Ros2BridgeFrame> SentFrames { get; } = new List<Ros2BridgeFrame>();
            public bool IsConnected { get; private set; }
            public void Connect(string host, int port, int timeoutMs) => IsConnected = true;
            public void Send(Ros2BridgeFrame frame, int timeoutMs) => SentFrames.Add(frame);
            public void Disconnect() => IsConnected = false;
            public void Dispose() => Disconnect();
        }

        private static Ros2BridgeFrame Frame(
            string topic,
            string schema,
            FoxRunResolvedQos qos,
            ulong sequence)
            => Ros2BridgeFrame.CreateValidated(
                topic,
                schema,
                "cdr",
                184UL + sequence,
                sequence,
                new byte[] { 0, 1, 0, 0, (byte)sequence },
                qos);
    }
}
