// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 94 validation for the localhost Unity-to-ROS2 bridge spike.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Foxglove;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase94Validation.
    /// </summary>
    public static partial class Phase94Validation
    {
        private static byte[] ReadExact(Stream stream, int count)
        {
            var buffer = new byte[count];
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException();
                offset += read;
            }

            return buffer;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24));
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }

            return condition();
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string ReadRepoDirectoryText(string relativePath, string searchPattern)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);

            var files = Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file, StringComparer.Ordinal)
                .ToArray();
            if (files.Length == 0)
                throw new FileNotFoundException("Required validation source files were not found.", Path.Combine(path, searchPattern));

            return string.Join("\n", files.Select(File.ReadAllText));
        }

        private sealed class FakeBridgeSink : IRos2BridgeSink
        {
            public readonly List<Ros2BridgeFrame> SentFrames = new List<Ros2BridgeFrame>();
            public bool IsConnected => true;
            /// <summary>
            /// Validation method for Connect.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
            public void Connect(string host, int port, int timeoutMs) { }
            /// <summary>
            /// Validation method for Send.
            /// </summary>
            /// <param name="frame">ROS 2 bridge frame sent by the validation client.</param>
            /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
            public void Send(Ros2BridgeFrame frame, int timeoutMs) => SentFrames.Add(frame);
            /// <summary>
            /// Validation method for Disconnect.
            /// </summary>
            public void Disconnect() { }
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() { }
        }

        private sealed class BridgeSample
        {
            public BridgeSample(string topic, string schemaName, IMessage message)
            {
                Topic = topic;
                SchemaName = schemaName;
                Message = message;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public IMessage Message { get; }
        }

        private sealed class Ros2BridgeFrameCapture
        {
            public Ros2BridgeFrameCapture(string topic, string schemaName, string encoding, ulong logTimeNs, ulong sequence, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Encoding = encoding;
                LogTimeNs = logTimeNs;
                Sequence = sequence;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public string Encoding { get; }
            public ulong LogTimeNs { get; }
            public ulong Sequence { get; }
            public byte[] Payload { get; }
        }
    }
}
