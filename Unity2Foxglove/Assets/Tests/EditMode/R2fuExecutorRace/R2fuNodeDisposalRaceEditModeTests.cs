// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Remove and dispose nodes while the real ROS2UnityCore executor spins and require the Editor to survive.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using ROS2;

namespace Unity2Foxglove.Tests.EditMode.Ros2ForUnity
{
    /// <summary>Drives the real R2FU core executor while the test thread destroys nodes that it is spinning.</summary>
    public sealed class R2fuNodeDisposalRaceEditModeTests
    {
        private const int Iterations = 200;
        private const double WidenedSpinTimeoutSeconds = 0.05;

        [Test]
        [Category("R2fuExecutorRace")]
        public void RemovingNodesWhileCoreExecutorSpinsKeepsTheProcessAlive()
        {
            RunNodeChurn(
                (core, node, i) => Assert.That(core.TryRemoveNode(node), Is.True, "Node removal failed at iteration " + i + "."));
        }

        [Test]
        [Category("R2fuExecutorRace")]
        public void DisposingNodesWhileCoreExecutorSpinsKeepsTheProcessAlive()
        {
            RunNodeChurn((core, node, i) =>
            {
                node.Dispose();
                Assert.That(core.TryRemoveNode(node), Is.True, "Disposed node was not released at iteration " + i + ".");
            });
        }

        [Test]
        [Category("R2fuExecutorRace")]
        public void NodeRemovalWhileACallbackBlocksTheExecutorFailsBoundedAndCanBeRetried()
        {
            var id = Process.GetCurrentProcess().Id;
            using (var core = new ROS2UnityCore())
            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                Assert.That(core.Ok(), Is.True, "R2FU runtime did not initialize.");
                var blockingNode = core.CreateNode("u2f_r2fu_node_block_" + id);
                var topic = "/u2f_test/r2fu_node_block_" + id;
                var publisher = blockingNode.CreatePublisher<std_msgs.msg.String>(topic);
                var blocked = 0;
                blockingNode.CreateSubscription<std_msgs.msg.String>(topic, _ =>
                {
                    if (Interlocked.Exchange(ref blocked, 1) != 0)
                        return;
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                });
                var target = core.CreateNode("u2f_r2fu_node_target_" + id);
                target.CreateSubscription<std_msgs.msg.String>(topic + "_target", _ => { });
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

                    var attempt = Stopwatch.StartNew();
                    var removed = core.TryRemoveNode(target);
                    var elapsed = attempt.Elapsed;
                    release.Set();
                    Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "Node removal waited without bound while a callback held the executor.");
                    Assert.That(removed, Is.False, "Node removal reported success while the executor still held the spin gate.");
                }
                finally
                {
                    release.Set();
                }

                var probe = target.CreatePublisher<std_msgs.msg.String>(topic + "_probe");
                Assert.That(target.RemovePublisher(probe), Is.True, "The node lost native ownership after a failed removal.");

                var retried = false;
                var retry = Stopwatch.StartNew();
                while (!retried && retry.Elapsed < TimeSpan.FromSeconds(5))
                    retried = core.TryRemoveNode(target);
                Assert.That(retried, Is.True, "The retained node could not be removed after the callback returned.");
                Assert.That(core.TryRemoveNode(target), Is.True, "Repeated removal of a disposed node did not stay idempotent.");

                blockingNode.RemovePublisher(publisher);
                core.RemoveNode(blockingNode);
            }
        }

        private static void RunNodeChurn(Action<ROS2UnityCore, ROS2Node, int> destroy)
        {
            var id = Process.GetCurrentProcess().Id;
            using (var core = new ROS2UnityCore())
            {
                Assert.That(core.Ok(), Is.True, "R2FU runtime did not initialize.");
                var spinTimeout = typeof(ROS2UnityCore).GetField("spinTimeout", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(spinTimeout, Is.Not.Null, "ROS2UnityCore.spinTimeout was not found.");
                var host = core.CreateNode("u2f_r2fu_node_churn_host_" + id);
                var topic = "/u2f_test/r2fu_node_churn_" + id;
                var publisher = host.CreatePublisher<std_msgs.msg.String>(topic);
                var received = 0;
                host.CreateSubscription<std_msgs.msg.String>(topic, _ => Interlocked.Increment(ref received));
                var clock = Stopwatch.StartNew();
                while (Volatile.Read(ref received) == 0 && clock.Elapsed < TimeSpan.FromSeconds(5))
                {
                    using (var message = new std_msgs.msg.String { Data = "r2fu-node-race-live" })
                        publisher.Publish(message);
                    Thread.Sleep(20);
                }
                Assert.That(Volatile.Read(ref received), Is.GreaterThan(0), "No callback ran, so the executor did not spin.");

                // Widen this executor's rcl_wait window so destruction lands inside a blocked wait on the node's silent subscription.
                spinTimeout.SetValue(core, WidenedSpinTimeoutSeconds);
                for (var i = 0; i < Iterations; i++)
                {
                    var node = core.CreateNode("u2f_r2fu_node_churn_" + id + "_" + i);
                    node.CreateSubscription<std_msgs.msg.String>(topic + "_silent", _ => { });
                    Thread.Sleep(5);
                    destroy(core, node, i);
                }

                host.RemovePublisher(publisher);
                core.RemoveNode(host);
            }
        }
    }
}
