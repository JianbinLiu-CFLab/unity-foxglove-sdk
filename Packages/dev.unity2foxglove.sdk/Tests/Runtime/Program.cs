// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test runner entry point — discovers and executes all Phase validation tests.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Tests;

class Program
{
    /// <summary>
    /// Dispatches to test runner or interactive server mode based on
    /// command-line arguments. <c>--serve</c> starts a manual test
    /// server; default runs all validation phases.
    /// </summary>
    static int Main(string[] args)
    {
        var argList = args.ToList();

        if (argList.Contains("--serve"))
        {
            int port = 8765;
            var portIdx = argList.IndexOf("--port");
            if (portIdx >= 0 && portIdx + 1 < argList.Count)
                int.TryParse(argList[portIdx + 1], out port);

            var demo = argList.Contains("--demo");
            var demo3d = argList.Contains("--demo3d");
            return RunServer(port, demo, demo3d);
        }

        if (argList.Contains("--phase50"))
            return RunPhase50Only();

        if (argList.Contains("--phase16"))
            return RunPhase16Only();

        if (argList.Contains("--phase51"))
            return RunPhase51Only();

        if (argList.Contains("--phase53"))
            return RunPhase53Only();

        if (argList.Contains("--phase52"))
            return RunPhase52Only();

        if (argList.Contains("--phase54"))
            return RunPhase54Only();

        if (argList.Contains("--phase55"))
            return RunPhase55Only();

        if (argList.Contains("--phase56"))
            return RunPhase56Only();

        if (argList.Contains("--phase57"))
            return RunPhase57Only();

        if (argList.Contains("--phase65"))
            return RunPhase65Only();

        if (argList.Contains("--phase67"))
            return RunPhase67Only();

        if (argList.Contains("--phase68"))
            return RunPhase68Only();

        if (argList.Contains("--phase69"))
            return RunPhase69Only();

        if (argList.Contains("--phase70"))
            return RunPhase70Only();

        if (argList.Contains("--phase71"))
            return RunPhase71Only();

        if (argList.Contains("--phase72"))
            return RunPhase72Only();

        if (argList.Contains("--phase73"))
            return RunPhase73Only();

        if (argList.Contains("--phase74"))
            return RunPhase74Only();

        if (argList.Contains("--phase75"))
            return RunPhase75Only();

        if (argList.Contains("--phase76"))
            return RunPhase76Only();

        if (argList.Contains("--phase77"))
            return RunPhase77Only();

        if (argList.Contains("--phase80"))
            return RunPhase80Only();

        if (argList.Contains("--phase81"))
            return RunPhase81Only();

        if (argList.Contains("--phase82"))
            return RunPhase82Only();

        if (argList.Contains("--phase82-native-smoke"))
            return RunPhase82NativeSmoke();

        if (argList.Contains("--phase83"))
            return RunPhase83Only();

        if (argList.Contains("--phase84"))
            return RunPhase84Only();

        if (argList.Contains("--phase85"))
            return RunPhase85Only();

        if (argList.Contains("--phase86"))
            return RunPhase86Only();

        if (argList.Contains("--phase87"))
            return RunPhase87Only();

        if (argList.Contains("--phase88"))
            return RunPhase88Only();

        if (argList.Contains("--phase89"))
            return RunPhase89Only();

        if (argList.Contains("--phase90"))
            return RunPhase90Only();

        if (argList.Contains("--phase91"))
            return RunPhase91Only();

        if (argList.Contains("--phase92"))
            return RunPhase92Only();

        if (argList.Contains("--phase93"))
            return RunPhase93Only();

        if (argList.Contains("--phase94"))
            return RunPhase94Only();

        if (argList.Contains("--phase95"))
            return RunPhase95Only();

        if (argList.Contains("--phase96"))
            return RunPhase96Only();

        if (argList.Contains("--phase97"))
            return RunPhase97Only();

        if (argList.Contains("--phase97-health"))
            return RunPhase97Health(argList);

        if (argList.Contains("--phase98"))
            return RunPhase98Only();

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

        if (argList.Contains("--phase98-live"))
            return RunPhase98Live(argList);

        if (argList.Contains("--phase99"))
            return RunPhase99Only();

        if (argList.Contains("--phase99-live"))
            return RunPhase99Live(argList);

        if (argList.Contains("--phase100"))
            return RunPhase100Only();

        if (argList.Contains("--phase105"))
            return RunPhase105Only();

        if (argList.Contains("--phase106"))
            return RunPhase106Only();

        if (argList.Contains("--phase136"))
            return RunPhase136Only();

        if (argList.Contains("--phase137"))
            return RunPhase137Only();

        if (argList.Contains("--phase137b"))
            return RunPhase137BOnly();

        if (argList.Contains("--phase107"))
            return RunPhase107Only();

        if (argList.Contains("--phase108"))
            return RunPhase108Only();

        if (argList.Contains("--phase109"))
            return RunPhase109Only();

        if (argList.Contains("--phase110"))
            return RunPhase110Only();

        if (argList.Contains("--phase111f"))
            return RunPhase111FOnly();

        if (argList.Contains("--phase112"))
            return RunPhase112Only();

        if (argList.Contains("--phase112b"))
            return RunPhase112BOnly();

        if (argList.Contains("--phase113"))
            return RunPhase113Only();

        if (argList.Contains("--phase114"))
            return RunPhase114Only();

        if (argList.Contains("--phase115"))
            return RunPhase115Only();

        if (argList.Contains("--phase115b"))
            return RunPhase115BOnly();

        if (argList.Contains("--phase115c"))
            return RunPhase115COnly();

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

        if (argList.Contains("--phase13"))
            return RunPhase13Only();

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

        return RunTests();
    }

