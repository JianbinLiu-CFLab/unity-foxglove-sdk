// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: OpenH264 Python decompression, process and path support.

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading;
using UnityEditor.PackageManager;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class OpenH264OfficialBinaryInstaller
    {
        private static bool TryDecompressWithPythonBZip2(
            string compressedPath,
            string outputPath,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            var python = FindExecutable("python");
            if (string.IsNullOrEmpty(python))
            {
                error = "Python with the standard-library bz2 module was not found on PATH.";
                return false;
            }

            var script = "import bz2,sys;open(sys.argv[2],'wb').write(bz2.open(sys.argv[1],'rb').read())";
            try
            {
                EnsureDestinationAllowed(validateDestination);
                var result = RunProcess(
                    python,
                    "-c " + QuoteArgument(script) + " " + QuoteArgument(compressedPath) + " " + QuoteArgument(outputPath),
                    "",
                    15000,
                    out var stderr);
                EnsureDestinationAllowed(validateDestination);
                if (!result || !File.Exists(outputPath))
                {
                    error = string.IsNullOrWhiteSpace(stderr)
                        ? "Python bz2 did not produce the OpenH264 DLL."
                        : stderr.Trim();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs, out string stderr)
            => RunProcess(fileName, arguments, workingDirectory, timeoutMs, out _, out stderr);

        private static bool RunProcess(string fileName, string arguments, string workingDirectory, int timeoutMs, out string stdout, out string stderr)
        {
            stdout = "";
            stderr = "";
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? ""
                };
                var result = FoxgloveEditorProcessRunner.Run(startInfo, Math.Max(500, timeoutMs));
                stdout = result.Stdout;
                stderr = result.Stderr;
                if (result.TimedOut)
                {
                    stderr = string.IsNullOrWhiteSpace(stderr) ? "Process timed out." : "Process timed out.\n" + stderr;
                    return false;
                }

                return result.ExitCode == 0;
            }
            catch (Win32Exception ex)
            {
                stderr = ex.Message;
                return false;
            }
        }

        private static string FindExecutable(string executableName)
        {
            foreach (var dir in EnumeratePathDirectories())
            {
                foreach (var candidateName in CandidateNames(executableName))
                {
                    try
                    {
                        var candidate = Path.Combine(dir, candidateName);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch
                    {
                        // Ignore malformed PATH entries.
                    }
                }
            }

            return "";
        }

        private static string[] CandidateNames(string executableName)
        {
            if (IsWindowsEditor && string.IsNullOrEmpty(Path.GetExtension(executableName)))
                return new[] { executableName, executableName + ".exe" };

            return new[] { executableName };
        }

        private static bool IsWindowsEditor
        {
            get
            {
#if UNITY_EDITOR_WIN
                return true;
#else
                return false;
#endif
            }
        }

        private static string[] EnumeratePathDirectories()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? Environment.GetEnvironmentVariable("Path") ?? "";
            return path.Split(Path.PathSeparator);
        }

        private static string QuoteArgument(string value)
            => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

        private static string QuoteBatchPath(string value)
            => "\"" + (value ?? "").Replace("%", "%%").Replace("\"", "\"\"") + "\"";

        private static string BuildProcessDetails(string stdout, string stderr)
        {
            var details = "";
            if (!string.IsNullOrWhiteSpace(stdout))
                details += "\nstdout:\n" + stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                details += "\nstderr:\n" + stderr.Trim();
            return details;
        }

        private static bool TryDelete(
            string path,
            bool warnOnFailure = false,
            Func<bool> validateDestination = null)
        {
            try
            {
                if (validateDestination != null && !validateDestination())
                    return false;
                if (File.Exists(path))
                {
                    if (validateDestination != null && !validateDestination())
                        return false;
                    File.Delete(path);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (warnOnFailure)
                    UnityEngine.Debug.LogWarning("[Foxglove] Could not delete temporary OpenH264 installer file '" + path + "': " + ex.Message);
                return false;
            }
        }

        private static void EnsureDestinationAllowed(Func<bool> validateDestination)
        {
            if (validateDestination != null && !validateDestination())
                throw new IOException("OpenH264 install destination changed during an install operation.");
        }

        private static OpenH264InstallResult Fail(string message)
            => new OpenH264InstallResult(false, "", "", string.IsNullOrWhiteSpace(message) ? "OpenH264 install failed." : message);

        private static string CombineDecompressErrors(string bzip2Error, string pythonError)
        {
            if (string.IsNullOrWhiteSpace(bzip2Error))
                return pythonError ?? "";
            if (string.IsNullOrWhiteSpace(pythonError))
                return bzip2Error;
            return "bzip2 failed: " + bzip2Error + "\nPython bz2 failed: " + pythonError;
        }
    }
}
