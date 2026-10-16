// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test runner entry point - discovers and executes all Phase validation tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;

/// <summary>Console entry point for runtime validation and manual smoke servers.</summary>
partial class Program
{
    /// <remarks>Phase192 source sections: ValidationExecution.cs, LegacyTools.cs, CiAndServer.cs</remarks>
    private static readonly (string Flag, string Category, string Name)[] LegacyToolFlags =
    {
        ("--phase97-health", "Tool", "Phase 97 health report generator"),
        ("--phase98-sample-send-all", "Tool", "Phase 98 all-schema sample sender"),
        ("--phase98-live", "Tool", "Phase 98 live evidence generator"),
        ("--phase99-live", "Tool", "Phase 99 release evidence generator"),
        ("--phase94-bridge-send", "Tool", "Phase 94 bridge sender smoke"),
        ("--phase91-ros2-cdr-mcap", "Tool", "Phase 91 ROS2 CDR MCAP generator"),
        ("--phase92-ros2-product-mcap", "Tool", "Phase 92 ROS2 product MCAP generator"),
        ("--phase93-ros2-full-mcap", "Tool", "Phase 93 ROS2 full-schema MCAP generator"),
        ("--phase93-inspect-mcap", "Tool", "Phase 93 ROS2 full-schema MCAP inspector"),
        ("--phase68-indexed-reader-smoke", "Tool", "Phase 68 indexed reader external MCAP smoke"),
        ("--phase44-all-schemas-mcap", "Tool", "Phase 44 all-schema MCAP generator"),
        ("--phase185-inspect-mcap", "Tool", "Phase185 typed MessagePack output MCAP inspector"),
        ("--phase189-inspect-mcap", "Tool", "Phase189 Component MessagePack identity MCAP inspector"),
        ("--phase139b-remote-data-loader-server", "Manual", "Phase 139B remote data loader server"),
    };

    /// <summary>
    /// Dispatches to test runner or interactive server mode based on
    /// command-line arguments. <c>--serve</c> starts a manual test
    /// server; default runs all validation phases.
    /// </summary>
    static int Main(string[] args)
    {
        try
        {
            return MainCore(args);
        }
        finally
        {
            TempMcapHelper.Cleanup();
        }
    }

