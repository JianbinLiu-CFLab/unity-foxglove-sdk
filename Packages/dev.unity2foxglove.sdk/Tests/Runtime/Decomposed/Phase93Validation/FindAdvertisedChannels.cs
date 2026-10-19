// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 93 validation for full ROS 2 .msg CDR serializer parity.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Unity2Foxglove.Ros2Bridge;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase93Validation.
    /// </summary>
    public static partial class Phase93Validation
    {
        private static List<JObject> FindAdvertisedChannels(IEnumerable<string> texts)
        {
            var result = new List<JObject>();
            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var obj = JObject.Parse(text);
                if (obj["op"]?.ToString() != "advertise")
                    continue;
                foreach (var channel in obj["channels"] ?? Enumerable.Empty<JToken>())
                    if (channel is JObject channelObject)
                        result.Add(channelObject);
            }

            return result;
        }

        private static bool HasCdrHeader(byte[] payload)
        {
            return payload != null && payload.Length >= 4 && payload[0] == 0 && payload[1] == 1 && payload[2] == 0 && payload[3] == 0;
        }

        private static bool FixedArrayFailure<TException>(string schemaName, IMessage message)
            where TException : Exception
        {
            try
            {
                Ros2CdrSerializerRegistry.Serialize(schemaName, message);
                return false;
            }
            catch (TException)
            {
                return true;
            }
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

        private sealed class Phase93Sample
        {
            public Phase93Sample(string topic, string schemaName, byte[] payload)
            {
                Topic = topic;
                SchemaName = schemaName;
                Payload = payload;
            }

            public string Topic { get; }
            public string SchemaName { get; }
            public byte[] Payload { get; }
        }

        private sealed class Phase93FakeTransport : IFoxgloveTransport
        {
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public bool IsRunning { get; private set; }
            public string LastBroadcastText;
            public readonly List<string> SentTexts = new List<string>();
            public readonly List<byte[]> SentBinaries = new List<byte[]>();

            /// <summary>
            /// Validation method for Start.
            /// </summary>
            /// <param name="host">Host address used by the validation client or listener.</param>
            /// <param name="port">TCP port used by the validation client or listener.</param>
            public void Start(string host, int port) => IsRunning = true;
            /// <summary>
            /// Validation method for Stop.
            /// </summary>
            public void Stop() => IsRunning = false;
            /// <summary>
            /// Validation method for BroadcastText.
            /// </summary>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void BroadcastText(string json)
            {
                LastBroadcastText = json;
                SentTexts.Add(json);
            }

            /// <summary>
            /// Validation method for BroadcastBinary.
            /// </summary>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void BroadcastBinary(byte[] data) { }
            /// <summary>
            /// Validation method for SendText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SendText(uint clientId, string json) => SentTexts.Add(json);
            /// <summary>
            /// Validation method for SendBinary.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="data">Binary payload used by the transport stub.</param>
            public void SendBinary(uint clientId, byte[] data) => SentBinaries.Add(data);
            /// <summary>
            /// Validation method for Dispose.
            /// </summary>
            public void Dispose() { }
            /// <summary>
            /// Validation method for SimulateConnect.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            public void SimulateConnect(uint clientId) => OnClientConnected?.Invoke(clientId);
            /// <summary>
            /// Validation method for SimulateText.
            /// </summary>
            /// <param name="clientId">Foxglove client identifier used by the transport stub.</param>
            /// <param name="json">JSON payload used by the transport stub.</param>
            public void SimulateText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
