// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: Explicit user-triggered Cisco OpenH264 binary downloader.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public readonly struct OpenH264InstallResult
    {
        public OpenH264InstallResult(bool success, string helperPath, string dllPath, string errorMessage)
        {
            Success = success;
            HelperPath = helperPath ?? "";
            DllPath = dllPath ?? "";
            ErrorMessage = errorMessage ?? "";
        }

        public bool Success { get; }
        public string HelperPath { get; }
        public string DllPath { get; }
        public string ErrorMessage { get; }
    }

    public static class OpenH264OfficialBinaryInstaller
    {
        public static OpenH264InstallResult Install(string installRoot)
        {
            if (!OpenH264InstallLocation.IsAllowedInstallRoot(installRoot, out var reason))
                return Fail(reason);

            var versionDir = OpenH264InstallLocation.GetVersionedDirectory(installRoot);
            var compressedPath = Path.Combine(versionDir, OpenH264OfficialBinaryManifest.AssetName);
            var finalDllPath = OpenH264InstallLocation.GetFinalDllPath(installRoot);
            var finalHelperPath = OpenH264InstallLocation.GetFinalHelperPath(installRoot);
            try
            {
                Directory.CreateDirectory(versionDir);
                DownloadFile(OpenH264OfficialBinaryManifest.DownloadUrl, compressedPath);

                var tempDll = finalDllPath + ".tmp";
                if (File.Exists(tempDll))
                    File.Delete(tempDll);

                if (!TryDecompressBZip2(compressedPath, tempDll, out var decompressError))
                {
                    return Fail(
                        decompressError
                        + "\nManual fallback: download "
                        + OpenH264OfficialBinaryManifest.AssetName
                        + ", decompress it, then choose the resulting DLL with ...");
                }

                if (File.Exists(finalDllPath))
                    File.Delete(finalDllPath);

                File.Move(tempDll, finalDllPath);

                if (!BuildHelperExecutable(versionDir, finalHelperPath, out var buildError))
                    return Fail(buildError);

                return new OpenH264InstallResult(true, finalHelperPath, finalDllPath, "");
            }
            catch (Exception ex)
            {
                return Fail(
                    ex.Message
                    + "\nManual fallback: open "
                    + OpenH264OfficialBinaryManifest.ReleasePageUrl
                    + ", download the pinned "
                    + OpenH264OfficialBinaryManifest.AssetName
                    + ", then choose the DLL and a locally built helper with ....");
            }
        }

        private static void DownloadFile(string url, string destination)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "Unity2Foxglove OpenH264 Installer";
                client.DownloadFile(url, destination);
            }
        }

        private static bool TryDecompressBZip2(string compressedPath, string outputPath, out string error)
        {
            // The official Cisco asset is .bz2. We intentionally do not bundle
            // SharpZipLib or another MIT BZip2 decompressor here; if that changes,
            // update THIRD_PARTY_NOTICES before shipping.
            if (TryDecompressWithBZip2Tool(compressedPath, outputPath, out error))
                return true;

            if (TryDecompressWithPythonBZip2(compressedPath, outputPath, out error))
                return true;

            if (string.IsNullOrEmpty(error))
                error = "No BZip2 decompressor was found. Install bzip2 or Python, or decompress the DLL manually.";
            return false;
        }

        private static bool TryDecompressWithBZip2Tool(string compressedPath, string outputPath, out string error)
        {
            error = "";
            var bzip2 = FindExecutable("bzip2");
            if (string.IsNullOrEmpty(bzip2))
            {
                error = "bzip2 executable was not found on PATH.";
                return false;
            }

            var workDir = Path.GetDirectoryName(outputPath) ?? "";
            var tempCompressed = outputPath + ".bz2";
            var tempDecompressed = outputPath;
            try
            {
                File.Copy(compressedPath, tempCompressed, true);
                if (File.Exists(tempDecompressed))
                    File.Delete(tempDecompressed);

                var result = RunProcess(
                    bzip2,
                    "-dkf " + QuoteArgument(tempCompressed),
                    workDir,
                    15000,
                    out var stderr);
                if (!result || !File.Exists(tempDecompressed))
                {
                    error = string.IsNullOrWhiteSpace(stderr)
                        ? "bzip2 did not produce the OpenH264 DLL."
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
            finally
            {
                TryDelete(tempCompressed);
            }
        }

        private static bool BuildHelperExecutable(string workingDirectory, string outputPath, out string error)
        {
            error = "";
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                error = "Automatic OpenH264 helper builds are currently supported only in the Windows Unity Editor.";
                return false;
            }

            var packageRoot = GetPackageRoot();
            if (string.IsNullOrEmpty(packageRoot))
            {
                error = "Could not locate the Unity2Foxglove package root for OpenH264 helper sources.";
                return false;
            }

            var helperSource = Path.Combine(
                packageRoot,
                OpenH264OfficialBinaryManifest.HelperSourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var headerInclude = Path.Combine(
                packageRoot,
                OpenH264OfficialBinaryManifest.HeaderIncludeRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(helperSource))
            {
                error = "OpenH264 helper source was not found: " + helperSource;
                return false;
            }

            if (!Directory.Exists(headerInclude))
            {
                error = "OpenH264 header include directory was not found: " + headerInclude;
                return false;
            }

            var vcvars = FindVcVars64(out var vcvarsError);
            if (string.IsNullOrEmpty(vcvars))
            {
                error = vcvarsError;
                return false;
            }

            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                var buildScript = Path.Combine(workingDirectory, "build_openh264_helper.cmd");
                File.WriteAllLines(
                    buildScript,
                    new[]
                    {
                        "@echo off",
                        "call " + QuoteBatchPath(vcvars),
                        "if errorlevel 1 exit /b %errorlevel%",
                        "cl /nologo /EHsc /std:c++17 /O2 /I "
                            + QuoteBatchPath(headerInclude)
                            + " "
                            + QuoteBatchPath(helperSource)
                            + " /Fe:"
                            + QuoteBatchPath(outputPath),
                        "exit /b %errorlevel%"
                    });

                var result = RunProcess(
                    "cmd.exe",
                    "/c " + QuoteArgument(buildScript),
                    workingDirectory,
                    120000,
                    out var stdout,
                    out var stderr);
                if (!result || !File.Exists(outputPath))
                {
                    error = "OpenH264 helper build failed."
                        + BuildProcessDetails(stdout, stderr)
                        + "\nManual fallback: build "
                        + OpenH264OfficialBinaryManifest.HelperFileName
                        + " from "
                        + helperSource
                        + " and choose it with ....";
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

        private static string GetPackageRoot()
        {
            try
            {
                var package = PackageInfo.FindForAssembly(typeof(OpenH264OfficialBinaryInstaller).Assembly);
                if (package != null && !string.IsNullOrWhiteSpace(package.resolvedPath))
                    return package.resolvedPath;
            }
            catch
            {
                // Fall through to repository-style layout.
            }

            var candidate = Path.Combine(Directory.GetCurrentDirectory(), "Packages", "dev.unity2foxglove.sdk");
            return Directory.Exists(candidate) ? candidate : "";
        }

        private static string FindVcVars64(out string error)
        {
            error = "";
            var fromVsWhere = FindVcVars64WithVsWhere();
            if (!string.IsNullOrEmpty(fromVsWhere))
                return fromVsWhere;

            foreach (var root in CandidateVisualStudioRoots())
            {
                foreach (var year in new[] { "2026", "2022", "2019" })
                {
                    foreach (var edition in new[] { "Community", "Professional", "Enterprise", "BuildTools" })
                    {
                        var candidate = Path.Combine(
                            root,
                            "Microsoft Visual Studio",
                            year,
                            edition,
                            "VC",
                            "Auxiliary",
                            "Build",
                            "vcvars64.bat");
                        if (File.Exists(candidate))
                            return candidate;
                    }
                }
            }

            error = "Visual Studio C++ build environment was not found. Install Visual Studio C++ Build Tools, or build "
                + OpenH264OfficialBinaryManifest.HelperFileName
                + " manually and choose it with ....";
            return "";
        }

        private static string FindVcVars64WithVsWhere()
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vsWhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vsWhere))
                return "";

            var ok = RunProcess(
                vsWhere,
                "-latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath",
                "",
                15000,
                out var stdout,
                out _);
            if (!ok || string.IsNullOrWhiteSpace(stdout))
                return "";

            using (var reader = new StringReader(stdout))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var root = line.Trim();
                    if (string.IsNullOrEmpty(root))
                        continue;

                    var candidate = Path.Combine(root, "VC", "Auxiliary", "Build", "vcvars64.bat");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return "";
        }

        private static string[] CandidateVisualStudioRoots()
        {
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
        }

        private static bool TryDecompressWithPythonBZip2(string compressedPath, string outputPath, out string error)
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
                var result = RunProcess(
                    python,
                    "-c " + QuoteArgument(script) + " " + QuoteArgument(compressedPath) + " " + QuoteArgument(outputPath),
                    "",
                    15000,
                    out var stderr);
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
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory ?? "",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    if (!process.Start())
                        return false;

                    if (!process.WaitForExit(Math.Max(500, timeoutMs)))
                    {
                        TryKill(process);
                        stderr = "Process timed out.";
                        return false;
                    }

                    stdout = process.StandardOutput.ReadToEnd();
                    stderr = process.StandardError.ReadToEnd();
                    return process.ExitCode == 0;
                }
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
            if (Application.platform == RuntimePlatform.WindowsEditor && string.IsNullOrEmpty(Path.GetExtension(executableName)))
                return new[] { executableName, executableName + ".exe" };

            return new[] { executableName };
        }

        private static string[] EnumeratePathDirectories()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? Environment.GetEnvironmentVariable("Path") ?? "";
            return path.Split(Path.PathSeparator);
        }

        private static string QuoteArgument(string value)
            => "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";

        private static string QuoteBatchPath(string value)
            => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        private static string BuildProcessDetails(string stdout, string stderr)
        {
            var details = "";
            if (!string.IsNullOrWhiteSpace(stdout))
                details += "\nstdout:\n" + stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                details += "\nstderr:\n" + stderr.Trim();
            return details;
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

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static OpenH264InstallResult Fail(string message)
            => new OpenH264InstallResult(false, "", "", string.IsNullOrWhiteSpace(message) ? "OpenH264 install failed." : message);
    }
}