    private static int MainCore(string[] args)
    {
        var argList = args.ToList();
        var argSet = new HashSet<string>(argList, StringComparer.Ordinal);

        if (argList.Count(argument => string.Equals(argument, "--local-evidence", StringComparison.Ordinal)) > 1)
        {
            Console.Error.WriteLine("--local-evidence may be supplied only once.");
            return 1;
        }

        if (argSet.Contains("--serve"))
        {
            string invalidServeArgument = null;
            if (argList.Count(argument => argument == "--port") > 1)
            {
                Console.Error.WriteLine("--port may be supplied only once.");
                return 1;
            }
            for (var index = 0; index < argList.Count; index++)
            {
                var argument = argList[index];
                var allowed = argument == "--serve" || argument == "--demo" || argument == "--demo3d";
                if (argument == "--port")
                    allowed = index + 1 < argList.Count && int.TryParse(argList[index + 1], out _);
                else if (index > 0 && argList[index - 1] == "--port")
                    allowed = int.TryParse(argument, out _);
                if (!allowed)
                {
                    invalidServeArgument = argument;
                    break;
                }
            }
            if (invalidServeArgument != null)
            {
                Console.Error.WriteLine("--serve cannot be combined with " + invalidServeArgument + ".");
                return 1;
            }

            int port = 8765;
            var portIdx = argList.IndexOf("--port");
            if (portIdx >= 0 && portIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--port requires an integer.");
                return 1;
            }
            if (portIdx >= 0)
            {
                if (!int.TryParse(argList[portIdx + 1], out port))
                {
                    Console.Error.WriteLine("--port must be an integer.");
                    return 1;
                }
            }

            var demo = argSet.Contains("--demo");
            var demo3d = argSet.Contains("--demo3d");
            if (demo && demo3d)
            {
                Console.Error.WriteLine("--demo and --demo3d cannot be used together.");
                return 1;
            }

            if (IsCiEnvironment())
            {
                Console.Error.WriteLine("--serve is manual-only and is disabled when CI-like environment variables are set.");
                return 1;
            }

            return RunServer(port, demo, demo3d);
        }

        if (argSet.Contains("--demo") || argSet.Contains("--demo3d"))
        {
            Console.Error.WriteLine("--demo and --demo3d require --serve.");
            return 1;
        }

        if (argSet.Contains("--all-ci-safe"))
        {
            if (argList.Count != 1)
            {
                Console.Error.WriteLine("--all-ci-safe cannot be combined with other validation options.");
                return 1;
            }

            return RunAllCiSafeValidations();
        }

        if (TryRunRegisteredValidation(argList, argSet, out var registeredValidationExitCode))
            return registeredValidationExitCode;

        if (argSet.Contains("--phase185-inspect-mcap"))
            return RunPhase185MessagePackMcapInspector(argList);

        if (argSet.Contains("--phase189-inspect-mcap"))
            return RunPhase189ComponentMessagePackMcapInspector(argList);

        if (argSet.Contains("--phase139b-remote-data-loader-server"))
            return RunPhase139BRemoteDataLoaderServer(argList);

        if (argSet.Contains("--phase97-health"))
            return RunPhase97Health(argList, argSet);

        var phase98SampleSendAllIdx = argList.IndexOf("--phase98-sample-send-all");
        if (phase98SampleSendAllIdx >= 0)
        {
            if (phase98SampleSendAllIdx + 2 >= argList.Count)
            {
                Console.Error.WriteLine("--phase98-sample-send-all requires host and port.");
                return 1;
            }

            if (!int.TryParse(argList[phase98SampleSendAllIdx + 2], out var port))
            {
                Console.Error.WriteLine("--phase98-sample-send-all port must be an integer.");
                return 1;
            }

            return RunPhase98SampleSendAll(argList[phase98SampleSendAllIdx + 1], port);
        }

        if (argSet.Contains("--phase98-live"))
            return RunPhase98Live(argList);

        if (argSet.Contains("--phase99-live"))
            return RunPhase99Live(argList);

        var phase94BridgeSendIdx = argList.IndexOf("--phase94-bridge-send");
        if (phase94BridgeSendIdx >= 0)
        {
            if (phase94BridgeSendIdx + 2 >= argList.Count)
            {
                Console.Error.WriteLine("--phase94-bridge-send requires host and port.");
                return 1;
            }

            if (!int.TryParse(argList[phase94BridgeSendIdx + 2], out var port))
            {
                Console.Error.WriteLine("--phase94-bridge-send port must be an integer.");
                return 1;
            }

            return RunPhase94BridgeSend(argList[phase94BridgeSendIdx + 1], port);
        }

        var phase91McapIdx = argList.IndexOf("--phase91-ros2-cdr-mcap");
        if (phase91McapIdx >= 0)
        {
            if (phase91McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase91-ros2-cdr-mcap requires an output path.");
                return 1;
            }

            return RunPhase91Ros2CdrMcap(argList[phase91McapIdx + 1]);
        }

        var phase92McapIdx = argList.IndexOf("--phase92-ros2-product-mcap");
        if (phase92McapIdx >= 0)
        {
            if (phase92McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase92-ros2-product-mcap requires an output path.");
                return 1;
            }

            return RunPhase92Ros2ProductMcap(argList[phase92McapIdx + 1]);
        }

        var phase93McapIdx = argList.IndexOf("--phase93-ros2-full-mcap");
        if (phase93McapIdx >= 0)
        {
            if (phase93McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase93-ros2-full-mcap requires an output path.");
                return 1;
            }

            return RunPhase93Ros2FullMcap(argList[phase93McapIdx + 1]);
        }

        var phase93InspectIdx = argList.IndexOf("--phase93-inspect-mcap");
        if (phase93InspectIdx >= 0)
        {
            if (phase93InspectIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase93-inspect-mcap requires an input path.");
                return 1;
            }

            return RunPhase93InspectMcap(argList[phase93InspectIdx + 1]);
        }

        var phase68SmokeIdx = argList.IndexOf("--phase68-indexed-reader-smoke");
        if (phase68SmokeIdx >= 0)
            return RunPhase68IndexedReaderSmoke(argList, phase68SmokeIdx);

        var phase44McapIdx = argList.IndexOf("--phase44-all-schemas-mcap");
        if (phase44McapIdx >= 0)
        {
            if (phase44McapIdx + 1 >= argList.Count)
            {
                Console.Error.WriteLine("--phase44-all-schemas-mcap requires an output path.");
                return 1;
            }

            try
            {
                Phase44Validation.GenerateAllSchemasMcap(argList[phase44McapIdx + 1]);
                Console.WriteLine($"Phase 44 all-schema smoke MCAP written: {argList[phase44McapIdx + 1]}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to generate Phase 44 all-schema smoke MCAP: {ex.Message}");
                return 1;
            }
        }
        var unknownFlag = argList.FirstOrDefault(IsUnknownDefaultFlag);
        if (unknownFlag != null)
        {
            Console.Error.WriteLine("Unknown validation flag: " + unknownFlag);
            return 1;
        }

        return RunTests(argSet.Contains("--local-evidence"));
    }

}
