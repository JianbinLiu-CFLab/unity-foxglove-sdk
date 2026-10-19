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
    private static int RunPhase189ComponentMessagePackMcapInspector(IReadOnlyList<string> arguments)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var allowed = new HashSet<string>(new[]
            {
                "--phase189-inspect-mcap",
                "--expected-probe-report",
                "--expected-run",
                "--expected-head",
                "--expected-generation",
                "--output"
            }, StringComparer.Ordinal);
            for (var index = 0; index < arguments.Count; index += 2)
            {
                var flag = arguments[index];
                if (!allowed.Contains(flag))
                    throw new ArgumentException("Unexpected Phase189 inspector argument: " + flag);
                if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(flag + " requires exactly one path/value.");
                if (!values.TryAdd(flag, arguments[index + 1]))
                    throw new ArgumentException(flag + " may be supplied only once.");
            }
            foreach (var flag in allowed)
                if (!values.ContainsKey(flag))
                    throw new ArgumentException("Missing required Phase189 inspector option: " + flag);
            return ComponentMessagePackMcapInspector.RunCommand(
                values["--phase189-inspect-mcap"],
                values["--expected-probe-report"],
                values["--expected-run"],
                values["--expected-head"],
                values["--expected-generation"],
                values["--output"]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Phase189 Component MessagePack inspector argument failure: " + exception.Message);
            return 1;
        }
    }

    private static bool TryRunRegisteredValidation(List<string> argList, IReadOnlyCollection<string> argSet, out int exitCode)
    {
        if (argSet.Contains("--list-validations"))
        {
            if (argList.Count != 1 || !string.Equals(argList[0], "--list-validations", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--list-validations cannot be combined with other arguments.");
                exitCode = 1;
                return true;
            }

            foreach (var validation in PhaseValidationRegistry.All)
            {
                var flags = string.Join(", ", validation.AllFlags());
                if (string.IsNullOrEmpty(flags))
                    flags = "(default only)";
                Console.WriteLine(
                    $"{flags} [{validation.Category}] {ValidationEvidenceFormatter.Format(validation.Evidence)} {validation.Name}");
            }
            foreach (var tool in LegacyToolFlags)
                Console.WriteLine($"{tool.Flag} [{tool.Category}] {tool.Name}");

            exitCode = 0;
            return true;
        }

        var selected = PhaseValidationRegistry.FindAll(argList).ToList();
        if (selected.Count == 0)
        {
            exitCode = 0;
            return false;
        }

        if (selected.Count > 1)
        {
            Console.Error.WriteLine(
                "Multiple validation flags matched: " +
                string.Join(", ", selected.Select(item => item.Name + " (" + string.Join("/", item.AllFlags()) + ")")));
            exitCode = 1;
            return true;
        }

        // A registered selector authenticates the complete argv. Reject
        // unknown/positional tokens and duplicate aliases before invoking any
        // validation delegate; otherwise FindAll could silently drop intent.
        var acceptedFlags = new HashSet<string>(selected[0].AllFlags(), StringComparer.Ordinal)
        {
            "--local-evidence"
        };
        var suppliedFlags = argList.Where(argument => argument.StartsWith("--", StringComparison.Ordinal)).ToList();
        var invalidToken = argList.FirstOrDefault(argument => !acceptedFlags.Contains(argument));
        if (invalidToken != null)
        {
            Console.Error.WriteLine("Unexpected validation argument: " + invalidToken);
            exitCode = 1;
            return true;
        }

        var selectorOccurrences = suppliedFlags.Count(argument => acceptedFlags.Contains(argument) && argument != "--local-evidence");
        if (selectorOccurrences != 1)
        {
            Console.Error.WriteLine("Validation selector must be supplied exactly once.");
            exitCode = 1;
            return true;
        }

        exitCode = RunValidation(selected[0]);
        return true;
    }

    private static bool IsUnknownDefaultFlag(string arg)
    {
        return arg.StartsWith("--", StringComparison.Ordinal)
            && !string.Equals(arg, "--local-evidence", StringComparison.Ordinal)
            && !string.Equals(arg, "--all-ci-safe", StringComparison.Ordinal);
    }

    private static bool IsCiEnvironment()
    {
        foreach (var name in new[] { "CI", "GITHUB_ACTIONS", "TF_BUILD", "BUILD_BUILDID" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "0", StringComparison.Ordinal))
                return true;
        }

        return false;
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
}
