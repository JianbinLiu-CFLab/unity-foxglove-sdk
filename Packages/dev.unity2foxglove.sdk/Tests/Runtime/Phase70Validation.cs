// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 70 validation for FoxgloveManager Inspector workflow UX.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates that the FoxgloveManager Inspector is organized around
    /// user-facing workflows without changing runtime behavior.
    /// </summary>
    public static class Phase70Validation
    {
        private static int _passed;

        /// <summary>
        /// Runs all Phase 70 validation checks.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 70: Manager Inspector Workflow UX ===");
            _passed = 0;

            VerifyWorkflowSections();
            VerifyPublishDataHeaders();
            VerifySerializedFieldCoverage();
            VerifyConnectionSecurityAdjacency();
            VerifyMcapWorkflowGrouping();
            VerifyPreflightModuleBoundary();
            VerifyLayoutHelperExists();

            Console.WriteLine($"Phase 70: {_passed} checks passed.");
        }

        private static void VerifyWorkflowSections()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            Check(source.Contains("DrawSection(\"Connection & Security\""),
                "70A-1: Inspector has Connection & Security workflow section");
            Check(source.Contains("DrawSection(\"Publish Data\""),
                "70A-2: Inspector has Publish Data workflow section");
            Check(source.Contains("DrawSection(\"MCAP Record & Replay\""),
                "70A-3: Inspector has MCAP Record & Replay workflow section");
            Check(source.Contains("DrawSection(\"Diagnostics\""),
                "70A-4: Inspector has Diagnostics workflow section");
            CheckOrdered(source,
                "DrawSection(\"Connection & Security\"",
                "DrawSection(\"Publish Data\"",
                "70A-4b: main workflow order starts with Connection & Security before Publish Data");
            CheckOrdered(source,
                "DrawSection(\"Publish Data\"",
                "DrawSection(\"MCAP Record & Replay\"",
                "70A-4c: main workflow order promotes MCAP before optional bridge");
            CheckOrdered(source,
                "DrawSection(\"MCAP Record & Replay\"",
                "DrawSection(\"ROS2 Bridge\"",
                "70A-4d: ROS2 Bridge follows MCAP Record & Replay");
            CheckOrdered(source,
                "DrawSection(\"ROS2 Bridge\"",
                "DrawSection(\"Diagnostics\"",
                "70A-4e: Diagnostics remains last after ROS2 Bridge");

            Check(!source.Contains("DrawSection(\"Server\""),
                "70A-5: Server is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"Publisher Encoding\""),
                "70A-6: Publisher Encoding is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"Coordinate System\""),
                "70A-7: Coordinate System is no longer a top-level section");
            Check(!source.Contains("Subheader(\"Coordinates\""),
                "70A-7b: Publish Data relies on the serialized Coordinate System header");
            Check(!source.Contains("DrawSection(\"Assets\""),
                "70A-8: Assets is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"Playback Control\""),
                "70A-9: Playback Control is no longer a top-level section");
            Check(!source.Contains("Subheader(\"Recording\""),
                "70A-9b: MCAP section relies on the serialized MCAP Recording header");
            Check(!source.Contains("Subheader(\"Replay\""),
                "70A-9c: MCAP section relies on the serialized MCAP Replay header");
            Check(!source.Contains("Subheader(\"Replay Preflight\""),
                "70A-9d: MCAP preflight drawer owns its specific headings");
            Check(!source.Contains("DrawSection(\"MCAP Recording\""),
                "70A-10: MCAP Recording is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"MCAP Replay\""),
                "70A-11: MCAP Replay is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"Security\""),
                "70A-12: Security is no longer a top-level section");
            Check(!source.Contains("DrawSection(\"Transport Health\""),
                "70A-13: Transport Health is no longer a top-level section");
        }

        private static void VerifyPublishDataHeaders()
        {
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs");
            var section = Slice(editorSource, "private void DrawPublishDataSection()", "private void DrawMcapSection()");

            Check(section.Contains("Subheader(\"Publish Rate\")"),
                "70A-14: Publish Data labels rate settings as Publish Rate");
            Check(section.Contains("Subheader(\"Publisher Encoding\")"),
                "70A-15: Publish Data labels global encoding settings as Publisher Encoding");
            Check(!section.Contains("Subheader(\"Rate\")") && !section.Contains("Subheader(\"Encoding\")"),
                "70A-16: Publish Data avoids ambiguous Rate and Encoding headings");
            Check(IndexOf(section, "Subheader(\"Publish Rate\")") < IndexOf(section, "_defaultPublishRateHz")
                  && IndexOf(section, "_defaultPublishRateHz") < IndexOf(section, "Subheader(\"Publisher Encoding\")")
                  && IndexOf(section, "Subheader(\"Publisher Encoding\")") < IndexOf(section, "_defaultPublisherEncoding")
                  && IndexOf(section, "_defaultPublisherEncoding") < IndexOf(section, "_allowPublisherOverride"),
                "70A-17: Publish Data header order is Publish Rate -> Publisher Encoding");
            var publishRateHeader = IndexOf(managerSource, "[Header(\"Publish Rate\")]");
            var publishRateField = IndexOf(managerSource, "_defaultPublishRateHz");
            var publisherEncodingHeader = IndexOf(managerSource, "[Header(\"Publisher Encoding\")]");
            var publisherEncodingField = IndexOf(managerSource, "_defaultPublisherEncoding");
            Check(publishRateHeader >= 0
                  && publishRateField >= 0
                  && publisherEncodingHeader >= 0
                  && publisherEncodingField >= 0
                  && publishRateHeader < publishRateField
                  && publishRateField < publisherEncodingHeader
                  && publisherEncodingHeader < publisherEncodingField,
                "70A-18: serialized headers align with rate and encoding fields");
        }

        private static void VerifySerializedFieldCoverage()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var fields = new[]
            {
                "_serverName",
                "_transportMode",
                "_host",
                "_port",
                "_startOnEnable",
                "_runInBackground",
                "_defaultPublisherEncoding",
                "_allowPublisherOverride",
                "_coordinateMode",
                "_assetRoots",
                "_enablePlaybackControl",
                "_playbackStartOffsetSeconds",
                "_playbackDurationSeconds",
                "_enableRecording",
                "_recordingPrefix",
                "_recordingDirectory",
                "_recordingChunkSizeKB",
                "_recordingCompression",
                "_enableReplay",
                "_replayFilePath",
                "_replayAutoPlay",
                "_disableLivePublishers",
                "_allowHostedFoxgloveWeb",
                "_allowedBrowserOrigins",
                "_certificatePfxPath",
                "_certificatePassword",
                "_rootCaDistributorEnabled",
                "_rootCaDistributorHost",
                "_rootCaDistributorPort",
                "_rootCaFilePath",
                "_sharedToken"
            };

            for (var i = 0; i < fields.Length; i++)
                Check(source.Contains(fields[i]), $"70B-{i + 1}: Inspector still references {fields[i]}");
        }

        private static void VerifyConnectionSecurityAdjacency()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var section = Slice(source, "private void DrawConnectionSecuritySection()", "private void DrawPublishDataSection()");
            Check(section.Contains("_transportMode"),
                "70C-1: Connection & Security contains transport mode");
            Check(section.Contains("_certificatePfxPath") || section.Contains("DrawSecureWebSocketFields"),
                "70C-2: Connection & Security contains WSS certificate path");
            Check(section.Contains("_sharedToken") || section.Contains("DrawSecureWebSocketFields"),
                "70C-3: Connection & Security contains shared token");
            Check(section.Contains("_allowedBrowserOrigins"),
                "70C-4: Connection & Security contains browser Origin settings");
            Check(section.Contains("DrawSecureWebSocketSection"),
                "70C-5: Connection & Security owns WSS security rendering");
            Check(source.Contains("_connectionSecurityExpanded = true"),
                "70C-6: Connection & Security is expanded by default");
            var secureAutoExpand = Slice(source, "private void EnsureSecureSettingsVisible()", "private static void DrawSection");
            Check(secureAutoExpand.Contains("_connectionSecurityExpanded = true"),
                "70C-7: secure settings auto-expand Connection & Security");
            var certificateButtons = Slice(source, "private void DrawCertificateUtilityButtons", "private void DrawTransportHealth");
            Check(CountOccurrences(certificateButtons, "new EditorGUILayout.HorizontalScope()") >= 2,
                "70C-8: certificate utility actions are split into rows with at most two buttons");
        }

        private static void VerifyMcapWorkflowGrouping()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var section = Slice(source, "private void DrawMcapSection()", "private void DrawDiagnosticsSection()");
            Check(section.Contains("_enablePlaybackControl"),
                "70D-1: MCAP section contains Playback Control");
            Check(section.Contains("_enableRecording"),
                "70D-2: MCAP section contains Recording");
            Check(section.Contains("_enableReplay"),
                "70D-3: MCAP section contains Replay");
            Check(section.Contains("_mcapReplayPreflight.Draw(serializedObject, target, replayPath)"),
                "70D-4: MCAP section contains indexed reader preflight");
            Check(IndexOf(section, "_enablePlaybackControl") < IndexOf(section, "_enableRecording")
                  && IndexOf(section, "_enableRecording") < IndexOf(section, "_enableReplay")
                  && IndexOf(section, "_enableReplay") < IndexOf(section, "_mcapReplayPreflight.Draw"),
                "70D-5: MCAP workflow order is Playback Control -> Recording -> Replay -> Preflight");
            Check(source.Contains("_mcapExpanded"),
                "70D-6: MCAP Record & Replay has a dedicated foldout state");
            Check(IndexOf(section, "_enableReplay") < IndexOf(section, "_replayAutoPlay")
                  && IndexOf(section, "_replayAutoPlay") < IndexOf(section, "_disableLivePublishers")
                  && IndexOf(section, "_disableLivePublishers") < IndexOf(section, "_replayFilePath")
                  && IndexOf(section, "_replayFilePath") < IndexOf(section, "_mcapReplayPreflight.Draw"),
                "70D-7: replay workflow order is Enable -> Auto Play -> Disable Live Publishers -> File Path -> Preflight");
            Check(section.Contains("DrawStackedPathBrowse(replayPath"),
                "70D-8: replay file path uses stacked two-control browse layout");
        }

        private static void VerifyPreflightModuleBoundary()
        {
            var managerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs");
            var drawerSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/McapReplayPreflightDrawer.cs");
            Check(drawerSource.Contains("internal sealed class McapReplayPreflightDrawer"),
                "70E-1: MCAP preflight drawer remains separate");
            Check(managerSource.Contains("_mcapReplayPreflight.Draw(serializedObject, target, replayPath)"),
                "70E-2: manager Inspector delegates preflight drawing");
            Check(!managerSource.Contains("private void AnalyzeReplayMcap")
                  && !managerSource.Contains("private static bool FindLatestReadableRecording"),
                "70E-3: manager Inspector does not own MCAP preflight IO");
        }

        private static void VerifyLayoutHelperExists()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerInspectorLayout.cs");
            Check(source.Contains("internal static class FoxgloveManagerInspectorLayout"),
                "70F-1: Manager Inspector layout helper exists");
            Check(source.Contains("WorkflowSection"),
                "70F-2: layout helper exposes workflow section helper");
            Check(source.Contains("Subheader"),
                "70F-3: layout helper exposes subheader helper");
        }

        private static int IndexOf(string text, string pattern)
        {
            return text.IndexOf(pattern, StringComparison.Ordinal);
        }

        private static void CheckOrdered(string text, string before, string after, string name)
        {
            Check(IndexOf(text, before) >= 0 && IndexOf(text, after) >= 0 && IndexOf(text, before) < IndexOf(text, after), name);
        }

        private static int CountOccurrences(string text, string pattern)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(pattern, index, StringComparison.Ordinal);
                if (index < 0)
                    return count;

                count++;
                index += pattern.Length;
            }
        }

        private static string Slice(string text, string start, string end)
        {
            var startIndex = text.IndexOf(start, StringComparison.Ordinal);
            if (startIndex < 0)
                return string.Empty;

            var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
            return endIndex < 0
                ? text.Substring(startIndex)
                : text.Substring(startIndex, endIndex - startIndex);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new Exception(name);

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
