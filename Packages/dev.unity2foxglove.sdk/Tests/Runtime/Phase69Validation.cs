// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 69 validation for MCAP indexed reader Inspector integration.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates that the Phase 68 indexed reader is exposed through the
    /// FoxgloveManager Inspector replay workflow.
    /// </summary>
    public static class Phase69Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 69 validation checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 69: MCAP Inspector Preflight ===");
            _passed = 0;
            var sources = Phase69Sources.Load();

            VerifyReplayInspectorExposesPreflightActions(sources);
            VerifyPreflightUsesIndexedReaderSurface(sources.DrawerSource);
            VerifyLatestRecordingPickerUsesRecordingsDirectory(sources.DrawerSource);
            VerifyPreflightDrawerIsSeparateModule(sources);

            Console.WriteLine($"Phase 69: {_passed} checks passed.");
        }

        private static void VerifyReplayInspectorExposesPreflightActions(Phase69Sources sources)
        {
            Check(sources.ManagerSource.Contains("_mcapReplayPreflight.Draw(serializedObject, target, replayPath)"),
                "69A-1: replay Inspector delegates to MCAP preflight drawer");
            Check(sources.DrawerSource.Contains("Analyze Replay File"),
                "69A-2: replay Inspector exposes Analyze Replay File action");
            Check(sources.DrawerSource.Contains("Use Latest Recording"),
                "69A-3: replay Inspector exposes Use Latest Recording action");
            Check(sources.DrawerSource.Contains("MCAP Indexed Reader Summary"),
                "69A-4: replay Inspector renders indexed summary label");
            Check(sources.DrawerSource.Contains("Copy Topics"),
                "69A-5: replay Inspector exposes Copy Topics action");
            Check(sources.DrawerSource.Contains("EditorGUILayout.Foldout(_mcapTopicsExpanded"),
                "69A-6: replay Inspector exposes a collapsible topic list");
        }

        private static void VerifyPreflightUsesIndexedReaderSurface(string source)
        {
            Check(source.Contains("using Unity.FoxgloveSDK.IO;"),
                "69B-1: editor references runtime MCAP IO namespace");
            Check(source.Contains("McapIndexedReader.OpenRead"),
                "69B-2: Inspector preflight opens MCAP through McapIndexedReader");
            Check(source.Contains("indexed.Summary.ChunkIndexes.Count"),
                "69B-3: Inspector summary reports chunk index count");
            Check(source.Contains("indexed.Channels.Count"),
                "69B-4: Inspector summary reports channel count");
            Check(source.Contains("indexed.MetadataIndexes.Count"),
                "69B-5: Inspector summary reports metadata index count");
            Check(source.Contains("indexed.AttachmentIndexes.Count"),
                "69B-6: Inspector summary reports attachment index count");
            Check(source.Contains("FormatMcapTimeRange(statistics)"),
                "69B-7: Inspector summary formats replay time range as human-readable UTC");
            Check(source.Contains("Raw Time Range"),
                "69B-8: Inspector summary preserves raw nanosecond time range");
            Check(source.Contains("EditorGUIUtility.systemCopyBuffer = _mcapPreflightTopics"),
                "69B-9: Copy Topics writes the analyzed topic list to clipboard");
        }

        private static void VerifyLatestRecordingPickerUsesRecordingsDirectory(string source)
        {
            Check(source.Contains("FindLatestReadableRecording"),
                "69C-1: Inspector has latest readable recording helper");
            Check(source.Contains("Path.Combine(GetDefaultDir(), \"Recordings\")"),
                "69C-2: latest recording helper uses the Unity project Recordings directory");
            Check(source.Contains("using (McapIndexedReader.OpenRead(candidate.Path))"),
                "69C-3: latest recording helper verifies MCAP summary readability");
            Check(source.Contains("catch (InvalidDataException)"),
                "69C-4: latest recording helper skips malformed MCAP files");
            Check(source.Contains("MakeRelative(latestRecording)") && source.Contains("replayPath.stringValue"),
                "69C-5: Use Latest Recording writes a project-relative replay path");
        }

        private static void VerifyPreflightDrawerIsSeparateModule(Phase69Sources sources)
        {
            Check(sources.DrawerSource.Contains("internal sealed class McapReplayPreflightDrawer"),
                "69D-1: MCAP replay preflight lives in a separate Editor module");
            Check(sources.ManagerSource.Contains("_mcapReplayPreflight.Draw(serializedObject, target, replayPath)"),
                "69D-2: FoxgloveManagerEditor delegates replay preflight drawing");
            Check(!sources.ManagerSource.Contains("private void AnalyzeReplayMcap")
                  && !sources.ManagerSource.Contains("private static bool FindLatestReadableRecording"),
                "69D-3: FoxgloveManagerEditor does not own preflight IO implementation");
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private sealed class Phase69Sources
        {
            public string ManagerSource { get; }
            public string DrawerSource { get; }

            private Phase69Sources(string managerSource, string drawerSource)
            {
                ManagerSource = managerSource;
                DrawerSource = drawerSource;
            }

            public static Phase69Sources Load()
                => new Phase69Sources(
                    ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs"),
                    ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs"));
        }

        private static string FindRepoRoot()
        {
            var dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }
}
