// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Publishers
// Purpose: OpenH264 helper executable build and Visual Studio discovery.

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

        internal static string GetPackageRoot()
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
    }
}
