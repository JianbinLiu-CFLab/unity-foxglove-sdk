// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/Process
// Purpose: Safe redirected process runner for Editor tooling.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
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

    /// <summary>Captures a redirected process result with binary standard output.</summary>
    public readonly struct FoxgloveEditorBinaryProcessResult
    {
        /// <summary>Creates a binary process result.</summary>
        public FoxgloveEditorBinaryProcessResult(
            int exitCode,
            bool timedOut,
            byte[] stdout,
            string stderr,
            bool outputReadFailed)
        {
            ExitCode = exitCode;
            TimedOut = timedOut;
            Stdout = stdout ?? Array.Empty<byte>();
            Stderr = stderr ?? "";
            OutputReadFailed = outputReadFailed;
        }

        public int ExitCode { get; }
        public bool TimedOut { get; }
        public byte[] Stdout { get; }
        public string Stderr { get; }
        public bool OutputReadFailed { get; }
    }

    public static class FoxgloveEditorProcessRunner
    {
        private static readonly MethodInfo KillProcessTreeMethod =
            typeof(Process).GetMethod("Kill", new[] { typeof(bool) });

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

        /// <summary>Runs a redirected process with bounded binary stdin, stdout and stderr.</summary>
        public static FoxgloveEditorBinaryProcessResult RunBytes(
            ProcessStartInfo startInfo,
            byte[] stdin,
            int maxStdoutBytes,
            int timeoutMs)
        {
            if (startInfo == null)
                throw new ArgumentNullException(nameof(startInfo));
            if (stdin == null)
                throw new ArgumentNullException(nameof(stdin));
            if (maxStdoutBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStdoutBytes));

            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (var process = new Process())
            {
                process.StartInfo = startInfo;
                if (!process.Start())
                    throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");

                var stdinTask = WriteInputAsync(process, stdin);
                var stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream, maxStdoutBytes);
                var stderrTask = ReadTextAsync(process.StandardError.BaseStream, maxStdoutBytes);
                var watch = Stopwatch.StartNew();
                while (!process.WaitForExit(50))
                {
                    if (stdoutTask.IsFaulted || stderrTask.IsFaulted)
                    {
                        TryKill(process);
                        process.WaitForExit(500);
                        WaitForStreamDrain(stdinTask, stdoutTask, stderrTask, 500);
                        return new FoxgloveEditorBinaryProcessResult(
                            -1,
                            false,
                            Array.Empty<byte>(),
                            GetCompletedOutput(stderrTask),
                            true);
                    }

                    if (watch.ElapsedMilliseconds < Math.Max(1, timeoutMs))
                        continue;

                    TryKill(process);
                    process.WaitForExit(500);
                    WaitForStreamDrain(stdinTask, stdoutTask, stderrTask, 500);
                    return new FoxgloveEditorBinaryProcessResult(
                        -1,
                        true,
                        GetCompletedBytes(stdoutTask),
                        GetCompletedOutput(stderrTask),
                        stdoutTask.IsFaulted || stdoutTask.IsCanceled || stderrTask.IsFaulted || stderrTask.IsCanceled);
                }

                process.WaitForExit();
                WaitForStreamDrain(stdinTask, stdoutTask, stderrTask, -1);
                return new FoxgloveEditorBinaryProcessResult(
                    process.ExitCode,
                    false,
                    GetCompletedBytes(stdoutTask),
                    GetCompletedOutput(stderrTask),
                    stdoutTask.IsFaulted || stdoutTask.IsCanceled || stderrTask.IsFaulted || stderrTask.IsCanceled);
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
            catch (Exception ex)
            {
                LogWarning("Failed to drain process output streams: " + ex.Message);
            }
        }

        /// <summary>Waits for binary process streams and stdin to finish.</summary>
        private static void WaitForStreamDrain(Task stdin, Task<byte[]> stdout, Task<string> stderr, int timeoutMs)
        {
            try
            {
                var tasks = new Task[] { stdin, stdout, stderr };
                if (timeoutMs < 0)
                    Task.WaitAll(tasks);
                else
                    Task.WaitAll(tasks, timeoutMs);
            }
            catch (Exception ex)
            {
                LogWarning("Failed to drain binary process streams: " + ex.Message);
            }
        }

        /// <summary>Writes bounded binary input and closes the child stdin pipe.</summary>
        private static async Task WriteInputAsync(Process process, byte[] input)
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(input, 0, input.Length).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                process.StandardInput.Close();
            }
        }

        /// <summary>Reads binary stdout while enforcing the output bound.</summary>
        private static async Task<byte[]> ReadBytesAsync(Stream source, int maxBytes)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                        return output.ToArray();
                    if (output.Length + read > maxBytes)
                        throw new InvalidDataException("Redirected process output exceeded its configured bound.");
                    output.Write(buffer, 0, read);
                }
            }
        }

        /// <summary>Reads redirected text while enforcing a byte bound.</summary>
        private static async Task<string> ReadTextAsync(Stream source, int maxBytes)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                        return Encoding.UTF8.GetString(output.ToArray());
                    if (output.Length + read > maxBytes)
                        throw new InvalidDataException("Redirected process error output exceeded its configured bound.");
                    output.Write(buffer, 0, read);
                }
            }
        }

        private static string GetCompletedOutput(Task<string> task)
        {
            try
            {
                return task.IsCompleted ? task.Result ?? "" : "";
            }
            catch (Exception ex)
            {
                LogWarning("Failed to read process output stream: " + ex.Message);
                return "";
            }
        }

        /// <summary>Returns completed binary output without propagating a process-drain error.</summary>
        private static byte[] GetCompletedBytes(Task<byte[]> task)
        {
            try
            {
                return task.IsCompleted && !task.IsFaulted && !task.IsCanceled
                    ? task.Result
                    : Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                LogWarning("Failed to read binary process output stream: " + ex.Message);
                return Array.Empty<byte>();
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    if (KillProcessTreeMethod != null)
                        KillProcessTreeMethod.Invoke(process, new object[] { true });
                    else
                        process.Kill();
                }
            }
            catch (Exception ex)
            {
                LogWarning("Failed to kill timed-out process: " + ex.Message);
            }
        }

        private static void LogWarning(string message)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning("[Foxglove] " + message);
#else
            Debug.WriteLine("[Foxglove] " + message);
#endif
        }
    }
}
