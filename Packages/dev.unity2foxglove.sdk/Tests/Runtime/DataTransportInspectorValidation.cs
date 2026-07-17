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
            Check(HasSingleDataTransportWorkflowBeforeSiblingMcap(topLevel),
                "180A-1: Manager Inspector directly wires one Data Transport workflow before sibling MCAP Record & Replay");
            Check(!topLevel.Contains("DrawSection(\"Publish Data\"", StringComparison.Ordinal),
                "180A-2: Publish Data is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"Subscribe Data\"", StringComparison.Ordinal),
                "180A-3: Subscribe Data is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"ROS2 Runtime (R2FU)\"", StringComparison.Ordinal)
                  && !topLevel.Contains("DrawSection(\"ROS 2 Native Runtime (R2FU)\"", StringComparison.Ordinal),
                "180A-4: ROS 2 Native Runtime (R2FU) is no longer a top-level workflow section");
            Check(!topLevel.Contains("DrawSection(\"ROS2 Bridge\"", StringComparison.Ordinal),
                "180A-5: ROS2 Bridge is no longer a top-level workflow section");
        }

        private static void VerifyNestedTransportWorkflow(string dataTransport)
        {
            Check(!dataTransport.Contains("MCAP Record & Replay", StringComparison.Ordinal)
                  && !dataTransport.Contains("DrawMcapSection", StringComparison.Ordinal),
                "180B-1: Data Transport contains no MCAP Record & Replay child workflow");
            Check(ContainsPublicHeading(dataTransport, "Publish"),
                "180B-2: Data Transport nests the public Publish workflow");
            Check(ContainsPublicHeading(dataTransport, "Subscribe"),
                "180B-3: Data Transport nests the public Subscribe workflow");
            Check(HasNativeRuntimeSubsectionUnderNativeDemand(dataTransport),
                "180B-4: Data Transport nests ROS 2 Native Runtime (R2FU) only under native demand");
        }

        private static bool ContainsPublicHeading(string source, string heading)
            => source.Contains("\"" + heading + "\"", StringComparison.Ordinal);

        private static bool HasSingleDataTransportWorkflowBeforeSiblingMcap(string source)
        {
            var dataTransport = FindInvocation(source, "DrawSection", "Data Transport", 0, directBodyOnly: true);
            if (dataTransport == null
                || !dataTransport.Text.Contains("DrawDataTransportSection", StringComparison.Ordinal)
                || FindInvocation(source, "DrawSection", "Data Transport", dataTransport.StatementEnd + 1, directBodyOnly: true) != null)
                return false;

            var mcap = FindInvocation(source, "DrawSection", "MCAP Record & Replay", dataTransport.StatementEnd + 1, directBodyOnly: true);
            return mcap != null
                   && mcap.Text.Contains("DrawMcapSection", StringComparison.Ordinal)
                   && FindInvocation(source, "DrawSection", "MCAP Record & Replay", mcap.StatementEnd + 1, directBodyOnly: true) == null;
        }

        private static bool HasNativeRuntimeSubsectionUnderNativeDemand(string source)
        {
            const string heading = "ROS 2 Native Runtime (R2FU)";
            var subsection = FindInvocation(source, "DrawDataTransportSubsection", heading, 0);
            return subsection != null
                   && subsection.Text.Contains("DrawR2fuRuntimeSection", StringComparison.Ordinal)
                   && IsInsideNativeDemandCondition(source, subsection.Start)
                   && FindInvocation(source, "DrawDataTransportSubsection", heading, subsection.StatementEnd + 1) == null;
        }

        private static SourceInvocation FindInvocation(
            string source,
            string methodName,
            string heading,
            int searchStart,
            bool directBodyOnly = false)
        {
            var anchor = methodName + "(\"" + heading + "\"";
            for (var start = source.IndexOf(anchor, searchStart, StringComparison.Ordinal);
                 start >= 0;
                 start = source.IndexOf(anchor, start + 1, StringComparison.Ordinal))
            {
                if (directBodyOnly && !IsDirectBodyInvocation(source, start))
                    continue;

                var invocationEnd = FindMatchingDelimiter(source, start + methodName.Length, '(', ')');
                var statementEnd = invocationEnd < 0 ? -1 : source.IndexOf(';', invocationEnd);
                if (invocationEnd >= 0 && statementEnd >= invocationEnd)
                    return new SourceInvocation(start, statementEnd, source.Substring(start, invocationEnd - start + 1));
            }

            return null;
        }

        private static bool IsDirectBodyInvocation(string source, int start)
        {
            var bodyStart = source.IndexOf('{');
            if (bodyStart < 0 || start <= bodyStart)
                return false;

            var braces = 0;
            var parentheses = 0;
            for (var i = bodyStart; i < start; i++)
            {
                if (source[i] == '{')
                    braces++;
                else if (source[i] == '}')
                    braces--;
                else if (source[i] == '(')
                    parentheses++;
                else if (source[i] == ')')
                    parentheses--;
            }

            return braces == 1 && parentheses == 0;
        }

        private static bool IsInsideNativeDemandCondition(string source, int targetStart)
        {
            const string condition = "if (HasR2fuNativeRuntimeDemand())";
            var conditionStart = source.IndexOf(condition, StringComparison.Ordinal);
            if (conditionStart < 0)
                return false;

            var bodyStart = SkipWhitespace(source, conditionStart + condition.Length);
            var bodyEnd = bodyStart < 0
                ? -1
                : source[bodyStart] == '{'
                    ? FindMatchingDelimiter(source, bodyStart, '{', '}')
                    : source.IndexOf(';', bodyStart);
            return bodyEnd >= bodyStart && targetStart >= bodyStart && targetStart <= bodyEnd;
        }

        private static int FindMatchingDelimiter(string source, int openIndex, char open, char close)
        {
            var depth = 0;
            for (var i = openIndex; i < source.Length; i++)
            {
                if (source[i] == open)
                    depth++;
                else if (source[i] == close && --depth == 0)
                    return i;
            }

            return -1;
        }

        private static int SkipWhitespace(string source, int start)
        {
            for (var i = start; i < source.Length; i++)
            {
                if (!char.IsWhiteSpace(source[i]))
                    return i;
            }

            return -1;
        }

        private sealed class SourceInvocation
        {
            public SourceInvocation(int start, int statementEnd, string text)
            {
                Start = start;
                StatementEnd = statementEnd;
                Text = text;
            }

            public int Start { get; }

            public int StatementEnd { get; }

            public string Text { get; }
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
