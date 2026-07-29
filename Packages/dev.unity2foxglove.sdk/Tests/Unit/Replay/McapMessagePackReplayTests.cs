// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Replay
// Purpose: Phase185-D generic MessagePack replay preservation coverage.

using System;
using System.IO;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    public sealed class McapMessagePackReplayTests
    {
        [Fact]
        [Trait("Phase", "185-D")]
        public void GenericReplayContextPreservesRawMessagePackEncodingAndBytes()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase185-msgpack-replay-" + Guid.NewGuid().ToString("N") + ".mcap");
            var payload = Payload();
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(
                        1,
                        "/phase185/replay",
                        "msgpack",
                        string.Empty,
                        string.Empty,
                        string.Empty);
                    recorder.WriteMessage(1, 185_000UL, payload);
                    recorder.Close();
                }

                using var controller = new ReplayController(
                    new ConsoleLogger(),
                    recordingState: null,
                    clock: null);
                ReplayMessageContext? observed = null;
                controller.OnReplayMessageContext += context => observed = context;

                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.ApplyTickToScene(185_000UL);

                Assert.True(observed.HasValue);
                Assert.Equal("/phase185/replay", observed.Value.Topic);
                Assert.Equal("msgpack", observed.Value.MessageEncoding);
                Assert.Equal(string.Empty, observed.Value.SchemaName);
                Assert.Equal(string.Empty, observed.Value.SchemaEncoding);
                Assert.Equal(payload, observed.Value.Payload);
                Assert.Equal(
                    ReplayChannelBehavior.NonPose,
                    ReplayChannelBehaviorClassifier.ClassifyChannel(
                        observed.Value.MessageEncoding,
                        observed.Value.SchemaName,
                        observed.Value.SchemaEncoding,
                        observed.Value.Topic));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static byte[] Payload()
        {
            using var writer = new FoxgloveMsgPackWriter();
            writer.WriteMapHeader(1);
            writer.WriteString("value");
            writer.WriteInt64(9_007_199_254_740_993L);
            return writer.ToArray();
        }
    }
}
