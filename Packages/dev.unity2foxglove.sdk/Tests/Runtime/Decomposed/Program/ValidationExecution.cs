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
    private static int RunPhase185MessagePackMcapInspector(IReadOnlyList<string> arguments)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var allowed = new HashSet<string>(
                new[]
                {
                    "--phase185-inspect-mcap",
                    "--expected-probe-report",
                    "--output"
                },
                StringComparer.Ordinal);

            for (var index = 0; index < arguments.Count; index += 2)
            {
                var flag = arguments[index];
                if (!allowed.Contains(flag))
                    throw new ArgumentException("Unexpected Phase185 inspector argument: " + flag);
                if (index + 1 >= arguments.Count
                    || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException(flag + " requires exactly one path value.");
                }
                if (!values.TryAdd(flag, arguments[index + 1]))
                    throw new ArgumentException(flag + " may be supplied only once.");
            }

            foreach (var flag in allowed)
                if (!values.ContainsKey(flag))
                    throw new ArgumentException("Missing required Phase185 inspector option: " + flag);

            return FoxRunMessagePackMcapInspector.RunCommand(
                values["--phase185-inspect-mcap"],
                values["--expected-probe-report"],
                values["--output"]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Phase185 MessagePack MCAP inspector argument failure: " + exception.Message);
            return 1;
        }
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

    private static int RunValidation(PhaseValidationCase validation)
    {
        var methodBody = validation.Run?.Method.GetMethodBody()?.GetILAsByteArray();
        if (methodBody == null || methodBody.Length <= 1)
        {
            Console.Error.WriteLine($"[FAIL] {validation.Name}: validation delegate has no executable body.");
            return 1;
        }

        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalOutNewLine = originalOut.NewLine;
        var originalErrorNewLine = originalError.NewLine;
        var classifiedOut = TextWriter.Synchronized(
            new ValidationEvidenceTextWriter(originalOut, validation.Evidence));
        var classifiedError = TextWriter.Synchronized(
            new ValidationEvidenceTextWriter(originalError, validation.Evidence));
        var exitCode = 1;
        Exception primaryFailure = null;
        Exception cleanupFailure = null;
        try
        {
            Console.SetOut(classifiedOut);
            Console.SetError(classifiedError);
            validation.Run();
            Console.WriteLine($"\n{validation.Name} checks passed.");
            exitCode = 0;
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
            try
            {
                originalError.WriteLine($"\n[FAIL] {validation.Name}: {ex.Message}");
            }
            catch (Exception writeFailure)
            {
                cleanupFailure = writeFailure;
            }
        }
        finally
        {
            TryCleanup(() => classifiedOut.Flush(), ref cleanupFailure);
            TryCleanup(() => classifiedError.Flush(), ref cleanupFailure);
            TryCleanup(() => originalOut.NewLine = originalOutNewLine, ref cleanupFailure);
            TryCleanup(() => originalError.NewLine = originalErrorNewLine, ref cleanupFailure);
            TryCleanup(() => Console.SetOut(originalOut), ref cleanupFailure);
            TryCleanup(() => Console.SetError(originalError), ref cleanupFailure);
            if (cleanupFailure != null && primaryFailure == null)
            {
                exitCode = 1;
                try
                {
                    originalError.WriteLine($"[FAIL] {validation.Name}: console cleanup failed: {cleanupFailure.Message}");
                }
                catch
                {
                    // The original error stream itself is unavailable; retain the non-zero status.
                }
            }
        }

        return exitCode;
    }

    private static void TryCleanup(Action cleanup, ref Exception firstFailure)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex)
        {
            firstFailure ??= ex;
        }
    }

}

