// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Cover reviewed Editor schema and sample edge cases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "187")]
    [Trait("Domain", "Editor schema and samples")]
    public sealed class EditorSchemaAndSampleReviewTests
    {
        [Fact]
        public void ReflectionSchemaPreservesInterfaceCollectionShape()
        {
            AssertArray(FoxServiceSchemaReflectionBuilder.Build(
                typeof(IList<int>), FoxServiceDtoRules.RequestSide));
            AssertArray(FoxServiceSchemaReflectionBuilder.Build(
                typeof(IReadOnlyList<int>), FoxServiceDtoRules.ResponseSide));
            AssertDictionary(FoxServiceSchemaReflectionBuilder.Build(
                typeof(IDictionary<string, int>), FoxServiceDtoRules.RequestSide));
            AssertDictionary(FoxServiceSchemaReflectionBuilder.Build(
                typeof(IReadOnlyDictionary<string, int>), FoxServiceDtoRules.ResponseSide));
        }

        [Fact]
        public void RoslynSchemaPreservesInterfaceCollectionShape()
        {
            const string source = @"
namespace Probe
{
    public sealed class Dto
    {
        public System.Collections.Generic.IList<int> MutableList;
        public System.Collections.Generic.IReadOnlyList<int> ReadOnlyList;
        public System.Collections.Generic.IDictionary<string, int> MutableMap;
        public System.Collections.Generic.IReadOnlyDictionary<string, int> ReadOnlyMap;
    }
}";
            var compilation = CSharpCompilation.Create(
                "SchemaProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences());
            var dto = compilation.GetTypeByMetadataName("Probe.Dto");
            Assert.NotNull(dto);

            AssertArray(BuildRoslyn(dto, "MutableList", FoxServiceDtoRules.RequestSide));
            AssertArray(BuildRoslyn(dto, "ReadOnlyList", FoxServiceDtoRules.ResponseSide));
            AssertDictionary(BuildRoslyn(dto, "MutableMap", FoxServiceDtoRules.RequestSide));
            AssertDictionary(BuildRoslyn(dto, "ReadOnlyMap", FoxServiceDtoRules.ResponseSide));
        }

        [Fact]
        public void GeneratedServiceDescriptorCacheUsesSafePublication()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceSourceEmitter/FoxServiceSourceEmitter.cs");

            Assert.Contains(
                "private volatile global::System.Collections.Generic.IReadOnlyList<global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor> __foxgloveServices;",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        public void MazeSceneBuildersEnumerateInactiveSceneRoots()
        {
            var paths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs",
            };

            foreach (var path in paths)
            {
                var source = TestSources.Text(path);
                Assert.DoesNotContain("GameObject.Find(rootName)", source, StringComparison.Ordinal);
                Assert.Contains("GetRootGameObjects()", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void FullDemoPayloadPreviewBacksOffUtf8ContinuationByte()
        {
            var live = TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var package = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");

            Assert.Equal(live, package);
            Assert.Contains(
                "while (count > 0 && (payload[count] & 0xC0) == 0x80)",
                live,
                StringComparison.Ordinal);
        }

        [Fact]
        public void ManifestFirstPublishMoveParticipatesInRetryBoundary()
        {
            var root = CSharpSyntaxTree.ParseText(TestSources.Text(
                    "Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs"))
                .GetCompilationUnitRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(candidate => candidate.Identifier.ValueText == "ReplaceFile");
            var firstMove = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .First(invocation => invocation.Expression.ToString() == "File.Move");

            Assert.Contains(firstMove.Ancestors(), ancestor => ancestor is TryStatementSyntax);
        }

        [Fact]
        public void ManifestSectionHashWritersRejectNullSectionsByName()
        {
            Assert.Equal("section", Assert.Throws<ArgumentNullException>(
                () => Unity2FoxgloveSchemaManifestJsonWriter.WriteFoxRunSectionHashInput(null)).ParamName);
            Assert.Equal("section", Assert.Throws<ArgumentNullException>(
                () => Unity2FoxgloveSchemaManifestJsonWriter.WriteProtobufRegistrySectionHashInput(null)).ParamName);
            Assert.Equal("section", Assert.Throws<ArgumentNullException>(
                () => Unity2FoxgloveSchemaManifestJsonWriter.WriteSdkTypedPublishersSectionHashInput(null)).ParamName);
        }

        private static FoxServiceSchemaModel BuildRoslyn(
            INamedTypeSymbol dto,
            string fieldName,
            string side)
        {
            var field = dto.GetMembers(fieldName).OfType<IFieldSymbol>().Single();
            return FoxServiceRoslynSchemaBuilder.Build(field.Type, side, 0);
        }

        private static void AssertArray(FoxServiceSchemaModel model)
        {
            Assert.Equal("array", model.JsonType);
            Assert.NotNull(model.Element);
            Assert.Equal("integer", model.Element.JsonType);
        }

        private static void AssertDictionary(FoxServiceSchemaModel model)
        {
            Assert.Equal("object", model.JsonType);
            Assert.NotNull(model.AdditionalProperties);
            Assert.Equal("integer", model.AdditionalProperties.JsonType);
        }

        private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        {
            var trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.False(string.IsNullOrEmpty(trusted));
            return trusted.Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
