// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunInboundTests
    {
        private static MetadataReference[] DynamicCompilationReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                    ?? string.Empty;
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunAttribute).Assembly.Location)
                .Append(typeof(UnityEngine.Vector3).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }


        private sealed class StagedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;
            private bool _hasPending;
            private byte _pending;

            public StagedRecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int AppliedCount { get; private set; }
            public byte LastAppliedValue { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                _pending = payload[0];
                _hasPending = true;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
            {
                if (!_hasPending)
                    return 0;

                LastAppliedValue = _pending;
                AppliedCount++;
                _hasPending = false;
                return 1;
            }
        }

        private sealed class ThrowingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                throw new InvalidOperationException("staging failed");
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class ThrowingFlushInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingFlushInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
                => throw new InvalidOperationException("apply failed");
        }

        private sealed class InheritedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public InheritedRecordingInput(string topic)
            {
                _topic = InputTopic(
                    topic,
                    (FoxRunEncoding)0,
                    hasExplicitEncoding: false);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private static byte[] ClientMessageFrame(uint channelId, byte[] payload)
        {
            var frame = new byte[5 + payload.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        private static FoxgloveInputTopicInfo InputTopic(
            string topic,
            FoxRunEncoding encoding,
            bool hasExplicitEncoding = true,
            bool isStream = false)
            => new(
                topic,
                encoding,
                FoxRunFlow.Subscribe,
                publishTransportIds: null,
                subscribeTransportId:
                    FoxgloveWebSocketTransport.Id,
                hasExplicitEncoding: hasExplicitEncoding,
                supportsWebSocket: true,
                deliveryPolicy:
                    FoxRunDeliveryPolicy.ProviderDefault,
                hasExplicitDeliveryPolicy: false,
                isStream: isStream);

        private enum GeneratedJsonProbeKind
        {
            Inactive,
            Active
        }

        private sealed class GeneratedJsonNestedProbe
        {
            public bool Enabled { get; set; }
        }

        private sealed class GeneratedJsonProbe
        {
            public int Count { get; set; }
            public GeneratedJsonProbeKind Kind { get; set; }
            public string Label { get; set; }
            public GeneratedJsonNestedProbe Nested { get; set; }
        }

        private sealed class RestartInputTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void ReceiveText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void ReceiveBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
