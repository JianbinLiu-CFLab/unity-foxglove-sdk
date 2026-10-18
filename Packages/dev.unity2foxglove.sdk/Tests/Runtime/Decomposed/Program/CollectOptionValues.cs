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
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
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
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
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
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
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
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
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
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
            return 1;
        }
    }

    private static int RunPhase97Health(List<string> argList, IReadOnlyCollection<string> argSet)
    {
        try
        {
            var jsonPath = ReadStringOption(argList, "--json", "");
            if (string.IsNullOrWhiteSpace(jsonPath))
            {
                Console.Error.WriteLine("--phase97-health requires --json <path>.");
                return 1;
            }

            var liveMode = argSet.Contains("--phase97-live")
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
            if (liveMode && report.Summary != Unity2Foxglove.Ros2Bridge.Ros2BridgeHealthSummary.Ready)
                return 1;
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[FAIL] {ex.Message}");
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

    /// <summary>
    /// Runs all Phase validation classes sequentially and returns 0 only when
    /// every selected validation succeeds. Every validation is executed so a
    /// failure cannot hide the remaining default-lane results.
    /// </summary>

    internal static List<string> CollectValidationFailures(
        IEnumerable<(string Name, Func<int> Run)> validations)
    {
        if (validations == null)
            throw new ArgumentNullException(nameof(validations));

        var failures = new List<string>();
        foreach (var validation in validations)
        {
            if (validation.Run() != 0)
                failures.Add(validation.Name);
        }

        return failures;
    }

    /// <summary>
    /// Runs every registry entry classified as CI-safe and collects all
    /// failures before returning. The fast default sweep intentionally omits
    /// some expensive or historical selectors; this explicit gate makes that
    /// coverage visible and prevents the first failure from hiding later ones.
    /// </summary>

    private static int RunAllCiSafeValidations()
    {
        var validations = PhaseValidationRegistry.CiSafeValidations().ToList();
        var failures = new List<string>();

        Console.WriteLine("=== FoxgloveSDK all CI-safe validation registry ===\n");
        foreach (var validation in validations)
        {
            if (RunValidation(validation) != 0)
                failures.Add(validation.Name);
        }

        var passed = validations.Count - failures.Count;
        Console.WriteLine(
            $"\nCI-safe registry coverage: passed={passed} failed={failures.Count} total={validations.Count}");
        if (failures.Count == 0)
        {
            Console.WriteLine("All CI-safe validations passed.");
            return 0;
        }

        Console.Error.WriteLine("Failed CI-safe validations: " + string.Join("; ", failures));
        return 1;
    }

    /// <summary>Runs the manual Phase139B loopback server used by browser and Python acceptance probes.</summary>
    private static string ValueAfter(List<string> args, string flag)
    {
        var idx = args.IndexOf(flag);
        return idx >= 0 && idx + 1 < args.Count
            ? args[idx + 1]
            : null;
    }

    /// <summary>
    /// Starts a long-running Foxglove WebSocket server for manual
    /// testing. <c>demo</c> publishes a heartbeat; <c>demo3d</c>
    /// publishes FrameTransform and SceneUpdate.
    /// </summary>
}
