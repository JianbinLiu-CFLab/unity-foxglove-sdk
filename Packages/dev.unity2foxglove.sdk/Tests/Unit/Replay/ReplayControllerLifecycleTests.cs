// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
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

                Assert.Single(deliveredMessages);
                Assert.Equal(40 * 1024 * 1024, deliveredMessages[0].Payload.Length);
                Assert.Equal(0, batchCount);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
