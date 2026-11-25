// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Remove subscriptions while the real ROS2UnityCore executor spins and require the Editor to survive.

using System.Diagnostics;
using System.Threading;
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
    }
}
