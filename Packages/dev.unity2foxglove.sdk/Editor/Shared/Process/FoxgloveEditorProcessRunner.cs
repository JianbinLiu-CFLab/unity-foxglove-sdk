// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/Process
// Purpose: Safe redirected process runner for Editor tooling.

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.Editor
{
    public readonly struct FoxgloveEditorProcessResult
    {
        public FoxgloveEditorProcessResult(int exitCode, bool timedOut, string stdout, string stderr)
        {
            ExitCode = exitCode;
            TimedOut = timedOut;
            Stdout = stdout ?? "";
            Stderr = stderr ?? "";
        }

        public int ExitCode { get; }
        public bool TimedOut { get; }
        public string Stdout { get; }
        public string Stderr { get; }
    }

    public static class FoxgloveEditorProcessRunner
    {
        public static FoxgloveEditorProcessResult Run(ProcessStartInfo startInfo, int timeoutMs)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));

            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                if (!process.Start())
                    throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(Math.Max(1, timeoutMs)))
                {
                    TryKill(process);
                    process.WaitForExit(500);
                    WaitForStreamDrain(stdout, stderr, 500);
                    return new FoxgloveEditorProcessResult(
                        -1,
                        true,
                        GetCompletedOutput(stdout),
                        GetCompletedOutput(stderr));
                }

                process.WaitForExit();
                WaitForStreamDrain(stdout, stderr, -1);
                return new FoxgloveEditorProcessResult(
                    process.ExitCode,
                    false,
                    GetCompletedOutput(stdout),
                    GetCompletedOutput(stderr));
            }
        }

        private static void WaitForStreamDrain(Task<string> stdout, Task<string> stderr, int timeoutMs)
        {
            try
            {
                if (timeoutMs < 0)
                    Task.WaitAll(stdout, stderr);
                else
                    Task.WaitAll(new Task[] { stdout, stderr }, timeoutMs);
            }
            catch
            {
            }
        }

        private static string GetCompletedOutput(Task<string> task)
        {
            try
            {
                return task.IsCompleted ? task.Result ?? "" : "";
            }
            catch
            {
                return "";
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }
    }
}
