// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 77 validation for manual-only FFmpeg setup UX.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the editor-only manual FFmpeg setup UX through source
    /// checks so the runtime test assembly stays Unity-free.
    /// </summary>
    public static class Phase77Validation
    {
        private static int _passed;

        /// <summary>Runs all Phase 77 validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 77: FFmpeg Manual Setup UX ===");
            _passed = 0;

            VerifyNoAutomaticInstallerSources();
            VerifyCameraEditorSource();
            VerifyDocsAndNotices();

            Console.WriteLine($"Phase 77: {_passed} checks passed.");
        }

        private static void VerifyNoAutomaticInstallerSources()
        {
            Check(string.IsNullOrEmpty(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FfmpegInstallManifest.cs")),
                "77A-1: no pinned FFmpeg download manifest is shipped");
            Check(string.IsNullOrEmpty(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FfmpegInstallLocation.cs")),
                "77A-2: no FFmpeg install-location helper is shipped");
            Check(string.IsNullOrEmpty(ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FfmpegInstaller.cs")),
                "77A-3: no automatic FFmpeg downloader/installer is shipped");
        }

        private static void VerifyCameraEditorSource()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            Check(!string.IsNullOrEmpty(source), "77D-1: camera editor source exists");
            Check(source.Contains("FFmpeg Help..."), "77D-2: camera editor exposes manual FFmpeg help action");
            Check(source.Contains("DrawFfmpegHelpAction") && source.Contains("FfmpegHelpWindow.ShowWindow"),
                "77D-3: help action is always visible in FFmpeg video modes");
            Check(!source.Contains("FfmpegInstaller") && !source.Contains("FfmpegInstallManifest") && !source.Contains("DownloadFfmpeg"),
                "77D-4: camera editor does not call automatic installer workflow");
            Check(!source.Contains("GUILayout.Button(\"Install FFmpeg\")")
                  && !source.Contains("FfmpegInstallLocation")
                  && !source.Contains("Download FFmpeg"),
                "77D-5: FFmpeg help window has no one-click FFmpeg install controls");
            Check(source.Contains("does not bundle, download, install, or modify PATH"),
                "77D-6: help window explains why the SDK does not auto-install FFmpeg");
            Check(source.Contains("GPL") && source.Contains("libx264") && source.Contains("libx265"),
                "77D-7: help window warns about common GPL encoder components");
            Check(source.Contains("Manual Download") && source.Contains("Open FFmpeg Legal") && !source.Contains("Download FFmpeg"),
                "77D-8: help window uses manual/legal actions only");
            Check(source.Contains("Check FFmpeg") && source.Contains("Reveal Folder"),
                "77D-9: existing check and reveal actions remain");
            Check(source.Contains("Use ...") && source.Contains("FFmpeg Help"),
                "77D-10: missing/invalid status text points to browse or help choices");
        }

        private static void VerifyDocsAndNotices()
        {
            var troubleshooting = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/11_Troubleshooting.md");
            Check(troubleshooting.Contains("FFmpeg Help") && troubleshooting.Contains("PATH"),
                "77E-1: troubleshooting documents manual setup and PATH behavior");
            Check(troubleshooting.Contains("does not bundle, download, install, or modify") && troubleshooting.Contains("GPL"),
                "77E-2: troubleshooting documents licensing boundary and GPL risk");

            var inspector = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md");
            Check(inspector.Contains("FFmpeg Help") && inspector.Contains("H.264") && inspector.Contains("H.265"),
                "77E-3: Inspector reference documents manual FFmpeg help for video modes");
            Check(inspector.Contains("does not bundle, download, install, or modify") && inspector.Contains("libx264"),
                "77E-4: Inspector reference documents why auto-install is intentionally absent");

            var notices = ReadRepoText("THIRD_PARTY_NOTICES.md");
            Check(!notices.Contains("Bundled FFmpeg"),
                "77E-5: third-party notices do not claim FFmpeg is bundled");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine($"[PASS] {name}");
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : "";
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }

            return null;
        }
    }
}
