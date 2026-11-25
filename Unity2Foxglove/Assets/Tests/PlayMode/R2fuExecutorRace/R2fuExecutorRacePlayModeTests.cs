// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Remove subscriptions while the real ROS2UnityComponent executor spins and require the player loop to survive.

using System.Collections;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;
using ROS2;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity2Foxglove.Tests.PlayMode.Ros2ForUnity
{
    /// <summary>Drives the real R2FU component executor, which the native bridges use, while the main thread removes subscriptions.</summary>
    public sealed class R2fuExecutorRacePlayModeTests
    {
        private const int Iterations = 10000;
        private const int StartupFixedUpdates = 10;

        [UnityTest]
        [Category("R2fuExecutorRace")]
        public IEnumerator RemovingSubscriptionsWhileComponentExecutorSpinsKeepsTheProcessAlive()
        {
            var id = Process.GetCurrentProcess().Id;
            var host = new GameObject("u2f-r2fu-race");
            try
            {
                var component = host.AddComponent<ROS2UnityComponent>();
                for (var frame = 0; frame < StartupFixedUpdates; frame++)
                    yield return new WaitForFixedUpdate();
                Assert.That(component.Ok(), Is.True, "R2FU runtime did not initialize.");

                var node = component.CreateNode("u2f_r2fu_race_component_" + id);
                var topic = "/u2f_test/r2fu_race_component_" + id;
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
                component.RemoveNode(node);
                Assert.That(Volatile.Read(ref received), Is.GreaterThan(0), "No callback ran, so the executor did not spin.");
            }
            finally
            {
                Object.Destroy(host);
            }

            yield return null;
        }
    }
}
