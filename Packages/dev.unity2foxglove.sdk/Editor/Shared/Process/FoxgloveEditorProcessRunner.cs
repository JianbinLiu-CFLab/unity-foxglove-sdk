// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/Process
// Purpose: Safe redirected process runner for Editor tooling.

using System;
using System.Diagnostics;
using System.Text;

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

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            var stdoutLock = new object();
            var stderrLock = new object();

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        return;

                    lock (stdoutLock)
                        stdout.AppendLine(args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (args.Data == null)
                        return;

                    lock (stderrLock)
                        stderr.AppendLine(args.Data);
                };

                if (!process.Start())
                    throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(Math.Max(1, timeoutMs)))
                {
                    TryKill(process);
                    process.WaitForExit(500);
                    return new FoxgloveEditorProcessResult(-1, true, Snapshot(stdout, stdoutLock), Snapshot(stderr, stderrLock));
                }

                // WaitForExit() without timeout flushes pending async output events after
                // the process has already exited.
                process.WaitForExit();
                return new FoxgloveEditorProcessResult(
                    process.ExitCode,
                    false,
                    Snapshot(stdout, stdoutLock),
                    Snapshot(stderr, stderrLock));
            }
        }

        private static string Snapshot(StringBuilder builder, object sync)
        {
            lock (sync)
                return builder.ToString();
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
