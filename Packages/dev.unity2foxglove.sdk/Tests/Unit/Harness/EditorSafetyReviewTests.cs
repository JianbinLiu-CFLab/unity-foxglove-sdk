// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Keep reviewed Editor process and path boundaries fail-closed.

using System;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Editor safety")]
    public sealed class EditorSafetyReviewTests
    {
        [Fact]
        public void TimedOutOpenH264ProbeWaitsForKilledProcessBeforeDrainingPipes()
        {
            var method = Method(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264ExecutableCheck.cs",
                "Check");
            var source = method.ToFullString();
            var kill = source.IndexOf("TryKill(process);", StringComparison.Ordinal);
            var wait = source.IndexOf("process.WaitForExit(500);", StringComparison.Ordinal);
            var drain = source.IndexOf("WaitForStreamDrain(stdoutTask, stderrTask, 500)", StringComparison.Ordinal);

            Assert.True(kill >= 0);
            Assert.True(wait > kill);
            Assert.True(drain > wait);
        }

        [Fact]
        public void OpenH264InstallerBuildsBothTemporaryArtifactsBeforePublishingEither()
        {
            var method = Method(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs",
                "Install");
            var source = method.ToFullString();
            var build = source.IndexOf("BuildHelperExecutable(versionDir, tempHelper", StringComparison.Ordinal);
            var dllCommit = source.IndexOf("File.Move(tempDll, finalDllPath)", StringComparison.Ordinal);
            var helperCommit = source.IndexOf("File.Move(tempHelper, finalHelperPath)", StringComparison.Ordinal);

            Assert.True(build >= 0);
            Assert.True(dllCommit > build);
            Assert.True(helperCommit > build);
        }

        [Fact]
        public void OpenH264BatchPathsEscapePercentExpansion()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs");

            Assert.Contains("Replace(\"%\", \"%%\")", source, StringComparison.Ordinal);
        }

        [Fact]
        public void SchemaEvidenceContainmentUsesHostFilesystemCaseRules()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs");
            var sameOrChild = MethodFromSource(source, "IsSameOrChildPath").ToFullString();
            var equal = MethodFromSource(source, "PathsEqual").ToFullString();

            Assert.Contains("PathComparison", source, StringComparison.Ordinal);
            Assert.Contains("PathComparison", sameOrChild, StringComparison.Ordinal);
            Assert.Contains("PathComparison", equal, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax Method(string path, string name)
            => MethodFromSource(TestSources.Text(path), name);

        private static MethodDeclarationSyntax MethodFromSource(string source, string name)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (method.Identifier.ValueText == name && method.Body != null)
                    return method;
            }

            throw new InvalidOperationException("Method not found: " + name);
        }
    }
}
