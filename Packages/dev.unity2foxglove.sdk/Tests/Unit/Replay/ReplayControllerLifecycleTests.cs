// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Replay
{
    public sealed class ReplayControllerLifecycleTests
    {
        [Fact]
        public void DisableStopsCallbacksAlreadyTransferredToTheDrain()
        {
            using var controller = new ReplayController(new ConsoleLogger(), null, null);
            var disableReturned = false;
            var callbacksAfterDisable = 0;

            controller.OnReplayMessageContext += _ =>
            {
                controller.Disable();
                disableReturned = true;
            };
            controller.OnReplayMessageContext += _ =>
            {
                if (disableReturned)
                    callbacksAfterDisable++;
            };

            controller.FireForTests("/phase187/f04", new byte[] { 1 });

            Assert.Equal(0, callbacksAfterDisable);
        }

        [Fact]
        public void ReplayTransportFanoutDoesNotHoldEngineLock()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase187-replay-lock-order-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/phase187/replay-lock-order", "json", "", "", "");
                    recorder.WriteMessage(1, 1, new byte[] { 1 });
                    recorder.Close();
                }

                using var transport = new BlockingTransport();
                using var session = new FoxgloveSession("replay-lock-order", transport);
                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.RegisterChannels(session);
                transport.ReceiveText(
                    1,
                    "{\"op\":\"subscribe\",\"subscriptions\":[{\"id\":1,\"channelId\":32769}]}" );

                var tick = Task.Run(() => controller.Tick(session, 1, deferCallbacks: true));
                Assert.True(transport.SendEntered.Wait(TimeSpan.FromSeconds(5)));

                var seek = Task.Run(() => controller.Seek(1));
                Assert.True(seek.Wait(TimeSpan.FromSeconds(2)), "Replay seek waited for transport fanout while engine lock was held.");

                transport.ReleaseSend.Set();
                Assert.True(tick.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void SnapshotDropDoesNotPublishACompleteBatchBoundary()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase187-f04-callback-budget-" + Guid.NewGuid().ToString("N") + ".mcap");
            var firstPayload = new byte[40 * 1024 * 1024];
            var secondPayload = new byte[40 * 1024 * 1024];
            firstPayload[0] = 1;
            secondPayload[0] = 2;

            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/phase187/f04/a", "json", "", "", "");
                    recorder.AddChannel(2, "/phase187/f04/b", "json", "", "", "");
                    recorder.WriteMessage(1, 1_000_000UL, firstPayload);
                    recorder.WriteMessage(2, 2_000_000UL, secondPayload);
                    recorder.Close();
                }

                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                var deliveredMessages = new List<ReplayMessageContext>();
                var batchCount = 0;
                controller.OnReplayMessageContext += context => deliveredMessages.Add(context);
                controller.OnReplayBatchCompleted += _ => batchCount++;

                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.ApplySnapshotToScene(2_000_000UL, deferCallbacks: true);
                controller.DrainReplayCallbacks();

                Assert.Empty(deliveredMessages);
                Assert.Equal(0, batchCount);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DisableReleasesTickAndSnapshotPayloadBuffers()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "phase187-f04-buffer-retirement-" + Guid.NewGuid().ToString("N") + ".mcap");
            var firstPayload = new byte[] { 1, 2, 3, 4 };
            var secondPayload = new byte[] { 5, 6, 7, 8, 9 };

            try
            {
                using (var stream = File.Create(path))
                using (var recorder = new McapRecorder(stream))
                {
                    recorder.AddChannel(1, "/phase187/f04/buffer-a", "json", "", "", "");
                    recorder.AddChannel(2, "/phase187/f04/buffer-b", "json", "", "", "");
                    recorder.WriteMessage(1, 1_000_000UL, firstPayload);
                    recorder.WriteMessage(2, 2_000_000UL, secondPayload);
                    recorder.Close();
                }

                using var controller = new ReplayController(new ConsoleLogger(), null, null);
                controller.Enable(path, SchemaIdentityMode.Off);
                Assert.True(controller.IsEnabled, controller.LastEnableFailureMessage);
                controller.ApplyTickToScene(2_000_000UL);
                Assert.Equal((2, 9L), BufferStats(controller, "_replayTickBuffer"));

                controller.ApplySnapshotToScene(2_000_000UL, deferCallbacks: true);
                Assert.Equal((2, 9L), BufferStats(controller, "_replaySnapshotBuffer"));

                controller.Disable();
                Assert.Equal((0, 0L), BufferStats(controller, "_replayTickBuffer"));
                Assert.Equal((0, 0L), BufferStats(controller, "_replaySnapshotBuffer"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static (int Count, long PayloadBytes) BufferStats(ReplayController controller, string fieldName)
        {
            var field = typeof(ReplayController).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var messages = (IEnumerable)field.GetValue(controller);
            var count = 0;
            long payloadBytes = 0;
            foreach (var value in messages)
            {
                count++;
                payloadBytes += ((McapMessage)value).Data?.Length ?? 0;
            }

            return (count, payloadBytes);
        }

        private sealed class BlockingTransport : IFoxgloveTransport
        {
            internal readonly ManualResetEventSlim SendEntered = new();
            internal readonly ManualResetEventSlim ReleaseSend = new();
            public bool IsRunning => true;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;
            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) => SendBinary(1, data);
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data)
            {
                SendEntered.Set();
                ReleaseSend.Wait(TimeSpan.FromSeconds(5));
            }
            public void ReceiveText(uint clientId, string text) => OnTextReceived?.Invoke(clientId, text);
            public void Dispose()
            {
                ReleaseSend.Set();
                SendEntered.Dispose();
                ReleaseSend.Dispose();
            }
        }
    }
}
