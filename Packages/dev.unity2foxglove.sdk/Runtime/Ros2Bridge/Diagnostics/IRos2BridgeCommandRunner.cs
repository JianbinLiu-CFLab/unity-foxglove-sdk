// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Ros2Bridge/Diagnostics
// Purpose: Command runner abstraction for ROS2 Bridge health diagnostics.

using System;
using System.Diagnostics;
using System.Text;

namespace Unity.FoxgloveSDK.Ros2Bridge
{
    /// <summary>Runs ROS2 CLI commands for health diagnostics without coupling callers to Process.</summary>
    public interface IRos2BridgeCommandRunner
    {
        Ros2BridgeCommandResult Run(string executable, string arguments, int timeoutMs);
    }

    /// <summary>Result of one ROS2 CLI command, including timeout and launch-error state.</summary>
    public sealed class Ros2BridgeCommandResult
    {
        public Ros2BridgeCommandResult(
            int exitCode,
            string stdout,
            string stderr,
            bool timedOut,
            string error,
            long durationMs)
        {
            ExitCode = exitCode;
            Stdout = stdout ?? string.Empty;
            Stderr = stderr ?? string.Empty;
            TimedOut = timedOut;
            Error = error ?? string.Empty;
            DurationMs = durationMs < 0 ? 0 : durationMs;
        }

        public int ExitCode { get; }
        public string Stdout { get; }
        public string Stderr { get; }
        public bool TimedOut { get; }
        public string Error { get; }
        public long DurationMs { get; }
        public bool Succeeded => !TimedOut && ExitCode == 0 && string.IsNullOrEmpty(Error);
        public string FailureMessage => TimedOut
            ? "Command timed out."
            : !string.IsNullOrEmpty(Error)
                ? Error
                : !string.IsNullOrWhiteSpace(Stderr)
                    ? Stderr.Trim()
                    : $"Command exited with code {ExitCode}.";
    }

    /// <summary>Process-based command runner used by the Inspector health check.</summary>
    public sealed class ProcessRos2BridgeCommandRunner : IRos2BridgeCommandRunner
    {
        public Ros2BridgeCommandResult Run(string executable, string arguments, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            if (string.IsNullOrWhiteSpace(executable))
            {
                return new Ros2BridgeCommandResult(
                    -1,
                    string.Empty,
                    string.Empty,
                    timedOut: false,
                    error: "ros2 executable path is empty.",
                    durationMs: 0);
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(Math.Max(1, timeoutMs)))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // The process may have exited between WaitForExit and Kill.
                    }

                    stopwatch.Stop();
                    return new Ros2BridgeCommandResult(
                        -1,
                        stdout.ToString(),
                        stderr.ToString(),
                        timedOut: true,
                        error: string.Empty,
                        durationMs: stopwatch.ElapsedMilliseconds);
                }

                process.WaitForExit();
                stopwatch.Stop();
                return new Ros2BridgeCommandResult(
                    process.ExitCode,
                    stdout.ToString(),
                    stderr.ToString(),
                    timedOut: false,
                    error: string.Empty,
                    durationMs: stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new Ros2BridgeCommandResult(
                    -1,
                    stdout.ToString(),
                    stderr.ToString(),
                    timedOut: false,
                    error: ex.Message,
                    durationMs: stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
