// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Publish live PointCloud2 traffic across a runtime topic switch and check delivered topic labels.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using RosPointCloud2 = sensor_msgs.msg.PointCloud2;

/// <summary>Owns the probe publisher node and runs the subscribe, retired-window, and resubscribe phases.</summary>
internal sealed class TopicSwitchScenario : IDisposable
{
    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetiredWindow = TimeSpan.FromSeconds(1);

    private readonly ROS2.ROS2UnityComponent _unity;
    private readonly BridgeHarness _harness;
    private readonly string _topicA;
    private readonly string _topicB;
    private readonly ROS2.QualityOfServiceProfile _qos;
    private readonly ROS2.ROS2Node _node;
    private readonly ROS2.Publisher<RosPointCloud2> _publisherA;
    private readonly ROS2.Publisher<RosPointCloud2> _publisherB;
    private readonly ROS2.Subscription<RosPointCloud2> _monitor;
    private int _monitorWindowOpen;
    private int _monitoredLateFrames;
    private int _sequence;

    internal TopicSwitchScenario(ROS2.ROS2UnityComponent unity, BridgeHarness harness, string topicA, string topicB)
    {
        _unity = unity;
        _harness = harness;
        _topicA = topicA;
        _topicB = topicB;
        _qos = new ROS2.QualityOfServiceProfile(ROS2.QosPresetProfile.DEFAULT);
        _node = unity.CreateNode("u2f_pointcloud2_probe_publisher_" + Environment.ProcessId);
        _publisherA = _node.CreatePublisher<RosPointCloud2>(topicA, _qos);
        _publisherB = _node.CreatePublisher<RosPointCloud2>(topicB, _qos);
        _monitor = _node.CreateSubscription<RosPointCloud2>(topicA, OnMonitoredFrame, _qos);
    }

    internal void Run()
    {
        Check(RunUntil(PhaseTimeout, () =>
        {
            _harness.Update();
            Publish(_publisherA, "a-pre-" + _sequence++);
            return _harness.Delivered.Any(item => item.Topic == _topicA && item.FrameId.StartsWith("a-pre-", StringComparison.Ordinal));
        }), "Bridge did not deliver a frame from the initial topic subscription.");
        Console.WriteLine("PHASE subscribed topic=" + _topicA + " delivered=" + _harness.Delivered.Count);

        _harness.Bridge.Topic = _topicB;
        Volatile.Write(ref _monitorWindowOpen, 1);
        RunUntil(RetiredWindow, () =>
        {
            Publish(_publisherA, "a-late-" + _sequence++);
            return false;
        });
        Volatile.Write(ref _monitorWindowOpen, 0);
        var lateOnWire = Volatile.Read(ref _monitoredLateFrames);
        Check(lateOnWire > 0, "No old-topic traffic reached the process during the retired-subscription window.");
        Console.WriteLine("PHASE retired-window late_frames_on_wire=" + lateOnWire);

        var deliveredBeforeSwitch = _harness.Delivered.Count;
        Check(RunUntil(PhaseTimeout, () =>
        {
            _harness.Update();
            Publish(_publisherA, "a-post-" + _sequence);
            Publish(_publisherB, "b-" + _sequence++);
            return _harness.Delivered.Any(item => item.Topic == _topicB && item.FrameId.StartsWith("b-", StringComparison.Ordinal));
        }), "Bridge did not deliver a frame from the reconfigured topic subscription.");

        CheckAfterSwitch(deliveredBeforeSwitch);
    }

    public void Dispose()
    {
        _node.RemoveSubscription(_monitor);
        _node.RemovePublisher(_publisherA);
        _node.RemovePublisher(_publisherB);
        _unity.TryRemoveNode(_node);
        _qos.Dispose();
    }

    private void CheckAfterSwitch(int deliveredBeforeSwitch)
    {
        var afterSwitch = _harness.Delivered.Skip(deliveredBeforeSwitch).ToList();
        var mislabeled = afterSwitch.Count(item => item.Topic == _topicB && item.FrameId.StartsWith("a-", StringComparison.Ordinal));
        var retiredDelivered = afterSwitch.Count(item => item.FrameId.StartsWith("a-late-", StringComparison.Ordinal));
        var newOnOldLabel = afterSwitch.Count(item => item.Topic == _topicA && item.FrameId.StartsWith("b-", StringComparison.Ordinal));
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(
            "PHASE resubscribed topic=" + _topicB
            + " delivered_after_switch=" + afterSwitch.Count
            + " mislabeled_old_frames=" + mislabeled
            + " retired_frames_delivered=" + retiredDelivered
            + " new_frames_on_old_label=" + newOnOldLabel
            + " rejected=" + stats.Rejected);

        Check(mislabeled == 0, "Old-topic frames were delivered under the reconfigured topic label.");
        Check(retiredDelivered == 0, "Callbacks from the retired subscription were delivered after the topic switch.");
        Check(newOnOldLabel == 0, "New-topic frames were delivered under the retired topic label.");
        Check(stats.Rejected == 0, "Bridge rejected valid PointCloud2 frames: " + stats.LastError);
    }

    private void OnMonitoredFrame(RosPointCloud2 message)
    {
        var frameId = message?.Header?.Frame_id;
        if (Volatile.Read(ref _monitorWindowOpen) == 1
            && frameId != null
            && frameId.StartsWith("a-late-", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _monitoredLateFrames);
        }
    }

    private static void Publish(ROS2.Publisher<RosPointCloud2> publisher, string frameId)
    {
        using var message = new RosPointCloud2
        {
            Header = new std_msgs.msg.Header
            {
                Stamp = new builtin_interfaces.msg.Time { Sec = 1, Nanosec = 0 },
                Frame_id = frameId
            },
            Height = 1,
            Width = 1,
            Fields = new[] { new sensor_msgs.msg.PointField { Name = "x", Offset = 0, Datatype = 7, Count = 1 } },
            Is_bigendian = false,
            Point_step = 4,
            Row_step = 4,
            Data = new byte[4],
            Is_dense = true
        };
        publisher.Publish(message);
    }

    private static bool RunUntil(TimeSpan timeout, Func<bool> step)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            if (step())
                return true;
            Thread.Sleep(20);
        }

        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
