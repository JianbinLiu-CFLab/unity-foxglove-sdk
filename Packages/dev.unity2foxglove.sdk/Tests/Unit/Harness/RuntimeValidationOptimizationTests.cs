// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Tests;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-89")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2BridgeSchemaOptimizationTests
    {
        [Fact]
        public void Phase90CachesStableRelativePathsBeforeHashing()
        {
            var method = TestSources.Slice(TestSources.Runtime("Phase90Validation.cs"), "private static string ComputeSourceTreeSha256", "        private static string ToStableRelativePath");

            Assert.Contains("Select(path => new SourceFilePath(path, ToStableRelativePath(sourceRoot, path)))", method, StringComparison.Ordinal);
            Assert.Contains("OrderBy(file => file.RelativePath", method, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetBytes(file.RelativePath)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase91ReusesPointCloudFrameAcrossBuilderChecks()
        {
            var source = TestSources.Runtime("Phase91Validation.cs");
            var verifyMethod = TestSources.Slice(source, "private static void VerifyMessageBuilders", "        private static void VerifyFrameTransformBuilder");

            Assert.Contains("var pointFrame = BuildPointCloudFrame();", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyCompressedPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("private static void VerifyPointCloudSharedPacking(PointCloudFrame frame)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase100DelegatesMethodExtractionToSharedRoslynParser()
        {
            var source = TestSources.Runtime("Phase100Validation.cs");
            var method = TestSources.Slice(source, "private static string ExtractMethodBody", "        private static string ReadRepoText");

            Assert.Contains("PhaseValidationSourceHelpers.SourceMethod(source, methodName)", method, StringComparison.Ordinal);
            Assert.DoesNotContain("FindMethodSignature", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MethodSignatureRegexes", source, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Phase121Validation.cs", "ReadRepoText")]
        [InlineData("Phase128Validation.cs", "ReadRepoText")]
        [InlineData("Phase129Validation.cs", "ReadRepoText")]
        [InlineData("Phase130Validation.cs", "ReadRepoText")]
        [InlineData("Phase131Validation.cs", "ReadRepoText")]
        [InlineData("Phase132Validation.cs", "ReadRepoText")]
        [InlineData("Phase134_30Validation.cs", "ReadRepoText")]
        [InlineData("Phase134_31Validation.cs", "ReadRepoText")]
        [InlineData("Phase164_42Validation.cs", "Read")]
        [InlineData("Phase163_25Validation.cs", "ReadRepoText")]
        [InlineData("Phase163_44Validation.cs", "Read")]
        [InlineData("Phase105Validation.cs", "ReadRepoText")]
        public void RuntimeReadersDelegateToSplitAwareHelper(string filename, string readerName)
        {
            var source = CSharpSyntaxTree.ParseText(TestSources.Runtime(filename));
            var method = Assert.Single(
                source.GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>(),
                candidate => candidate.Identifier.ValueText == readerName);
            var invocation = Assert.IsType<InvocationExpressionSyntax>(method.ExpressionBody?.Expression);

            Assert.Equal(
                "PhaseValidationSourceHelpers.ReadRequiredRepoText",
                invocation.Expression.ToString());
            var argument = Assert.Single(invocation.ArgumentList.Arguments);
            Assert.Equal("relativePath", argument.Expression.ToString());
        }

        [Fact]
        public void FoxgloveLogHubSectionsStayWithinSourceSizeBudget()
        {
            var files = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/DeferredSourceChanges.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/LifecycleAndRegistration.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/PublishingAndEncoding.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/TransportSelection.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Decomposed/FoxgloveLogHub/ScanningAndDiagnostics.cs",
            };

            foreach (var file in files)
            {
                var lineCount = TestSources.Text(file).Split('\n').Length;
                Assert.True(lineCount <= 500, file + " has " + lineCount + " lines.");
            }
        }

        [Fact]
        public void RegisteredCSharpSourceEnumerationIsRecursive()
        {
            var source = TestSources.Runtime("PhaseValidationSourceHelpers.cs");
            var method = TestSources.Slice(
                source,
                "public static string ReadRegisteredCSharpSource",
                "        public static string ReadSplitPythonSource");

            Assert.Contains("SearchOption.AllDirectories", method, StringComparison.Ordinal);
        }

        [Fact]
        public void MediaFoundationSourceReaderRejectsMissingFragment()
        {
            var fixtureRoot = TestSources.CreateMediaFoundationFixture();
            try
            {
                File.Delete(Path.Combine(
                    fixtureRoot,
                    "Packages",
                    "dev.unity2foxglove.sdk",
                    "Runtime",
                    "Schemas",
                    "Proto",
                    "Video",
                    "MediaFoundationH264EncoderSidecar.NativeLifecycle.cs"));

                var error = Assert.Throws<InvalidDataException>(() =>
                {
                    PhaseValidationSourceHelpers.ReadMediaFoundationH264EncoderSidecarSources(fixtureRoot);
                });
                Assert.Contains("Missing:", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        [Fact]
        public void MediaFoundationSourceReaderRejectsUnregisteredFragment()
        {
            var fixtureRoot = TestSources.CreateMediaFoundationFixture();
            try
            {
                var directory = Path.Combine(
                    fixtureRoot,
                    "Packages",
                    "dev.unity2foxglove.sdk",
                    "Runtime",
                    "Schemas",
                    "Proto",
                    "Video");
                File.WriteAllText(
                    Path.Combine(directory, "MediaFoundationH264EncoderSidecar.Diagnostics.cs"),
                    "namespace Fixture { public partial class MediaFoundationH264EncoderSidecar { } }");

                var error = Assert.Throws<InvalidDataException>(() =>
                {
                    PhaseValidationSourceHelpers.ReadMediaFoundationH264EncoderSidecarSources(fixtureRoot);
                });
                Assert.Contains("unexpected:", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        [Fact]
        public void IncludedCppSourceReaderRejectsMissingFragment()
        {
            var fixtureRoot = TestSources.CreateBridgeSmokeCppFixture();
            try
            {
                File.Delete(Path.Combine(
                    fixtureRoot,
                    "Tools",
                    "ros2_bridge",
                    "unity2foxglove_ros2_bridge",
                    "test",
                    "test_bridge_smoke",
                    "TransportIntegrationTests.inl"));

                var error = Assert.Throws<InvalidDataException>(() =>
                {
                    PhaseValidationSourceHelpers.ReadIncludedCppSource(
                        fixtureRoot,
                        "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_bridge_smoke.cpp");
                });
                Assert.Contains("Missing:", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        [Fact]
        public void IncludedCppSourceReaderRejectsUnregisteredFragment()
        {
            var fixtureRoot = TestSources.CreateBridgeSmokeCppFixture();
            try
            {
                var directory = Path.Combine(
                    fixtureRoot,
                    "Tools",
                    "ros2_bridge",
                    "unity2foxglove_ros2_bridge",
                    "test",
                    "test_bridge_smoke");
                File.WriteAllText(Path.Combine(directory, "Diagnostics.inl"), "int diagnostics = 0;\n");

                var error = Assert.Throws<InvalidDataException>(() =>
                {
                    PhaseValidationSourceHelpers.ReadIncludedCppSource(
                        fixtureRoot,
                        "Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_bridge_smoke.cpp");
                });
                Assert.Contains("unexpected:", error.Message, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        [Fact]
        public void Phase14089MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_89Validation.cs", "--phase140-89", "Phase140_89Validation.Validate");

        [Fact]
        public void Phase184AcceptanceSourceManifestPreservesSceneGuidOwnership()
        {
            var root = TestSources.RepoRoot;
            var sectionDirectory = Path.Combine(
                root,
                "Unity2Foxglove",
                "Assets",
                "Scripts",
                "ManualAcceptance",
                "Decomposed",
                "Phase184FoxRunProfileAcceptance");
            var expectedSources = new[]
            {
                "Phase184AcceptanceRoute.cs",
                "Phase184AcceptanceText.cs",
                "Phase184DegradedTargetRoute.cs",
                "Phase184FoxRunProfileAcceptance.Context.cs",
                "Phase184FoxRunProfileAcceptance.Lifecycle.cs",
                "Phase184FoxgloveProfileRoute.cs",
                "Phase184MultiTargetRoute.cs",
                "Phase184QosContractRoute.cs",
                "Phase184StreamRoute.cs",
                "Phase184TransportClientMarkerState.cs",
            };
            var actualSources = Directory.GetFiles(sectionDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(sectionDirectory, path).Replace('\\', '/'))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(expectedSources.OrderBy(name => name, StringComparer.Ordinal), actualSources);

            var scene = File.ReadAllText(Path.Combine(
                root,
                "Unity2Foxglove",
                "Assets",
                "Scenes",
                "ManualAcceptance",
                "Phase184FoxRunProfileAcceptance.unity"));
            var expectedOwnership = new Dictionary<string, (string Stub, string Component)>(StringComparer.Ordinal)
            {
                ["983acb559504477ebd0c4d69a7d1edbe"] = ("Phase184FoxgloveProfileRoute.cs", "Unity2Foxglove.ManualAcceptance.Phase184FoxgloveProfileRoute"),
                ["7b052fef51264defb3b5934d0271da7a"] = ("Phase184MultiTargetRoute.cs", "Unity2Foxglove.ManualAcceptance.Phase184MultiTargetRoute"),
                ["7f1320889ffd4aae8580cf5507278c6a"] = ("Phase184DegradedTargetRoute.cs", "Unity2Foxglove.ManualAcceptance.Phase184DegradedTargetRoute"),
                ["ae2bf84a4ef244ccb4185841a415279b"] = ("Phase184QosContractRoute.cs", "Unity2Foxglove.ManualAcceptance.Phase184QosContractRoute"),
                ["f08839578006415a9d94a9ce4ef663a9"] = ("Phase184StreamRoute.cs", "Unity2Foxglove.ManualAcceptance.Phase184StreamRoute"),
            };
            var componentBlocks = Regex.Matches(scene, @"(?ms)^--- !u!114 .*?(?=^--- !u!|\z)");
            var sceneOwnership = componentBlocks
                .Select(block =>
                {
                    var guid = Regex.Match(block.Value, @"m_Script: \{fileID: 11500000, guid: (?<guid>[0-9a-f]{32}), type: 3\}");
                    var component = Regex.Match(block.Value, @"^  m_EditorClassIdentifier: [^:]+::(?<component>[^\r\n]+)$", RegexOptions.Multiline);
                    return (Guid: guid.Success ? guid.Groups["guid"].Value : string.Empty,
                        Component: component.Success ? component.Groups["component"].Value : string.Empty);
                })
                .Where(entry => expectedOwnership.ContainsKey(entry.Guid))
                .ToArray();
            Assert.Equal(expectedOwnership.Count, sceneOwnership.Length);
            Assert.Equal(expectedOwnership.Count, sceneOwnership.Select(entry => entry.Guid).Distinct(StringComparer.Ordinal).Count());
            foreach (var (guid, expected) in expectedOwnership)
            {
                var stubPath = Path.Combine(root, "Unity2Foxglove", "Assets", "Scripts", "ManualAcceptance", expected.Stub);
                var metaPath = stubPath + ".meta";
                Assert.True(File.Exists(stubPath), "Phase184 root stub missing: " + expected.Stub);
                Assert.True(File.Exists(metaPath), "Phase184 root stub meta missing: " + expected.Stub);
                var metaGuid = Regex.Match(File.ReadAllText(metaPath), @"^guid: (?<guid>[0-9a-f]{32})$", RegexOptions.Multiline);
                Assert.True(metaGuid.Success, "Phase184 root stub meta GUID missing: " + expected.Stub);
                Assert.Equal(guid, metaGuid.Groups["guid"].Value);
                var stubType = expected.Component[(expected.Component.LastIndexOf('.') + 1)..];
                var stub = File.ReadAllText(stubPath);
                var syntax = CSharpSyntaxTree.ParseText(stub);
                Assert.Empty(syntax.GetDiagnostics());
                var declarations = syntax.GetRoot()
                    .DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Where(candidate => candidate.Identifier.ValueText == stubType)
                    .ToArray();
                Assert.Single(declarations);
                var declaration = declarations[0];
                Assert.Contains(declaration.Modifiers, modifier => modifier.IsKind(SyntaxKind.PublicKeyword));
                Assert.Contains(declaration.Modifiers, modifier => modifier.IsKind(SyntaxKind.SealedKeyword));
                Assert.Contains(declaration.Modifiers, modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
                var namespaceName = string.Join(
                    ".",
                    declaration.Ancestors()
                        .OfType<BaseNamespaceDeclarationSyntax>()
                        .Select(namespaceDeclaration => namespaceDeclaration.Name.ToString())
                        .Reverse());
                Assert.Equal(expected.Component[..expected.Component.LastIndexOf('.')], namespaceName);
                var ownership = sceneOwnership.Single(entry => entry.Guid == guid);
                Assert.Equal(expected.Component, ownership.Component);
            }
        }

        [Fact]
        public void CursorBridgeReaderRejectsNestedProductionSources()
        {
            using var temporary = new TemporaryDirectory();
            var sourceRoot = Path.Combine(temporary.Path, "Tools", "foxglove-extensions", "unity-cursor-bridge", "src");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "subdir"));
            foreach (var name in new[] { "index.ts", "helpers.ts", "panel.ts", "panel_dom.ts", "index.test.ts" })
                File.WriteAllText(Path.Combine(sourceRoot, name), "export {};");
            File.WriteAllText(Path.Combine(sourceRoot, "subdir", "production.ts"), "export {};");

            var error = Assert.Throws<InvalidDataException>(
                () => PhaseValidationSourceHelpers.ReadCursorBridgeSources(temporary.Path));

            Assert.Contains("source set mismatch", error.Message, StringComparison.Ordinal);
            Assert.Contains("subdir/production.ts", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CursorBridgeReaderRejectsNestedUnknownTestsAndAllowsRootIndexTest()
        {
            using var temporary = new TemporaryDirectory();
            var sourceRoot = Path.Combine(temporary.Path, "Tools", "foxglove-extensions", "unity-cursor-bridge", "src");
            Directory.CreateDirectory(Path.Combine(sourceRoot, "subdir"));
            foreach (var name in new[] { "index.ts", "helpers.ts", "panel.ts", "panel_dom.ts", "index.test.ts" })
                File.WriteAllText(Path.Combine(sourceRoot, name), "export {};");
            File.WriteAllText(Path.Combine(sourceRoot, "subdir", "unexpected.test.ts"), "export {};");

            var error = Assert.Throws<InvalidDataException>(
                () => PhaseValidationSourceHelpers.ReadCursorBridgeSources(temporary.Path));

            Assert.Contains("unknown test files", error.Message, StringComparison.Ordinal);
            Assert.Contains("subdir/unexpected.test.ts", error.Message, StringComparison.Ordinal);
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
        }
    }







internal static partial class TestSources
    {
        private static readonly string CachedRepoRoot = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }

}
