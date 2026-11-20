// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Drive the real PointCloud2 inbound bridge over live ros2cs DDS through a runtime topic switch.

using System;

internal static class Program
{
    private static int Main(string[] args)
    {
        var distro = args.Length > 0 ? args[0] : "unknown";
        var prefix = "/u2f_probe/pointcloud2_topic_switch_" + Environment.ProcessId;
        Console.WriteLine("PROBE distro=" + distro + " pid=" + Environment.ProcessId + " runtime_identity=unstaged");

        ROS2.ROS2UnityComponent unity = null;
        BridgeHarness harness = null;
        TopicSwitchScenario scenario = null;
        var status = 1;
        try
        {
            ROS2.Ros2cs.Init();
            unity = new ROS2.ROS2UnityComponent();
            unity.StartExecutor();
            harness = new BridgeHarness(unity, prefix + "/a");
            harness.Enable();
            scenario = new TopicSwitchScenario(unity, harness, prefix + "/a", prefix + "/b");
            scenario.Run();
            status = 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
        finally
        {
            unity?.StopExecutor();
            harness?.Disable();
            scenario?.Dispose();
            if (ROS2.Ros2cs.Ok())
                ROS2.Ros2cs.Shutdown();
        }

        Console.WriteLine("RESULT distro=" + distro + " status=" + (status == 0 ? "PASS" : "FAIL"));
        return status;
    }
}
