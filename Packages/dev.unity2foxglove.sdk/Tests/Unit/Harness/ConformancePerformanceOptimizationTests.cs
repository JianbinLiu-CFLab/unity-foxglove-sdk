// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-96 performance and conformance source-shape checks.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-96")]
    [Trait("Domain", "Harness")]
    public sealed class ConformancePerformanceOptimizationTests
    {
        [Fact]
        public void RuntimeRegressionPerformanceConformanceScopeRemainsEmpty()
        {
            var runtimeRoot = Path.Combine(RepoRoot, "Packages", "dev.unity2foxglove.sdk", "Tests", "Runtime");

            Assert.Empty(Directory.GetFiles(runtimeRoot, "*Regression*.cs", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(runtimeRoot, "*Performance*.cs", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(runtimeRoot, "*Conformance*.cs", SearchOption.AllDirectories));
        }

        [Fact]
        public void McapConformanceReaderStreamsAndScansFromOneFileRead()
        {
            var method = Method(
                "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceReader.cs",
                "ReadStreamed",
                parameterName: "filePath").ToFullString();

            Assert.Contains("var data = File.ReadAllBytes(filePath);", method, StringComparison.Ordinal);
            Assert.Contains("new MemoryStream(data", method, StringComparison.Ordinal);
            Assert.Contains("new Scanner(data)", method, StringComparison.Ordinal);
            Assert.Equal(1, Count(method, "File.ReadAllBytes(filePath)"));
            Assert.DoesNotContain("File.OpenRead(filePath)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void McapConformanceJsonRecordSortsWithoutLinqAllocations()
        {
            var method = Method("Packages/dev.unity2foxglove.sdk/Tests/McapConformance/McapConformanceJson.cs", "Record").ToFullString();

            Assert.Contains("Array.Sort(fields", method, StringComparison.Ordinal);
            Assert.Contains("new List<object[]>(fields.Length)", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".OrderBy(", method, StringComparison.Ordinal);
            Assert.DoesNotContain(".Select(", method, StringComparison.Ordinal);
        }

        [Fact]
        public void PerformancePayloadsKeepPerMessageSequenceData()
        {
            var payloadMethod = Method("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs", "MakeJsonPayload").ToFullString();
            var fanoutMethod = Method("Packages/dev.unity2foxglove.sdk/Tests/Performance/PerformanceRunner.cs", "RunPublishJsonFanout").ToFullString();

            Assert.Contains("seq = msgIdx", payloadMethod, StringComparison.Ordinal);
            Assert.Contains("MakeJsonPayload(t, i)", fanoutMethod, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14096MigratedConsolePhaseIsRemoved()
        {
            var registry = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"--phase140-96\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain("Phase140_96Validation.Validate", registry, StringComparison.Ordinal);

            var project = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Assert.DoesNotContain("Phase140_96Validation.cs", project, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax Method(string relativePath, string methodName, string parameterName = null)
        {
            return CSharpSyntaxTree.ParseText(Text(relativePath))
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == methodName
                                  && (parameterName == null
                                      || method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == parameterName)));
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
