// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the public Data Transport Inspector hierarchy.

using System;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Guards the public FoxgloveManager Inspector hierarchy planned for Phase 180.
    /// </summary>
    public static class DataTransportInspectorValidation
    {
        private const string ManagerEditorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";

        private static int _passed;

        /// <summary>
        /// Validates the public Data Transport grouping without changing Inspector behavior.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 180: Data Transport Inspector Structure ===");
            _passed = 0;

            var mainInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManagerEditorPath);
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var topLevel = PhaseValidationSourceHelpers.SourceMethod(
                mainInspector,
                "public override void OnInspectorGUI()");
            var dataTransport = PhaseValidationSourceHelpers.SourceMethod(
                editorSources,
                "private void DrawDataTransportSection()");

            VerifyTopLevelWorkflow(topLevel);
            VerifyNestedTransportWorkflow(dataTransport);

            Console.WriteLine("Phase 180: " + _passed + " checks passed.");
        }

        private static void VerifyTopLevelWorkflow(string topLevel)
        {
            const string dataTransport = "DrawSection(\"Data Transport\"";
            const string mcap = "DrawSection(\"MCAP Record & Replay\"";

            Check(CountOccurrences(topLevel, dataTransport) == 1,
                "180A-1: Manager Inspector exposes exactly one top-level Data Transport workflow section");
            Check(IsOrdered(topLevel, dataTransport, mcap),
                "180A-2: Data Transport precedes the top-level MCAP Record & Replay workflow");
            Check(!topLevel.Contains("DrawSection(\"Publish Data\"", StringComparison.Ordinal),
                "180A-3: Publish Data is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal),
                "180A-4: Subscribe Data is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"ROS2 Runtime (R2FU)\"", StringComparison.Ordinal)
                  && !topLevel.Contains("DrawSection(\"ROS 2 Native Runtime (R2FU)\"", StringComparison.Ordinal),
                "180A-5: ROS 2 Native Runtime (R2FU) is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"ROS2 Bridge\"", StringComparison.Ordinal),
                "180A-6: ROS2 Bridge is no longer a top-level workflow section");
        }

        private static void VerifyNestedTransportWorkflow(string dataTransport)
        {
            Check(!dataTransport.Contains("MCAP Record & Replay", StringComparison.Ordinal),
                "180B-1: Data Transport contains no MCAP Record & Replay child workflow");
            Check(ContainsPublicHeading(dataTransport, "Publish"),
                "180B-2: Data Transport nests the public Publish workflow");
            Check(ContainsPublicHeading(dataTransport, "Subscribe"),
                "180B-3: Data Transport nests the public Subscribe workflow");
            Check(ContainsConditionalPublicHeading(dataTransport, "ROS 2 Native Runtime (R2FU)"),
                "180B-4: Data Transport conditionally nests ROS 2 Native Runtime (R2FU)");
        }

        private static bool ContainsPublicHeading(string source, string heading)
            => source.Contains("\"" + heading + "\"", StringComparison.Ordinal);

        private static bool ContainsConditionalPublicHeading(string source, string heading)
        {
            var headingIndex = source.IndexOf("\"" + heading + "\"", StringComparison.Ordinal);
            if (headingIndex < 0)
                return false;

            var conditionalIndex = source.LastIndexOf("if (", headingIndex, StringComparison.Ordinal);
            return conditionalIndex >= 0 && headingIndex - conditionalIndex <= 400;
        }

        private static bool IsOrdered(string source, string before, string after)
        {
            var beforeIndex = source.IndexOf(before, StringComparison.Ordinal);
            var afterIndex = source.IndexOf(after, StringComparison.Ordinal);
            return beforeIndex >= 0 && afterIndex >= 0 && beforeIndex < afterIndex;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var start = 0;
            while (true)
            {
                var index = source.IndexOf(value, start, StringComparison.Ordinal);
                if (index < 0)
                    return count;

                count++;
                start = index + value.Length;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
