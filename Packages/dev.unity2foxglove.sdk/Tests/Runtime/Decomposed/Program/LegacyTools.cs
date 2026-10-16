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
    static int RunTests(bool includeLocalEvidence)
    {
        Console.WriteLine(includeLocalEvidence
            ? "=== FoxgloveSDK CI-safe + local evidence validation ===\n"
            : "=== FoxgloveSDK CI-safe validation ===\n");

        var validations = PhaseValidationRegistry.DefaultValidations(includeLocalEvidence).ToList();
        var failures = CollectValidationFailures(
            validations.Select(validation =>
                (validation.Name, new Func<int>(() => RunValidation(validation)))));
        var passed = validations.Count - failures.Count;
        Console.WriteLine(
            $"\nDefault validation coverage: passed={passed} failed={failures.Count} total={validations.Count}");
        if (failures.Count == 0)
        {
            Console.WriteLine("All checks passed.");
            return 0;
        }

        Console.Error.WriteLine("Failed default validations: " + string.Join("; ", failures));
        return 1;
    }

    /// <summary>
    /// Executes every supplied validation and returns the names of all entries
    /// that report a non-zero result. Kept as a small seam so the default-runner
    /// no-early-return contract can be verified without launching the full lane.
    /// </summary>
}

