// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Publish live IMU samples at and above the 256-byte frame_id limit and check bridge admission.

using System;
using System.Diagnostics;
using System.Threading;

/// <summary>Runs one isolated lane per oversize encoding so rejections cannot be attributed across cases.</summary>
internal sealed class FrameIdLimitScenario
{
    internal const string PreCopyRejection = "IMU frame_id exceeds 256 bytes before managed copy";

    private readonly ROS2.ROS2UnityComponent _unity;
    private readonly string _topicPrefix;

    internal FrameIdLimitScenario(ROS2.ROS2UnityComponent unity, string topicPrefix)
    {
        _unity = unity;
        _topicPrefix = topicPrefix;
    }

    internal void Run()
    {
        RunLane("ascii", new string('a', 256), new string('b', 257), new string('c', 256));
        RunLane("utf8", new string('é', 128), new string('é', 128) + "b", new string('é', 127) + "cc");
    }

    private void RunLane(string lane, string admissible, string oversize, string afterRejection)
    {
        using var probe = new FrameIdLane(_unity, lane, _topicPrefix + "/" + lane);
        probe.Accept(lane + "-256", admissible, requireNoRejection: true);
        probe.Reject(lane + "-257", oversize);
        probe.Accept(lane + "-256-after-rejection", afterRejection, requireNoRejection: false);
        probe.CheckOnlyDelivered(admissible, afterRejection);
    }

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
