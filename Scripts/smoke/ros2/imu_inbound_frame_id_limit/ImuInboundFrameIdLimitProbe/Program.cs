// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Drive the real IMU inbound bridge over live ros2cs DDS for the frame_id limit and SROS2 receive checks.
// Usage: <distro> limit | <distro> receive <topic> <timeout-seconds> <expected-frame-id> <forbidden-frame-id>
// Receive mode reads `negative` and then `end` from stdin to switch from the trusted phase to the negative phase.

using System;
using System.Globalization;

internal static class Program
{
    private static int Main(string[] args)
    {
        var distro = args.Length > 0 ? args[0] : "unknown";
        var mode = args.Length > 1 ? args[1] : "limit";
        Console.WriteLine("PROBE distro=" + distro + " mode=" + mode + " pid=" + Environment.ProcessId + " runtime_identity=unstaged");

        ROS2.ROS2UnityComponent unity = null;
        ImuBridgeHarness harness = null;
        var result = "FAIL";
        try
        {
            ROS2.Ros2cs.Init();
            unity = new ROS2.ROS2UnityComponent();
            unity.StartExecutor();
            if (mode == "limit")
            {
                new FrameIdLimitScenario(unity, "/u2f_probe/imu_frame_id_limit_" + Environment.ProcessId).Run();
                result = "PASS";
            }
            else if (mode == "receive" && args.Length == 6)
            {
                harness = new ImuBridgeHarness(unity, args[2]);
                harness.Enable();
                var timeout = TimeSpan.FromSeconds(int.Parse(args[3], CultureInfo.InvariantCulture));
                result = new ReceiveScenario(harness, timeout, args[4], args[5]).Run();
            }
            else
            {
                throw new ArgumentException("Usage: <distro> limit | <distro> receive <topic> <timeout-seconds> <expected> <forbidden>");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
        }
        finally
        {
            unity?.StopExecutor();
            harness?.Disable();
            if (ROS2.Ros2cs.Ok())
                ROS2.Ros2cs.Shutdown();
        }

        Console.WriteLine("RESULT distro=" + distro + " mode=" + mode + " status=" + result);
        return result == "PASS" || result == "NEGATIVE_NO_FRAMES" ? 0 : 1;
    }
}
