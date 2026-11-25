// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: OpenH264 download, decompression and process helpers.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class OpenH264OfficialBinaryInstaller
    {
        private const int MaxCompressedAssetBytes = 16 * 1024 * 1024;
        private const int MaxDecompressedDllBytes = 64 * 1024 * 1024;

        /// <summary>Downloads response bytes directly into a parent-owned safe file.</summary>
        private static void DownloadFile(
            string url,
            OpenH264WindowsSafeFile destination,
            Func<bool> validateDestination)
        {
            EnsureDestinationAllowed(validateDestination);
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Unity2Foxglove-OpenH264-Installer/1.0");
                using (var cts = new CancellationTokenSource(DownloadBodyTimeoutMs))
                using (var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    using (var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    {
                        EnsureDestinationAllowed(validateDestination);
                        if (!destination.WriteFromStream(
                                source,
                                MaxCompressedAssetBytes,
                                cts.Token,
                                out _,
                                out var writeError))
                            throw new IOException("OpenH264 download could not be written safely: " + writeError);
                    }
                }
            }
        }

        /// <summary>Decompresses a safe input file into a safe output file through stdin/stdout.</summary>
        private static bool TryDecompressBZip2(
            OpenH264WindowsSafeFile compressedFile,
            OpenH264WindowsSafeFile outputFile,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            EnsureDestinationAllowed(validateDestination);
            // The official Cisco asset is .bz2. We intentionally do not bundle
            // SharpZipLib or another MIT BZip2 decompressor here; if that changes,
            // update THIRD_PARTY_NOTICES before shipping.
            if (!compressedFile.ReadAllBytes(
                    MaxCompressedAssetBytes,
                    out var compressedBytes,
                    out var readError))
            {
                error = "Could not read the compressed OpenH264 asset safely: " + readError;
                return false;
            }

            if (TryDecompressWithBZip2Tool(compressedBytes, outputFile, validateDestination, out var bzip2Error))
                return true;
            if (TryDecompressWithPythonBZip2(compressedBytes, outputFile, validateDestination, out var pythonError))
                return true;

            error = CombineDecompressErrors(bzip2Error, pythonError);
            if (string.IsNullOrEmpty(error))
                error = "No BZip2 decompressor was found. Install bzip2 or Python, or decompress the DLL manually.";
            return false;
        }

        /// <summary>Runs bzip2 with parent-owned stdin/stdout and no output path argument.</summary>
        private static bool TryDecompressWithBZip2Tool(
            byte[] compressedBytes,
            OpenH264WindowsSafeFile outputFile,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            var bzip2 = FindExecutable("bzip2");
            if (string.IsNullOrEmpty(bzip2))
            {
                error = "bzip2 executable was not found on PATH.";
                return false;
            }

            return RunDecompressProcess(
                bzip2,
                "-dc",
                compressedBytes,
                outputFile,
                validateDestination,
                out error);
        }

        /// <summary>Runs Python bz2 with parent-owned stdin/stdout and no output path argument.</summary>
        private static bool TryDecompressWithPythonBZip2(
            byte[] compressedBytes,
            OpenH264WindowsSafeFile outputFile,
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

            return RunDecompressProcess(
                python,
                "-c " + QuoteArgument("import bz2,sys;sys.stdout.buffer.write(bz2.decompress(sys.stdin.buffer.read()))"),
                compressedBytes,
                outputFile,
                validateDestination,
                out error);
        }

        /// <summary>Transfers bounded decompressor output into the safe output handle.</summary>
        private static bool RunDecompressProcess(
            string executable,
            string arguments,
            byte[] compressedBytes,
            OpenH264WindowsSafeFile outputFile,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            try
            {
                EnsureDestinationAllowed(validateDestination);
                var result = FoxgloveEditorProcessRunner.RunBytes(
                    new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = arguments,
                        WorkingDirectory = ""
                    },
                    compressedBytes,
                    MaxDecompressedDllBytes,
                    30000);
                EnsureDestinationAllowed(validateDestination);
                if (result.TimedOut || result.OutputReadFailed || result.ExitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(result.Stderr)
                        ? "OpenH264 decompressor failed."
                        : result.Stderr.Trim();
                    return false;
                }

                if (!outputFile.WriteAllBytes(result.Stdout, out var writeError))
                {
                    error = "OpenH264 decompressor output could not be written safely: " + writeError;
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

        /// <summary>Runs a text-oriented helper process with bounded timeout and captured diagnostics.</summary>
        private static bool RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            int timeoutMs,
            out string stderr)
            => RunProcess(fileName, arguments, workingDirectory, timeoutMs, out _, out stderr);

        /// <summary>Runs a text-oriented helper process and captures both output streams.</summary>
        private static bool RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            int timeoutMs,
            out string stdout,
            out string stderr)
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

        /// <summary>Finds an executable in the current process PATH.</summary>
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

        /// <summary>Returns platform-specific executable name candidates.</summary>
        private static string[] CandidateNames(string executableName)
        {
            if (IsWindowsEditor && string.IsNullOrEmpty(Path.GetExtension(executableName)))
                return new[] { executableName, executableName + ".exe" };

            return new[] { executableName };
        }

        /// <summary>Returns whether the current build is the Windows Unity Editor.</summary>
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

        /// <summary>Enumerates PATH entries without resolving child output paths.</summary>
        private static string[] EnumeratePathDirectories()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? Environment.GetEnvironmentVariable("Path") ?? "";
            return path.Split(Path.PathSeparator);
        }

        /// <summary>Quotes a process argument for the existing redirected runner.</summary>
        private static string QuoteArgument(string value)
            => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

        /// <summary>Formats decompressor error details.</summary>
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
