// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: One isolated topic, bridge instance, publisher and wire monitor for a single frame_id limit case.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using RosImu = sensor_msgs.msg.Imu;

/// <summary>Owns the bridge and endpoints of one lane, so every rejection it counts belongs to this lane's oversize value.</summary>
internal sealed class FrameIdLane : IDisposable
{
    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(15);

    private readonly ROS2.ROS2UnityComponent _unity;
    private readonly ImuBridgeHarness _harness;
    private readonly ROS2.QualityOfServiceProfile _qos;
    private readonly ROS2.ROS2Node _node;
    private readonly ROS2.Publisher<RosImu> _publisher;
    private readonly ROS2.Subscription<RosImu> _monitor;
    private string _watchedFrameId = string.Empty;
    private int _watchedOnWire;

    internal FrameIdLane(ROS2.ROS2UnityComponent unity, string lane, string topic)
    {
        _unity = unity;
        Name = lane;
        _harness = new ImuBridgeHarness(unity, topic);
        _harness.Enable();
        _qos = new ROS2.QualityOfServiceProfile(ROS2.QosPresetProfile.DEFAULT);
        _node = unity.CreateNode("u2f_imu_frame_id_probe_" + lane + "_" + Environment.ProcessId);
        _publisher = _node.CreatePublisher<RosImu>(topic, _qos);
        _monitor = _node.CreateSubscription<RosImu>(topic, OnMonitoredSample, _qos);
    }

    internal string Name { get; }

    internal void Accept(string label, string frameId, bool requireNoRejection)
    {
        var rejectedBefore = _harness.Bridge.Stats.Rejected;
        FrameIdLimitScenario.Check(FrameIdLimitScenario.RunUntil(PhaseTimeout, () =>
        {
            _harness.Update();
            Publish(frameId);
            return _harness.Delivered.Contains(frameId);
        }), "Bridge did not deliver the " + label + " frame_id.");
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(Describe("accepted", label, frameId) + " rejected_delta=" + (stats.Rejected - rejectedBefore));
        if (requireNoRejection)
            FrameIdLimitScenario.Check(stats.Rejected == rejectedBefore, "Bridge rejected an admissible " + label + " frame_id: " + stats.LastError);
    }

    internal void Reject(string label, string frameId)
    {
        var rejectedBefore = _harness.Bridge.Stats.Rejected;
        Volatile.Write(ref _watchedFrameId, frameId);
        FrameIdLimitScenario.Check(FrameIdLimitScenario.RunUntil(PhaseTimeout, () =>
        {
            _harness.Update();
            Publish(frameId);
            return _harness.Bridge.Stats.Rejected > rejectedBefore && Volatile.Read(ref _watchedOnWire) > 0;
        }), "Bridge did not reject the " + label + " frame_id that reached the wire.");
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(
            Describe("rejected", label, frameId)
            + " rejected_delta=" + (stats.Rejected - rejectedBefore)
            + " on_wire=" + Volatile.Read(ref _watchedOnWire)
            + " last_error=\"" + stats.LastError + "\"");
        FrameIdLimitScenario.Check(
            stats.LastError == FrameIdLimitScenario.PreCopyRejection,
            "Bridge rejected the " + label + " frame_id after the managed copy: " + stats.LastError);
        FrameIdLimitScenario.Check(!_harness.Delivered.Contains(frameId), "Bridge delivered the " + label + " frame_id.");
    }

    /// <summary>Checks that every delivered frame_id equals one of the lane's admissible published values.</summary>
    internal void CheckOnlyDelivered(params string[] admissible)
    {
        var unexpected = _harness.Delivered.Count(id => !admissible.Contains(id));
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(
            "LANE " + Name
            + " delivered=" + _harness.Delivered.Count
            + " unexpected_delivered=" + unexpected
            + " received=" + stats.Received
            + " dropped=" + stats.Dropped
            + " rejected=" + stats.Rejected
            + " last_error=\"" + stats.LastError + "\"");
        FrameIdLimitScenario.Check(unexpected == 0, "Bridge delivered a frame_id that differs from every admissible published value.");
    }

    public void Dispose()
    {
        _harness.Disable();
        _node.RemoveSubscription(_monitor);
        _node.RemovePublisher(_publisher);
        _unity.TryRemoveNode(_node);
        _qos.Dispose();
    }

    private void OnMonitoredSample(RosImu message)
    {
        var frameId = message?.Header?.Frame_id;
        if (frameId != null && frameId == Volatile.Read(ref _watchedFrameId))
            Interlocked.Increment(ref _watchedOnWire);
    }

    private void Publish(string frameId)
    {
        using var message = new RosImu
        {
            Header = new std_msgs.msg.Header
            {
                Stamp = new builtin_interfaces.msg.Time { Sec = 1, Nanosec = 0 },
                Frame_id = frameId
            },
            Orientation = new geometry_msgs.msg.Quaternion { W = 1 },
            Angular_velocity = new geometry_msgs.msg.Vector3(),
            Linear_acceleration = new geometry_msgs.msg.Vector3 { Z = 9.81 }
        };
        _publisher.Publish(message);
    }

    private string Describe(string phase, string label, string frameId)
        => "PHASE " + phase + " lane=" + Name + " case=" + label
           + " bytes=" + Encoding.UTF8.GetByteCount(frameId)
           + " chars=" + frameId.Length;
}
