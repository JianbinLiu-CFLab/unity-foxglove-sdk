// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Channel publish gate reader concurrency and reentrant write exclusion.

using System;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Core
{
    public sealed class SessionChannelPublishGateTests
    {
        [Fact]
        public void ReadersFromDifferentThreadsProceedConcurrently()
        {
            var gate = new SessionChannelPublishGate();
            using var otherEntered = new ManualResetEventSlim(false);
            using (gate.EnterRead())
            {
                var other = new Thread(() =>
                {
                    using (gate.EnterRead())
                        otherEntered.Set();
                });
                other.Start();
                Assert.True(otherEntered.Wait(TimeSpan.FromSeconds(2)));
                Assert.True(other.Join(TimeSpan.FromSeconds(2)));
            }
        }

        [Fact]
        public void ReentrantWriteWaitsForOtherThreadReaders()
        {
            var gate = new SessionChannelPublishGate();
            using var otherReading = new ManualResetEventSlim(false);
            using var releaseOther = new ManualResetEventSlim(false);
            var other = new Thread(() =>
            {
                using (gate.EnterRead())
                {
                    otherReading.Set();
                    releaseOther.Wait(TimeSpan.FromSeconds(3));
                }
            });
            other.Start();
            Assert.True(otherReading.Wait(TimeSpan.FromSeconds(2)));

            using (gate.EnterRead())
            {
                var otherReleased = false;
                var releaser = new Thread(() =>
                {
                    Thread.Sleep(200);
                    Volatile.Write(ref otherReleased, true);
                    releaseOther.Set();
                });
                releaser.Start();
                using (gate.EnterWrite())
                    Assert.True(Volatile.Read(ref otherReleased));
                Assert.True(releaser.Join(TimeSpan.FromSeconds(2)));
            }

            Assert.True(other.Join(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void ConcurrentReentrantWritesFailInsteadOfDeadlocking()
        {
            var gate = new SessionChannelPublishGate();
            using var otherReading = new ManualResetEventSlim(false);
            using var firstUpgrading = new ManualResetEventSlim(false);
            Exception otherFailure = null;
            var other = new Thread(() =>
            {
                using (gate.EnterRead())
                {
                    otherReading.Set();
                    Assert.True(firstUpgrading.Wait(TimeSpan.FromSeconds(3)));
                    Thread.Sleep(100);
                    otherFailure = Record.Exception(() => gate.EnterWrite().Dispose());
                }
            });
            other.Start();
            Assert.True(otherReading.Wait(TimeSpan.FromSeconds(2)));

            using (gate.EnterRead())
            {
                firstUpgrading.Set();
                using (gate.EnterWrite())
                {
                }
            }

            Assert.True(other.Join(TimeSpan.FromSeconds(3)));
            Assert.IsType<InvalidOperationException>(otherFailure);
        }
    }
}
