// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-95 remote timeline validation source-shape checks.

using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-95")]
    [Trait("Domain", "Harness")]
    public sealed class RemoteTimelineOptimizationTests
    {
        [Fact]
        public void Phase139ReadersCacheRepoRootAndSourceReads()
        {
            foreach (var file in new[]
            {
                "Phase139Validation.cs",
                "Phase139BValidation.cs",
                "Phase139CValidation.cs",
                "Phase139DValidation.cs"
            })
            {
                var source = RuntimeText(file);
                Assert.Contains("private static readonly string CachedRepoRoot", source, StringComparison.Ordinal);
                Assert.Contains("private static readonly Dictionary<string, string> SourceCache", source, StringComparison.Ordinal);
                Assert.Contains("SourceCache.TryGetValue", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Phase139DReadsServerSourceThroughOneCachedPath()
        {
            Assert.Equal(1, Count(RuntimeText("Phase139DValidation.cs"), "FoxgloveManager.Server.cs"));
        }

        [Fact]
        public void CursorBridgeKeepsDomConstructionOutOfRenderLoop()
        {
            var source = Text("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var render = ExtractFunction(source, "context.onRender =");

            // Phase 140K Stage 1 promoted the cursor rate to a panel setting. Phase 164 caches the
            // derived interval outside onRender so the render loop only reads the current value.
            Assert.Contains("const DEFAULT_MAX_HZ = 60;", source, StringComparison.Ordinal);
            Assert.Contains("let minIntervalMs = 1000 / state.maxHz;", source, StringComparison.Ordinal);
            Assert.Contains("minIntervalMs = 1000 / state.maxHz;", source, StringComparison.Ordinal);
            Assert.Contains("shouldSendCursor(state.enabled, currentTime, lastCursorSec, lastCursorNsec, lastSentAtMs, nowMs, minIntervalMs)", render, StringComparison.Ordinal);
            Assert.DoesNotContain("1000 / state.maxHz", render, StringComparison.Ordinal);
            Assert.Contains("function buildPanelDom", source, StringComparison.Ordinal);
            Assert.Contains("panel.replayTime.textContent", render, StringComparison.Ordinal);
            Assert.DoesNotContain("replaceChildren", render, StringComparison.Ordinal);
        }

        [Fact]
        public void ExtractFunctionIgnoresLiteralBraces()
        {
            const string source = @"
context.onRender = () => {
  const label = ""{literal}"";
  const format = ""value {0}"";
  keepMe();
};
function later() { dropMe(); }";

            var render = ExtractFunction(source, "context.onRender =");

            Assert.Contains("keepMe()", render, StringComparison.Ordinal);
            Assert.DoesNotContain("dropMe()", render, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14095MigratedConsolePhaseIsRemoved()
        {
            var registry = RuntimeText("PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"--phase140-95\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("Phase140_95Validation.Validate", registry, StringComparison.Ordinal);

            var project = RuntimeText("FoxgloveSdk.Tests.csproj");
            Assert.DoesNotContain("Phase140_95Validation.cs", project, StringComparison.Ordinal);
        }

        private static string RuntimeText(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string ExtractFunction(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return string.Empty;

            var brace = source.IndexOf('{', index);
            if (brace < 0)
                return string.Empty;

            var depth = 0;
            var inString = false;
            var inChar = false;
            var inLineComment = false;
            var inBlockComment = false;
            var templateDepth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                var ch = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (ch == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (ch == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (inString)
                {
                    if (ch == '"' && !IsEscaped(source, i))
                        inString = false;
                    continue;
                }

                if (inChar)
                {
                    if (ch == '\'' && !IsEscaped(source, i))
                        inChar = false;
                    continue;
                }

                if (templateDepth > 0)
                {
                    if (ch == '`' && !IsEscaped(source, i))
                    {
                        templateDepth = 0;
                        continue;
                    }
                }

                if (ch == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = true;
                    continue;
                }

                if (ch == '`')
                {
                    templateDepth = 1;
                    continue;
                }

                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        private static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
