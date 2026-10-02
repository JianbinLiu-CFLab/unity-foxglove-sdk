// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Verifies post-start ROS 2 channel registration reuses enabled CDR.

using System;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity2Foxglove.Ros2Bridge.UnitTests.FoxRun
{
    public sealed class Ros2BridgeMcapCodecsBehaviorTests
    {
        [Fact]
        public void RegisterRos2MsgSchemaChannelAfterStartReusesEnabledCdrEncoding()
        {
            using var session = new FoxgloveSession(
                "ros2bridge-post-start-cdr",
                new NoopTransport());
            session.EnableMessageEncoding(Ros2BridgeMcapCodecs.MessageEncoding);
            session.Start("127.0.0.1", 0);

            session.RegisterRos2MsgSchemaChannel(
                1U,
                "/post-start/pointcloud",
                "foxglove_msgs/msg/PointCloud");

            var channel = session.Channels.Get(1U);
            Assert.NotNull(channel);
            Assert.Equal(Ros2BridgeMcapCodecs.MessageEncoding, channel.Encoding);
            Assert.Equal(Ros2BridgeMcapCodecs.SchemaEncoding, channel.SchemaEncoding);
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;

            public void Stop() => IsRunning = false;

            public void BroadcastText(string json) { }

            public void BroadcastBinary(byte[] data) { }

            public void SendText(uint clientId, string json) { }

            public void SendBinary(uint clientId, byte[] data) { }

            public void Dispose() => IsRunning = false;
        }
    }
}
