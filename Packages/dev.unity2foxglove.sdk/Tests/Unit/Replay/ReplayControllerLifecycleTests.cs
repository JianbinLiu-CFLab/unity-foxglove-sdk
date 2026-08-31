// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Unity.FoxgloveSDK.Core;
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
    }
}
