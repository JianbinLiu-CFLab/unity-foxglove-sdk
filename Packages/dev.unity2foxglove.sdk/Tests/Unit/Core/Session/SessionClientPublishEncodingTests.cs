// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Core/Session
// Purpose: Verifies advertised client encoding reaches the session inbound callback.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Core.Session
{
    public sealed class SessionClientPublishEncodingTests
    {
        [Fact]
        public void AdvertisedEncodingsReachInboundCallbackAndUnknownChannelsAreRejected()
        {
            var transport = new ClientPublishTransport();
            var received = new List<(string topic, string encoding, string payload)>();
            using var session = new FoxgloveSession("session-client-encoding", transport);
            session.OnClientMessageWithEncoding += (_, _, topic, encoding, payload) =>
                received.Add((topic, encoding, Encoding.UTF8.GetString(payload)));

            transport.ReceiveText(11,
                "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/phase175/json\",\"encoding\":\"json\"},{\"id\":2,\"topic\":\"/phase175/protobuf\",\"encoding\":\"protobuf\"}]}");
            transport.ReceiveBinary(11, ClientMessageFrame(1, "json-payload"));
            transport.ReceiveBinary(11, ClientMessageFrame(2, "protobuf-payload"));
            transport.ReceiveBinary(11, ClientMessageFrame(3, "must-not-dispatch"));

            Assert.Collection(
                received,
                message =>
                {
                    Assert.Equal("/phase175/json", message.topic);
                    Assert.Equal("json", message.encoding);
                    Assert.Equal("json-payload", message.payload);
                },
                message =>
                {
                    Assert.Equal("/phase175/protobuf", message.topic);
                    Assert.Equal("protobuf", message.encoding);
                    Assert.Equal("protobuf-payload", message.payload);
                });
        }

        [Fact]
        public void RecorderPersistsOriginalInboundPayloadBeforeSubscriberMutation()
        {
            var transport = new ClientPublishTransport();
            using var stream = new MemoryStream();
            using var recorder = new McapRecorder(stream);
            using var session = new FoxgloveSession("session-client-record-order", transport);
            session.SetRecorder(recorder);
            session.OnClientMessageWithEncoding += (_, _, _, _, payload) => payload[0] = (byte)'X';

            transport.ReceiveText(11,
                "{\"op\":\"advertise\",\"channels\":[{\"id\":1,\"topic\":\"/phase180/input\",\"encoding\":\"json\"}]}");
            transport.ReceiveBinary(11, ClientMessageFrame(1, "original"));
            session.SetRecorder(null);
            recorder.Close();

            stream.Position = 0;
            using var reader = new McapStreamingReader(stream, leaveOpen: true);
            var result = reader.Read();

            Assert.Equal("original", Encoding.UTF8.GetString(Assert.Single(result.Messages).Data));
            Assert.Equal("input", Assert.Single(result.Summary.Channels).Metadata["unity2foxglove.direction"]);
        }

        private static byte[] ClientMessageFrame(uint channelId, string payload)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var frame = new byte[5 + payloadBytes.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(payloadBytes, 0, frame, 5, payloadBytes.Length);
            return frame;
        }

        private sealed class ClientPublishTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Connect(uint clientId) => OnClientConnected?.Invoke(clientId);
            public void Disconnect(uint clientId) => OnClientDisconnected?.Invoke(clientId);
            public void ReceiveText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void ReceiveBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
