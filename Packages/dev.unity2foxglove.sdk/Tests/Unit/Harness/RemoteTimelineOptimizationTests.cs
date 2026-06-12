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

            Assert.Contains("const MIN_INTERVAL_MS = 1000 / DEFAULT_MAX_HZ;", source, StringComparison.Ordinal);
            Assert.Contains("function buildPanelDom", source, StringComparison.Ordinal);
            Assert.Contains("panel.replayTime.textContent", render, StringComparison.Ordinal);
            Assert.DoesNotContain("replaceChildren", render, StringComparison.Ordinal);
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
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
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
