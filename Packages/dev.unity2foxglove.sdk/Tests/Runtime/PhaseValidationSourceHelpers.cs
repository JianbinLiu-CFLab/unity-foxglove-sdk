// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationSourceHelpers
    {
        public static string FindRequiredRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root for source validation.");
            return root;
        }

        public static string RepoPath(string relativePath)
        {
            var root = FindRequiredRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return path;
        }

        public static string ReadRequiredRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        public static string ReadCameraPublisherSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Camera publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadReplayControllerSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Core",
                "Replay");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Replay controller directory was not found.");

            var files = Directory.GetFiles(dir, "ReplayController*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadMcapRecorderSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "IO",
                "Mcap",
                "Recording");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("MCAP recorder directory was not found.");

            var files = Directory.GetFiles(dir, "McapRecorder*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static string ReadFoxgloveLogSourceGeneratorSources()
        {
            var root = FindRequiredRepoRoot();

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Source generator src directory was not found.");

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            var source = new StringBuilder();
            foreach (var file in files)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(file));
            }

            return source.ToString();
        }

        public static bool SourceMethodContains(string source, string methodName, string needle)
            => SourceMethod(source, methodName).Contains(needle, StringComparison.Ordinal);

        public static string SourceMethod(string source, string methodName)
        {
            var start = source.IndexOf(methodName, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;

            var braceStart = FindNextCodeChar(source, start, '{');
            if (braceStart < 0)
                return string.Empty;

            var depth = 0;
            var state = SourceScanState.Code;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (!TryAdvanceSourceScanState(source, ref i, ref state))
                    continue;

                if (state != SourceScanState.Code)
                    continue;

                var current = source[i];
                if (current == '{')
                {
                    depth++;
                    continue;
                }

                if (current != '}')
                    continue;

                depth--;
                if (depth == 0)
                    return source.Substring(start, i - start + 1);
            }

            return string.Empty;
        }

        private static int FindNextCodeChar(string source, int start, char target)
        {
            var state = SourceScanState.Code;
            for (var i = start; i < source.Length; i++)
            {
                if (!TryAdvanceSourceScanState(source, ref i, ref state))
                    continue;

                if (state == SourceScanState.Code && source[i] == target)
                    return i;
            }

            return -1;
        }

        private static bool TryAdvanceSourceScanState(string source, ref int index, ref SourceScanState state)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            switch (state)
            {
                case SourceScanState.LineComment:
                    if (current == '\n' || current == '\r')
                        state = SourceScanState.Code;
                    return false;

                case SourceScanState.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        index++;
                        state = SourceScanState.Code;
                    }
                    return false;

                case SourceScanState.String:
                    if (current == '\\')
                    {
                        index++;
                        return false;
                    }

                    if (current == '"')
                        state = SourceScanState.Code;
                    return false;

                case SourceScanState.VerbatimString:
                    if (current == '"' && next == '"')
                    {
                        index++;
                        return false;
                    }

                    if (current == '"')
                        state = SourceScanState.Code;
                    return false;

                case SourceScanState.Character:
                    if (current == '\\')
                    {
                        index++;
                        return false;
                    }

                    if (current == '\'')
                        state = SourceScanState.Code;
                    return false;
            }

            if (current == '/' && next == '/')
            {
                index++;
                state = SourceScanState.LineComment;
                return false;
            }

            if (current == '/' && next == '*')
            {
                index++;
                state = SourceScanState.BlockComment;
                return false;
            }

            if (current == '@' && next == '"')
            {
                index++;
                state = SourceScanState.VerbatimString;
                return false;
            }

            if (current == '"')
            {
                state = SourceScanState.String;
                return false;
            }

            if (current == '\'')
            {
                state = SourceScanState.Character;
                return false;
            }

            return true;
        }

        private enum SourceScanState
        {
            Code,
            LineComment,
            BlockComment,
            String,
            VerbatimString,
            Character
        }
    }
}
