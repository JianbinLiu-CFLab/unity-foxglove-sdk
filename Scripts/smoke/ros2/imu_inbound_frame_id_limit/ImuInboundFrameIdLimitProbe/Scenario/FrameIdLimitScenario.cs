// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Publish live IMU samples at and above the 256-byte frame_id limit and check bridge admission.

using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using RosImu = sensor_msgs.msg.Imu;

/// <summary>Owns the probe publisher and an independent wire monitor for the frame_id limit phases.</summary>
internal sealed class FrameIdLimitScenario : IDisposable
{
    internal const string PreCopyRejection = "IMU frame_id exceeds 256 bytes before managed copy";

    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(1);

    private readonly ROS2.ROS2UnityComponent _unity;
    private readonly ImuBridgeHarness _harness;
    private readonly ROS2.QualityOfServiceProfile _qos;
    private readonly ROS2.ROS2Node _node;
    private readonly ROS2.Publisher<RosImu> _publisher;
    private readonly ROS2.Subscription<RosImu> _monitor;
    private string _watchedFrameId = string.Empty;
    private int _watchedOnWire;

    internal FrameIdLimitScenario(ROS2.ROS2UnityComponent unity, ImuBridgeHarness harness, string topic)
    {
        _unity = unity;
        _harness = harness;
        _qos = new ROS2.QualityOfServiceProfile(ROS2.QosPresetProfile.DEFAULT);
        _node = unity.CreateNode("u2f_imu_frame_id_probe_publisher_" + Environment.ProcessId);
        _publisher = _node.CreatePublisher<RosImu>(topic, _qos);
        _monitor = _node.CreateSubscription<RosImu>(topic, OnMonitoredSample, _qos);
    }

    internal void Run()
    {
        var accepted = new[] { new string('a', 256), new string('é', 128), new string('c', 256) };
        Accept("ascii-256", accepted[0]);
        Accept("utf8-256", accepted[1]);
        Reject("ascii-257", new string('b', 257));
        Reject("utf8-257", new string('é', 128) + "b");
        RunUntil(SettleWindow, () =>
        {
            _harness.Update();
            return false;
        });
        Accept("ascii-256-after-rejections", accepted[2]);

        var unexpected = _harness.Delivered.Count(id => !accepted.Contains(id));
        Console.WriteLine("PHASE settled delivered=" + _harness.Delivered.Count + " unexpected_delivered=" + unexpected);
        Check(unexpected == 0, "Bridge delivered a frame_id that differs from every admissible published value.");
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(
            "STATS received=" + stats.Received
            + " delivered=" + stats.Delivered
            + " dropped=" + stats.Dropped
            + " rejected=" + stats.Rejected
            + " last_error=\"" + stats.LastError + "\"");
    }

    public void Dispose()
    {
        _node.RemoveSubscription(_monitor);
        _node.RemovePublisher(_publisher);
        _unity.TryRemoveNode(_node);
        _qos.Dispose();
    }

    private void Accept(string label, string frameId)
    {
        var rejectedBefore = _harness.Bridge.Stats.Rejected;
        Check(RunUntil(PhaseTimeout, () =>
        {
            _harness.Update();
            Publish(frameId);
            return _harness.Delivered.Contains(frameId);
        }), "Bridge did not deliver the " + label + " frame_id.");
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(Describe("accepted", label, frameId) + " rejected_delta=" + (stats.Rejected - rejectedBefore));
        Check(stats.Rejected == rejectedBefore, "Bridge rejected an admissible " + label + " frame_id: " + stats.LastError);
    }

    private void Reject(string label, string frameId)
    {
        var rejectedBefore = _harness.Bridge.Stats.Rejected;
        Volatile.Write(ref _watchedFrameId, frameId);
        Interlocked.Exchange(ref _watchedOnWire, 0);
        Check(RunUntil(PhaseTimeout, () =>
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
        Check(stats.LastError == PreCopyRejection, "Bridge rejected the " + label + " frame_id after the managed copy: " + stats.LastError);
        Check(!_harness.Delivered.Contains(frameId), "Bridge delivered the " + label + " frame_id.");
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

    private static string Describe(string phase, string label, string frameId)
        => "PHASE " + phase + " case=" + label
           + " bytes=" + Encoding.UTF8.GetByteCount(frameId)
           + " chars=" + frameId.Length;

    internal static bool RunUntil(TimeSpan timeout, Func<bool> step)
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

    internal static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
