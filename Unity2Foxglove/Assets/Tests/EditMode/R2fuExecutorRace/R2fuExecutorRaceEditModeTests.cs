// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Remove subscriptions while the real ROS2UnityCore executor spins and require the Editor to survive.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ROS2;

namespace Unity2Foxglove.Tests.EditMode.Ros2ForUnity
{
    /// <summary>Drives the real R2FU core executor while the test thread removes subscriptions.</summary>
    public sealed class R2fuExecutorRaceEditModeTests
    {
        private const int Iterations = 2000;

        [Test]
        [Category("R2fuExecutorRace")]
        public void RemovingSubscriptionsWhileCoreExecutorSpinsKeepsTheProcessAlive()
        {
            var id = Process.GetCurrentProcess().Id;
            using (var core = new ROS2UnityCore())
            {
                Assert.That(core.Ok(), Is.True, "R2FU runtime did not initialize.");
                var node = core.CreateNode("u2f_r2fu_race_core_" + id);
                var topic = "/u2f_test/r2fu_race_core_" + id;
                var publisher = node.CreatePublisher<std_msgs.msg.String>(topic);
                var received = 0;
                for (var i = 0; i < Iterations; i++)
                {
                    var subscription = node.CreateSubscription<std_msgs.msg.String>(
                        topic,
                        _ => Interlocked.Increment(ref received));
                    using (var message = new std_msgs.msg.String { Data = "r2fu-race-" + i })
                        publisher.Publish(message);
                    Thread.Sleep(i % 3);
                    Assert.That(node.RemoveSubscription(subscription), Is.True, "Subscription removal failed at iteration " + i + ".");
                }

                node.RemovePublisher(publisher);
                core.RemoveNode(node);
                Assert.That(Volatile.Read(ref received), Is.GreaterThan(0), "No callback ran, so the executor did not spin.");
            }
        }

        [Test]
        [Category("R2fuExecutorRace")]
        public void RemovalWhileACallbackBlocksTheExecutorFailsBoundedAndCanBeRetried()
        {
            var id = Process.GetCurrentProcess().Id;
            using (var core = new ROS2UnityCore())
            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                Assert.That(core.Ok(), Is.True, "R2FU runtime did not initialize.");
                var node = core.CreateNode("u2f_r2fu_blocked_core_" + id);
                var blockingTopic = "/u2f_test/r2fu_blocked_core_" + id;
                var publisher = node.CreatePublisher<std_msgs.msg.String>(blockingTopic);
                var blocked = 0;
                var blocking = node.CreateSubscription<std_msgs.msg.String>(blockingTopic, _ =>
                {
                    if (Interlocked.Exchange(ref blocked, 1) != 0)
                        return;
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                });
                var other = node.CreateSubscription<std_msgs.msg.String>(blockingTopic + "_other", _ => { });
                try
                {
                    var clock = Stopwatch.StartNew();
                    while (!entered.IsSet && clock.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        using (var message = new std_msgs.msg.String { Data = "block" })
                            publisher.Publish(message);
                        entered.Wait(20);
                    }
                    Assert.That(entered.IsSet, Is.True, "The blocking callback never ran.");

                    var attempt = Task.Run(() => node.RemoveSubscription(other));
                    var finished = attempt.Wait(TimeSpan.FromSeconds(5));
                    release.Set();
                    Assert.That(finished, Is.True, "Removal waited without bound while a callback held the executor.");
                    Assert.That(attempt.Result, Is.False, "Removal reported success while the executor still held the spin gate.");
                }
                finally
                {
                    release.Set();
                }

                var removed = false;
                var retry = Stopwatch.StartNew();
                while (!removed && retry.Elapsed < TimeSpan.FromSeconds(5))
                    removed = node.RemoveSubscription(other);
                Assert.That(removed, Is.True, "The retained subscription could not be removed after the callback returned.");

                node.RemoveSubscription(blocking);
                node.RemovePublisher(publisher);
                core.RemoveNode(node);
            }
        }
    }
}
