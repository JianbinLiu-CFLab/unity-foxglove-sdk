// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Configured-path FFmpeg executable validation.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Foxglove.Schemas.Video
{
    public enum FfmpegExecutableStatus
    {
        NotChecked,
        Found,
        Missing,
        Invalid
    }

    public readonly struct FfmpegExecutableCheckResult
    {
        public FfmpegExecutableCheckResult(
            FfmpegExecutableStatus status,
            string executablePath,
            string versionLine,
            string errorMessage)
        {
            Status = status;
            ExecutablePath = executablePath ?? "";
            VersionLine = versionLine ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        public FfmpegExecutableStatus Status { get; }
        public string ExecutablePath { get; }
        public string VersionLine { get; }
        public string ErrorMessage { get; }
    }

    public static class FfmpegExecutableCheck
    {
        public static FfmpegExecutableCheckResult Check(string configuredPath, int timeoutMs = 2000)
        {
            var executable = FfmpegExecutableResolver.ResolveExecutablePath(configuredPath);
            if (Path.IsPathRooted(executable) && !File.Exists(executable))
            {
                return new FfmpegExecutableCheckResult(
                    FfmpegExecutableStatus.Missing,
                    executable,
                    "",
                    "Configured FFmpeg executable was not found.");
            }

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    if (!process.Start())
                    {
                        return new FfmpegExecutableCheckResult(
                            FfmpegExecutableStatus.Invalid,
                            executable,
                            "",
                            "FFmpeg process did not start.");
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(Math.Max(100, timeoutMs)))
                    {
                        TryKill(process);
                        WaitForReaderTasks(stdoutTask, stderrTask, 100);
                        return new FfmpegExecutableCheckResult(
                            FfmpegExecutableStatus.Invalid,
                            executable,
                            "",
                            "FFmpeg check timed out.");
                    }

                    WaitForReaderTasks(stdoutTask, stderrTask, Math.Max(100, timeoutMs));
                    var stdout = CompletedText(stdoutTask);
                    var stderr = CompletedText(stderrTask);
                    var combined = (stdout ?? "") + "\n" + (stderr ?? "");
                    var versionLine = FirstLineContaining(combined, "ffmpeg version");
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(versionLine))
                    {
                        return new FfmpegExecutableCheckResult(
                            FfmpegExecutableStatus.Found,
                            executable,
                            versionLine,
                            "");
                    }

                    return new FfmpegExecutableCheckResult(
                        FfmpegExecutableStatus.Invalid,
                        executable,
                        versionLine,
                        string.IsNullOrWhiteSpace(combined) ? "FFmpeg output was not recognized." : combined.Trim());
                }
            }
            catch (Win32Exception ex)
            {
                return new FfmpegExecutableCheckResult(
                    FfmpegExecutableStatus.Missing,
                    executable,
                    "",
                    ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                return new FfmpegExecutableCheckResult(
                    FfmpegExecutableStatus.Missing,
                    executable,
                    "",
                    ex.Message);
            }
            catch (Exception ex)
            {
                return new FfmpegExecutableCheckResult(
                    FfmpegExecutableStatus.Invalid,
                    executable,
                    "",
                    ex.Message);
            }
        }

        private static string FirstLineContaining(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        return line.Trim();
                }
            }

            return "";
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
                // Best effort cleanup only.
            }
        }

        private static void WaitForReaderTasks(Task<string> stdoutTask, Task<string> stderrTask, int timeoutMs)
        {
            try
            {
                Task.WaitAll(new Task[] { stdoutTask, stderrTask }, Math.Max(100, timeoutMs));
            }
            catch
            {
                // Best effort pipe drain only.
            }
        }

        private static string CompletedText(Task<string> task)
        {
            try
            {
                return task != null && task.IsCompleted ? task.Result ?? "" : "";
            }
            catch
            {
                return "";
            }
        }
    }

    public static class FfmpegExecutableResolver
    {
        private const string SystemPathExecutable = "ffmpeg";

        public static string ResolveExecutablePath(string configuredPath)
        {
            var path = NormalizeConfiguredPath(configuredPath);
            if (Path.IsPathRooted(path) || ContainsDirectorySeparator(path))
            {
                var resolvedPath = ResolveRootedOrRelativePath(path);
                return string.IsNullOrEmpty(resolvedPath) ? path : resolvedPath;
            }

            var pathResolvedExecutable = FindInPath(path);
            return string.IsNullOrEmpty(pathResolvedExecutable) ? path : pathResolvedExecutable;
        }

        private static string NormalizeConfiguredPath(string configuredPath)
        {
            var path = string.IsNullOrWhiteSpace(configuredPath) ? SystemPathExecutable : configuredPath.Trim();
            if (path.Length >= 2 && path[0] == '"' && path[path.Length - 1] == '"')
                path = path.Substring(1, path.Length - 2);
            return string.IsNullOrWhiteSpace(path) ? SystemPathExecutable : path;
        }

        private static string ResolveRootedOrRelativePath(string path)
        {
            try
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);

                if (!Directory.Exists(path))
                    return "";

                foreach (var candidateName in CandidateNames(SystemPathExecutable))
                {
                    var candidate = Path.Combine(path, candidateName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
            catch
            {
                // Ignore malformed or inaccessible configured paths.
            }

            return "";
        }

        private static string FindInPath(string executableName)
        {
            foreach (var dir in EnumeratePathDirectories())
            {
                foreach (var candidateName in CandidateNames(executableName))
                {
                    try
                    {
                        var candidate = Path.Combine(dir, candidateName);
                        if (File.Exists(candidate))
                            return Path.GetFullPath(candidate);
                    }
                    catch
                    {
                        // Ignore malformed PATH entries.
                    }
                }
            }

            return "";
        }

        private static IEnumerable<string> CandidateNames(string executableName)
        {
            yield return executableName;
            if (IsWindows() && string.IsNullOrEmpty(Path.GetExtension(executableName)))
                yield return executableName + ".exe";
        }

        private static IEnumerable<string> EnumeratePathDirectories()
        {
            var comparer = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var seen = new HashSet<string>(comparer);
            foreach (var pathValue in PathEnvironmentValues())
            {
                if (string.IsNullOrWhiteSpace(pathValue))
                    continue;

                var parts = pathValue.Split(Path.PathSeparator);
                foreach (var rawPart in parts)
                {
                    var dir = Unquote(rawPart);
                    if (string.IsNullOrWhiteSpace(dir) || !seen.Add(dir))
                        continue;

                    yield return dir;
                }
            }
        }

        private static IEnumerable<string> PathEnvironmentValues()
        {
            if (IsWindows())
            {
                yield return FirstNonEmpty(
                    Environment.GetEnvironmentVariable("Path"),
                    Environment.GetEnvironmentVariable("PATH"));
                yield return FirstNonEmpty(
                    SafeGetEnvironmentVariable("Path", EnvironmentVariableTarget.User),
                    SafeGetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
                yield return FirstNonEmpty(
                    SafeGetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine),
                    SafeGetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
                yield break;
            }

            yield return Environment.GetEnvironmentVariable("PATH");
        }

        private static string FirstNonEmpty(string primary, string secondary)
        {
            if (!string.IsNullOrWhiteSpace(primary))
                return primary;
            return secondary;
        }

        private static string SafeGetEnvironmentVariable(string variable, EnvironmentVariableTarget target)
        {
            try
            {
                return Environment.GetEnvironmentVariable(variable, target);
            }
            catch
            {
                return "";
            }
        }

        private static string Unquote(string value)
        {
            var trimmed = value == null ? "" : value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                return trimmed.Substring(1, trimmed.Length - 2);

            return trimmed;
        }

        private static bool ContainsDirectorySeparator(string path)
            => path.IndexOf(Path.DirectorySeparatorChar) >= 0
                || path.IndexOf(Path.AltDirectorySeparatorChar) >= 0;

        private static bool IsWindows()
            => Path.DirectorySeparatorChar == '\\';
    }
}
