// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: OpenH264 download, decompression and process helpers.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class OpenH264OfficialBinaryInstaller
    {
        /// <summary>Builds the local OpenH264 helper executable in the private staging directory.</summary>
        private static bool BuildHelperExecutable(
            string workingDirectory,
            string outputPath,
            string packageRoot,
            Func<bool> validateDestination,
            out string error)
        {
            error = "";
            if (!IsWindowsEditor)
            {
                error = "Automatic OpenH264 helper builds are currently supported only in the Windows Unity Editor.";
                return false;
            }

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
                EnsureDestinationAllowed(validateDestination);
                if (File.Exists(outputPath))
                {
                    EnsureDestinationAllowed(validateDestination);
                    File.Delete(outputPath);
                }

                var buildScript = Path.Combine(workingDirectory, "build_openh264_helper.cmd");
                EnsureDestinationAllowed(validateDestination);
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

                EnsureDestinationAllowed(validateDestination);
                var result = RunProcess(
                    "cmd.exe",
                    "/c " + QuoteArgument(buildScript),
                    workingDirectory,
                    120000,
                    out var stdout,
                    out var stderr);
                EnsureDestinationAllowed(validateDestination);
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
        /// <summary>Finds a Visual Studio x64 developer environment for helper compilation.</summary>
        private static string FindVcVars64(out string error)
        {
            error = "";
            var fromVsWhere = FindVcVars64WithVsWhere();
            if (!string.IsNullOrEmpty(fromVsWhere))
                return fromVsWhere;

            foreach (var root in CandidateVisualStudioRoots())
            {
                foreach (var year in new[] { "2022", "2019" })
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
        /// <summary>Finds the Visual Studio x64 environment through vswhere when available.</summary>
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
        /// <summary>Returns the supported Visual Studio installation roots.</summary>
        private static string[] CandidateVisualStudioRoots()
        {
            return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
        }
        /// <summary>Quotes a path for a Windows batch file while escaping percent signs.</summary>
        private static string QuoteBatchPath(string value)
            => "\"" + (value ?? "").Replace("%", "%%").Replace("\"", "\"\"") + "\"";

        /// <summary>Deletes an install staging file through the locked version directory; missing files succeed.</summary>
        private static bool TryDelete(
            OpenH264WindowsSafeDirectory directory,
            string path,
            bool warnOnFailure = false)
        {
            var reason = "The OpenH264 version directory is not open.";
            if (directory != null && directory.TryDeleteFile(Path.GetFileName(path), out reason))
                return true;
            if (warnOnFailure)
                UnityEngine.Debug.LogWarning("[Foxglove] Could not delete temporary OpenH264 installer file '" + path + "': " + reason);
            return false;
        }

        /// <summary>Rejects a path operation when the validated install root changed.</summary>
        private static void EnsureDestinationAllowed(Func<bool> validateDestination)
        {
            if (validateDestination != null && !validateDestination())
                throw new IOException("OpenH264 install destination changed during an install operation.");
        }

        /// <summary>Formats helper compiler output for a user-facing failure.</summary>
        private static string BuildProcessDetails(string stdout, string stderr)
        {
            var details = "";
            if (!string.IsNullOrWhiteSpace(stdout))
                details += "\nstdout:\n" + stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                details += "\nstderr:\n" + stderr.Trim();
            return details;
        }
    }
}