    private static int RunPhase16Only()
    {
        try
        {
            Phase16Validation.Validate();
            Console.WriteLine("\nPhase 16 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase50Only()
    {
        try
        {
            Phase50Validation.Validate();
            Console.WriteLine("\nPhase 50 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase51Only()
    {
        try
        {
            Phase51Validation.Validate();
            Console.WriteLine("\nPhase 51 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase53Only()
    {
        try
        {
            Phase53Validation.Validate();
            Console.WriteLine("\nPhase 53 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase52Only()
    {
        try
        {
            Phase52Validation.Validate();
            Console.WriteLine("\nPhase 52 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase54Only()
    {
        try
        {
            Phase54Validation.Validate();
            Console.WriteLine("\nPhase 54 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase55Only()
    {
        try
        {
            Phase55Validation.Validate();
            Console.WriteLine("\nPhase 55 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase56Only()
    {
        try
        {
            Phase56Validation.Validate();
            Console.WriteLine("\nPhase 56 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase57Only()
    {
        try
        {
            Phase57Validation.Validate();
            Console.WriteLine("\nPhase 57 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase65Only()
    {
        try
        {
            Phase65Validation.Validate();
            Console.WriteLine("\nPhase 65 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase67Only()
    {
        try
        {
            Phase67Validation.Validate();
            Console.WriteLine("\nPhase 67 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase68Only()
    {
        try
        {
            Phase68Validation.Validate();
            Console.WriteLine("\nPhase 68 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase68IndexedReaderSmoke(List<string> argList, int optionIndex)
    {
        if (optionIndex + 1 >= argList.Count)
        {
            Console.Error.WriteLine("--phase68-indexed-reader-smoke requires an MCAP path.");
            return 1;
        }

        try
        {
            var topics = CollectOptionValues(argList, "--phase68-topic");
            var maxMessages = ReadIntOption(argList, "--phase68-max-messages", 5);
            var minMessages = ReadIntOption(argList, "--phase68-min-messages", 1);

            Phase68Validation.ValidateExternalMcapSmoke(
                argList[optionIndex + 1],
                topics,
                maxMessages,
                minMessages);
            Console.WriteLine("\nPhase 68 indexed reader smoke passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static List<string> CollectOptionValues(List<string> argList, string option)
    {
        var values = new List<string>();
        for (var i = 0; i < argList.Count; i++)
        {
            if (argList[i] != option)
                continue;

            if (i + 1 >= argList.Count)
                throw new ArgumentException($"{option} requires a value.");

            values.Add(argList[i + 1]);
            i++;
        }

        return values;
    }

    private static int ReadIntOption(List<string> argList, string option, int defaultValue)
    {
        var idx = argList.IndexOf(option);
        if (idx < 0)
            return defaultValue;

        if (idx + 1 >= argList.Count)
            throw new ArgumentException($"{option} requires an integer value.");

        if (!int.TryParse(argList[idx + 1], out var value))
            throw new ArgumentException($"{option} requires an integer value.");

        return value;
    }

    private static string ReadStringOption(List<string> argList, string option, string defaultValue)
    {
        var idx = argList.IndexOf(option);
        if (idx < 0)
            return defaultValue;

        if (idx + 1 >= argList.Count)
            throw new ArgumentException($"{option} requires a value.");

        return argList[idx + 1];
    }

    private static int RunPhase69Only()
    {
        try
        {
            Phase69Validation.Validate();
            Console.WriteLine("\nPhase 69 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase70Only()
    {
        try
        {
            Phase70Validation.Validate();
            Console.WriteLine("\nPhase 70 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase71Only()
    {
        try
        {
            Phase71Validation.Validate();
            Console.WriteLine("\nPhase 71 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase72Only()
    {
        try
        {
            Phase72Validation.Validate();
            Console.WriteLine("\nPhase 72 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase73Only()
    {
        try
        {
            Phase73Validation.Validate();
            Console.WriteLine("\nPhase 73 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase74Only()
    {
        try
        {
            Phase74Validation.Validate();
            Console.WriteLine("\nPhase 74 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase75Only()
    {
        try
        {
            Phase75Validation.Validate();
            Console.WriteLine("\nPhase 75 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase76Only()
    {
        try
        {
            Phase76Validation.Validate();
            Console.WriteLine("\nPhase 76 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase77Only()
    {
        try
        {
            Phase77Validation.Validate();
            Console.WriteLine("\nPhase 77 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase80Only()
    {
        try
        {
            Phase80Validation.Validate();
            Console.WriteLine("\nPhase 80 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase81Only()
    {
        try
        {
            Phase81Validation.Validate();
            Console.WriteLine("\nPhase 81 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase82Only()
    {
        try
        {
            Phase82Validation.Validate();
            Console.WriteLine("\nPhase 82 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase82NativeSmoke()
    {
        try
        {
            Phase82Validation.RunNativeSmoke();
            Console.WriteLine("\nPhase 82 native smoke passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase83Only()
    {
        try
        {
            Phase83Validation.Validate();
            Console.WriteLine("\nPhase 83 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase84Only()
    {
        try
        {
            Phase84Validation.Validate();
            Console.WriteLine("\nPhase 84 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase85Only()
    {
        try
        {
            Phase85Validation.Validate();
            Console.WriteLine("\nPhase 85 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase86Only()
    {
        try
        {
            Phase86Validation.Validate();
            Console.WriteLine("\nPhase 86 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase87Only()
    {
        try
        {
            Phase87Validation.Validate();
            Console.WriteLine("\nPhase 87 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase88Only()
    {
        try
        {
            Phase88Validation.Validate();
            Console.WriteLine("\nPhase 88 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase89Only()
    {
        try
        {
            Phase89Validation.Validate();
            Console.WriteLine("\nPhase 89 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase90Only()
    {
        try
        {
            Phase90Validation.Validate();
            Console.WriteLine("\nPhase 90 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase91Only()
    {
        try
        {
            Phase91Validation.Validate();
            Console.WriteLine("\nPhase 91 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase91Ros2CdrMcap(string outputPath)
    {
        try
        {
            Phase91Validation.GenerateRos2CdrMcap(outputPath);
            Console.WriteLine($"Phase 91 ROS2 CDR MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase92Only()
    {
        try
        {
            Phase92Validation.Validate();
            Console.WriteLine("\nPhase 92 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase92Ros2ProductMcap(string outputPath)
    {
        try
        {
            Phase92Validation.GenerateRos2ProductMcap(outputPath);
            Console.WriteLine($"Phase 92 ROS2 product MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase93Only()
    {
        try
        {
            Phase93Validation.Validate();
            Console.WriteLine("\nPhase 93 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase93Ros2FullMcap(string outputPath)
    {
        try
        {
            Phase93Validation.GenerateRos2FullSchemaMcap(outputPath);
            Console.WriteLine($"Phase 93 ROS2 full-schema MCAP written: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase93InspectMcap(string inputPath)
    {
        try
        {
            Phase93Validation.InspectRos2FullSchemaMcap(inputPath);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase94Only()
    {
        try
        {
            Phase94Validation.Validate();
            Console.WriteLine("\nPhase 94 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase94BridgeSend(string host, int port)
    {
        try
        {
            Phase94Validation.RunBridgeSendSmoke(host, port);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase95Only()
    {
        try
        {
            Phase95Validation.Validate();
            Console.WriteLine("\nPhase 95 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase96Only()
    {
        try
        {
            Phase96Validation.Validate();
            Console.WriteLine("\nPhase 96 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase97Only()
    {
        try
        {
            Phase97Validation.Validate();
            Console.WriteLine("\nPhase 97 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase97Health(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase97-health requires --json <path>.");
                return 1;
            }

            var liveMode = argList.Contains("--phase97-live")
                || string.Equals(
                    Environment.GetEnvironmentVariable("UNITY2FOXGLOVE_PHASE97_LIVE"),
                    "1",
                    StringComparison.Ordinal);
            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var report = Phase97Validation.GenerateHealthReport(jsonPath, liveMode, ros2Path, host, port);

            Console.WriteLine($"Phase 97 health report written: {jsonPath}");
            Console.WriteLine($"Summary: {report.Summary}");
            if (liveMode && report.Summary != Unity.FoxgloveSDK.Ros2Bridge.Ros2BridgeHealthSummary.Ready)
                return 1;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase98Only()
    {
        try
        {
            Phase98Validation.Validate();
            Console.WriteLine("\nPhase 98 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase98SampleSendAll(string host, int port)
    {
        try
        {
            var summary = Phase98Validation.SendAllSchemaSamples(host, port);
            Console.WriteLine($"[phase98] sent frames={summary.SentFrames} totalWireBytes={summary.TotalWireBytes}");
            Console.WriteLine($"[phase98] firstSchema={summary.FirstSchema}");
            Console.WriteLine($"[phase98] lastSchema={summary.LastSchema}");
            Console.WriteLine("[phase98] PASS all-schema sample sender");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase98Live(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase98-live requires --json <path>.");
                return 1;
            }

            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var evidence = Phase98Validation.GenerateLiveEvidence(jsonPath, host, port, ros2Path);

            Console.WriteLine($"Phase 98 live evidence written: {jsonPath}");
            Console.WriteLine($"Health: {evidence.HealthSummary}");
            Console.WriteLine($"Product topics: {evidence.ProductTopics?.Length ?? 0}");
            Console.WriteLine($"All-schema frames: {evidence.AllSchema?.SentFrames ?? 0}");
            return string.Equals(evidence.HealthSummary, "Ready", StringComparison.Ordinal) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase99Only()
    {
        try
        {
            Phase99Validation.Validate();
            Console.WriteLine("\nPhase 99 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase99Live(List<string> argList)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase99-live requires --json <path>.");
                return 1;
            }

            var evidenceDir = ReadStringOption(argList, "--evidence-dir", "");
            var ros2Path = ReadStringOption(argList, "--ros2", "");
            var host = ReadStringOption(argList, "--host", "127.0.0.1");
            var port = ReadIntOption(argList, "--port", 8767);
            var report = Phase99Validation.GenerateLiveReport(jsonPath, evidenceDir, host, port, ros2Path);

            Console.WriteLine($"Phase 99 release gate report written: {jsonPath}");
            Console.WriteLine($"Verdict: {report.Verdict}");
            Console.WriteLine($"Evidence items: {report.Evidence?.Count ?? 0}");
            return report.Verdict == Phase99Verdict.Blocked ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase100Only()
    {
        try
        {
            Phase100Validation.Validate();
            Console.WriteLine("\nPhase 100 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase105Only()
    {
        try
        {
            Phase105Validation.Validate();
            Console.WriteLine("\nPhase 105 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase106Only()
    {
        try
        {
            Phase106Validation.Validate();
            Console.WriteLine("\nPhase 106 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase136Only()
    {
        try
        {
            Phase136Validation.Validate();
            Console.WriteLine("\nPhase 136 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase137Only()
    {
        try
        {
            Phase137Validation.Validate();
            Console.WriteLine("\nPhase 137 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase137BOnly()
    {
        try
        {
            Phase137BValidation.Validate();
            Console.WriteLine("\nPhase 137B checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase107Only()
    {
        try
        {
            Phase107Validation.Validate();
            Console.WriteLine("\nPhase 107 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase108Only()
    {
        try
        {
            Phase108Validation.Validate();
            Console.WriteLine("\nPhase 108 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase109Only()
    {
        try
        {
            Phase109Validation.Validate();
            Console.WriteLine("\nPhase 109 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase110Only()
    {
        try
        {
            Phase110Validation.Validate();
            Console.WriteLine("\nPhase 110 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase111FOnly()
    {
        try
        {
            Phase111FValidation.Validate();
            Console.WriteLine("\nPhase 111F checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase112Only()
    {
        try
        {
            Phase112Validation.Validate();
            Console.WriteLine("\nPhase 112 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase112BOnly()
    {
        try
        {
            Phase112BValidation.Validate();
            Console.WriteLine("\nPhase 112B checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 112B validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase113Only()
    {
        try
        {
            Phase113Validation.Validate();
            Console.WriteLine("\nPhase 113 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 113 validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase114Only()
    {
        try
        {
            Phase114Validation.Validate();
            Console.WriteLine("\nPhase 114 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 114 validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase115Only()
    {
        try
        {
            Phase115Validation.Validate();
            Console.WriteLine("\nPhase 115 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 115 validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase115BOnly()
    {
        try
        {
            Phase115BValidation.Validate();
            Console.WriteLine("\nPhase 115B checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 115B validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase115COnly()
    {
        try
        {
            Phase115CValidation.Validate();
            Console.WriteLine("\nPhase 115C checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Phase 115C validation failed: " + ex.Message);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunPhase13Only()
    {
        try
        {
            Phase13Validation.Validate();
            Console.WriteLine("\nPhase 13 checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Runs all Phase validation classes sequentially and returns 0 on
    /// success or 1 on the first failure.
    /// </summary>
    static int RunTests()
    {
        Console.WriteLine("=== FoxgloveSDK Phase 0 + Phase 1 Validation ===\n");

        try
        {
            SkeletonValidation.Validate();
            Console.WriteLine();
            Phase1Validation.Validate();
            Console.WriteLine();
            Phase2Validation.Validate();
            Console.WriteLine();
            Phase3Validation.Validate();
            Console.WriteLine();
            Phase4Validation.Validate();
            Console.WriteLine();
            Phase5Validation.Validate();
            Console.WriteLine();
            Phase6Validation.Validate();
            Console.WriteLine();
            Phase7Validation.Validate();
            Console.WriteLine();
            Phase8Validation.Validate();
            Console.WriteLine();
            Phase9Validation.Validate();
            Console.WriteLine();
            Phase10Validation.Validate();
            Console.WriteLine();
            Phase11Validation.Validate();
            Console.WriteLine();
            Phase12Validation.Validate();
            Console.WriteLine();
            Phase13Validation.Validate();
            Console.WriteLine();
            Phase14Validation.Validate();
            Console.WriteLine();
            Phase16Validation.Validate();
            Console.WriteLine();
            Phase17Validation.Validate();
            Console.WriteLine();
            Phase24DValidation.Validate();
            Console.WriteLine();
            Phase28Validation.Validate();
            Console.WriteLine();
            Phase31Validation.Validate();
            Console.WriteLine();
            Phase32Validation.Run();
            Console.WriteLine();
            Phase33Validation.Validate();
            Console.WriteLine();
            Phase34Validation.Validate();
            Console.WriteLine();
            Phase36Validation.Validate();
            Console.WriteLine();
            Phase37Validation.Validate();
            Console.WriteLine();
            Phase40Validation.Validate();
            Console.WriteLine();
            Phase41Validation.Validate();
            Console.WriteLine();
            Phase44Validation.Validate();
            Console.WriteLine();
            Phase48Validation.Validate();
            Console.WriteLine();
            Phase49Validation.Validate();
            Console.WriteLine();
            Phase50Validation.Validate();
            Console.WriteLine();
            Phase51Validation.Validate();
            Console.WriteLine();
            Phase52Validation.Validate();
            Console.WriteLine();
            Phase53Validation.Validate();
            Console.WriteLine();
            Phase54Validation.Validate();
            Console.WriteLine();
            Phase55Validation.Validate();
            Console.WriteLine();
            Phase56Validation.Validate();
            Console.WriteLine();
            Phase57Validation.Validate();
            Console.WriteLine();
            Phase65Validation.Validate();
            Console.WriteLine();
            Phase67Validation.Validate();
            Console.WriteLine();
            Phase68Validation.Validate();
            Console.WriteLine();
            Phase69Validation.Validate();
            Console.WriteLine();
            Phase70Validation.Validate();
            Console.WriteLine();
            Phase71Validation.Validate();
            Console.WriteLine();
            Phase72Validation.Validate();
            Console.WriteLine();
            Phase73Validation.Validate();
            Console.WriteLine();
            Phase74Validation.Validate();
            Console.WriteLine();
            Phase75Validation.Validate();
            Console.WriteLine();
            Phase76Validation.Validate();
            Console.WriteLine();
            Phase77Validation.Validate();
            Console.WriteLine();
            Phase80Validation.Validate();
            Console.WriteLine();
            Phase81Validation.Validate();
            Console.WriteLine();
            Phase82Validation.Validate();
            Console.WriteLine();
            Phase83Validation.Validate();
            Console.WriteLine();
            Phase84Validation.Validate();
            Console.WriteLine();
            Phase85Validation.Validate();
            Console.WriteLine();
            Phase86Validation.Validate();
            Console.WriteLine();
            Phase87Validation.Validate();
            Console.WriteLine();
            Phase88Validation.Validate();
            Console.WriteLine();
            Phase89Validation.Validate();
            Console.WriteLine();
            Phase90Validation.Validate();
            Console.WriteLine();
            Phase91Validation.Validate();
            Console.WriteLine();
            Phase92Validation.Validate();
            Console.WriteLine();
            Phase93Validation.Validate();
            Console.WriteLine();
            Phase94Validation.Validate();
            Console.WriteLine();
            Phase95Validation.Validate();
            Console.WriteLine();
            Phase96Validation.Validate();
            Console.WriteLine();
            Phase97Validation.Validate();
            Console.WriteLine();
            Phase98Validation.Validate();
            Console.WriteLine();
            Phase99Validation.Validate();
            Console.WriteLine();
            Phase100Validation.Validate();
            Console.WriteLine();
            Phase105Validation.Validate();
            Console.WriteLine();
            Phase106Validation.Validate();
            Console.WriteLine();
            Phase136Validation.Validate();
            Console.WriteLine();
            Phase137Validation.Validate();
            Console.WriteLine();
            Phase137BValidation.Validate();
            Console.WriteLine();
            Phase107Validation.Validate();
            Console.WriteLine();
            Phase108Validation.Validate();
            Console.WriteLine();
            Phase109Validation.Validate();
            Console.WriteLine();
            Phase110Validation.Validate();
            Console.WriteLine();
            Phase111FValidation.Validate();
            Console.WriteLine();
            Phase112Validation.Validate();
            Console.WriteLine();
            Phase112BValidation.Validate();
            Console.WriteLine();
            Phase113Validation.Validate();
            Console.WriteLine();
            Phase114Validation.Validate();
            Console.WriteLine();
            Phase115Validation.Validate();
            Console.WriteLine();
            Phase115BValidation.Validate();
            Console.WriteLine();
            Phase115CValidation.Validate();

            Console.WriteLine("\nAll checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Starts a long-running Foxglove WebSocket server for manual
    /// testing. <c>demo</c> publishes a heartbeat; <c>demo3d</c>
    /// publishes FrameTransform and SceneUpdate.
    /// </summary>
    static int RunServer(int port, bool demo, bool demo3d)
    {
        Console.WriteLine($"=== FoxgloveSDK Manual Server Mode ===");
        Console.WriteLine($"Starting on ws://127.0.0.1:{port}");

        var runtime = new Unity.FoxgloveSDK.Core.FoxgloveRuntime();
        runtime.Start("Unity Foxglove SDK", "127.0.0.1", port);

        Console.WriteLine($"Server running. SessionId: {runtime.Session.SessionId}");
        Console.WriteLine("Open Foxglove → Open connection → ws://127.0.0.1:{0}", port);

        Timer heartbeat = null;
        if (demo)
        {
            var ch = new AdvertiseChannel
            {
                Id = 1,
                Topic = "/debug/heartbeat",
                Encoding = "json",
                SchemaName = "",
                Schema = ""
            };
            runtime.RegisterChannel(ch);
            Console.WriteLine("Demo: registered /debug/heartbeat (1 Hz)");

            ulong seq = 0;
            heartbeat = new Timer(_ =>
            {
                seq++;
                var payload = new
                {
                    seq,
                    unixTimeNs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL,
                    message = "hello foxglove"
                };
                var json = JsonConvert.SerializeObject(payload);
                runtime.Publish(1, Encoding.UTF8.GetBytes(json));
            }, null, 1000, 1000);
        }

        Timer sceneTimer = null;
        if (demo3d)
        {
            runtime.RegisterSchemaChannel(1, "/tf", "foxglove.FrameTransform");
            runtime.RegisterSchemaChannel(2, "/scene", "foxglove.SceneUpdate");
            Console.WriteLine("Demo3D: registered /tf (FrameTransform) and /scene (SceneUpdate) at 1 Hz");

            ulong tfSeq = 0;
            sceneTimer = new Timer(_ =>
            {
                tfSeq++;
                var unixNs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000UL;
                var sec = unixNs / 1_000_000_000UL;
                var nsec = (uint)(unixNs % 1_000_000_000UL);

                var tf = new FrameTransformMessage
                {
                    Timestamp = new FoxgloveTime { Sec = sec, Nsec = nsec },
                    ParentFrameId = "unity_world",
                    ChildFrameId = "phase3_cube_frame",
                    Translation = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                    Rotation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                };
                runtime.PublishJson(1, tf, unixNs);

                var scene = new SceneUpdateMessage
                {
                    Entities = new System.Collections.Generic.List<SceneEntity>
                    {
                        new SceneEntity
                        {
                            Id = "phase3_cube",
                            FrameId = "phase3_cube_frame",
                            Timestamp = new FoxgloveTime { Sec = sec, Nsec = nsec },
                            Lifetime = new FoxgloveDuration(),
                            Cubes = new System.Collections.Generic.List<CubePrimitive>
                            {
                                new CubePrimitive
                                {
                                    Pose = new FoxglovePose
                                    {
                                        Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                                        Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                                    },
                                    Size = new FoxgloveVector3 { X = 1, Y = 1, Z = 1 },
                                    Color = new FoxgloveColor { R = 0, G = 1, B = 0, A = 1 }
                                }
                            }
                        }
                    }
                };
                runtime.PublishJson(2, scene, unixNs);
            }, null, 1000, 1000);
        }

        Console.WriteLine("Expected: connection succeeds, no topics listed.");
        if (demo)
            Console.WriteLine("Demo: /debug/heartbeat visible, subscribe to see messages.");
        if (demo3d)
        {
            Console.WriteLine("Demo3D: /tf and /scene visible.");
            Console.WriteLine("  Foxglove → 3D panel → select /scene → green cube at origin.");
        }
        Console.WriteLine("Press Ctrl+C to stop...");

        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            done.Set();
        };

        done.Wait();
        Console.WriteLine("\nStopping...");
        heartbeat?.Dispose();
        sceneTimer?.Dispose();
        runtime.Dispose();
        Console.WriteLine("Server stopped.");
        return 0;
    }
}
