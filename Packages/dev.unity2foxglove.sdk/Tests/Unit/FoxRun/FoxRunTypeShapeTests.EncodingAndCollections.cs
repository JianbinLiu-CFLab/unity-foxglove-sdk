// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the encoding-neutral recursive FoxRun type-shape contract.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed partial class FoxRunTypeShapeTests
    {
        [Fact]
        public void SharedDescriptorAssemblyExposesOnlyTheEncodingNeutralShape()
        {
            var assembly = typeof(FoxRunGenerationModel).Assembly;

            Assert.NotNull(assembly.GetType("Unity.FoxgloveSDK.Editor.FoxRunTypeShape"));
            Assert.NotNull(assembly.GetType("Unity.FoxgloveSDK.Editor.FoxRunTypeField"));
            Assert.NotNull(assembly.GetType("Unity.FoxgloveSDK.Editor.FoxRunEnumValue"));
            Assert.Null(assembly.GetType("Unity.FoxgloveSDK.Editor.FoxRunProtobufTypeShape"));
            Assert.Null(typeof(FoxRunTypeField).GetProperty("ProtobufFieldNumber"));
            Assert.Null(typeof(FoxRunTypeField).GetProperty("PresenceOnly"));
            Assert.Null(typeof(FoxRunTypeField).GetProperty("PresenceUsesHasValue"));
            Assert.Null(typeof(FoxRunGenerationMember).GetProperty("ProtobufFieldNumber"));
            Assert.NotNull(typeof(FoxRunGenerationMember).GetField("ProtobufMetadata"));
            Assert.Null(typeof(FoxRunManifestField).GetProperty("ProtobufFieldNumber"));
            Assert.NotNull(typeof(FoxRunManifestField).GetProperty("ProtobufMetadata"));
        }

        [Fact]
        public void ReflectionBuilderIsARealCompiledEncodingNeutralSurface()
        {
            var builder = typeof(FoxRunGenerationModel).Assembly.GetType(
                "Unity.FoxgloveSDK.Editor.FoxRunReflectionTypeShapeBuilder");

            Assert.NotNull(builder);
            Assert.NotNull(builder.GetMethod("Build"));
        }

        [Fact]
        public void ReflectionBuilderPreservesRecursiveCollectionAndBinaryIdentity()
        {
            var shape = FoxRunReflectionTypeShapeBuilder.Build(typeof(CollectionPayload));

            Assert.Equal(FoxRunTypeShapeKind.Object, shape.Kind);
            var samples = Assert.Single(shape.Fields, field => field.MemberName == nameof(CollectionPayload.Samples));
            Assert.True(samples.Repeated);
            Assert.Equal(FoxRunCollectionKind.List, samples.RepeatedCollectionKind);
            Assert.Equal(FoxRunTypeShapeKind.Collection, samples.TypeShape.Kind);
            Assert.Equal(FoxRunCollectionKind.List, samples.TypeShape.CollectionKind);
            Assert.Equal(FoxRunTypeShapeKind.Object, samples.TypeShape.ElementShape.Kind);
            Assert.Equal(
                typeof(CollectionSample).FullName!.Replace('+', '.'),
                samples.TypeShape.ElementShape.TypeName);

            var payload = Assert.Single(shape.Fields, field => field.MemberName == nameof(CollectionPayload.Payload));
            Assert.True(payload.Repeated);
            Assert.Equal(FoxRunCollectionKind.Array, payload.RepeatedCollectionKind);
            Assert.Equal(FoxRunTypeShapeKind.Collection, payload.TypeShape.Kind);
            Assert.Equal(FoxRunCollectionKind.Binary, payload.TypeShape.CollectionKind);
            Assert.True(payload.TypeShape.IsBinary);
            Assert.Equal("uint8", payload.TypeShape.ElementShape.CanonicalType);
        }

        public static IEnumerable<object[]> SupportedMessagePackShapes()
        {
            yield return new object[] { typeof(bool) };
            yield return new object[] { typeof(sbyte) };
            yield return new object[] { typeof(byte) };
            yield return new object[] { typeof(short) };
            yield return new object[] { typeof(ushort) };
            yield return new object[] { typeof(int) };
            yield return new object[] { typeof(uint) };
            yield return new object[] { typeof(long) };
            yield return new object[] { typeof(ulong) };
            yield return new object[] { typeof(float) };
            yield return new object[] { typeof(double) };
            yield return new object[] { typeof(string) };
            yield return new object[] { typeof(SmallEnum) };
            yield return new object[] { typeof(SmallEnum?) };
            yield return new object[] { typeof(byte[]) };
            yield return new object[] { typeof(int[]) };
            yield return new object[] { typeof(List<int>) };
            yield return new object[] { typeof(IList<int>) };
            yield return new object[] { typeof(IReadOnlyList<int>) };
            yield return new object[] { typeof(UnityEngine.Vector2) };
            yield return new object[] { typeof(UnityEngine.Vector3) };
            yield return new object[] { typeof(UnityEngine.Quaternion) };
            yield return new object[] { typeof(UnityEngine.Color) };
            yield return new object[] { typeof(CollectionPayload) };
        }

        [Theory]
        [MemberData(nameof(SupportedMessagePackShapes))]
        public void ReflectionBuilderAcceptsTheLockedMessagePackTypeMatrix(Type type)
        {
            var shape = FoxRunReflectionTypeShapeBuilder.Build(type);

            Assert.NotNull(shape);
        }

        public static IEnumerable<object[]> UnsupportedMessagePackShapes()
        {
            yield return new object[] { typeof(char) };
            yield return new object[] { typeof(decimal) };
            yield return new object[] { typeof(object) };
            yield return new object[] { typeof(DateTime) };
            yield return new object[] { typeof(DateTimeOffset) };
            yield return new object[] { typeof(Guid) };
            yield return new object[] { typeof(TimeSpan) };
            yield return new object[] { typeof(Dictionary<string, int>) };
            yield return new object[] { typeof(HashSet<int>) };
            yield return new object[] { typeof(ICollection<int>) };
            yield return new object[] { typeof(IReadOnlyCollection<int>) };
            yield return new object[] { typeof(Queue<int>) };
            yield return new object[] { typeof(Stack<int>) };
            yield return new object[] { typeof(Collection<int>) };
            yield return new object[] { typeof(ValueTuple) };
            yield return new object[] { typeof((int First, int Second)) };
            yield return new object[] { typeof(Action) };
            yield return new object[] { typeof(IEnumerable<int>) };
            yield return new object[] { typeof(List<>) };
            yield return new object[] { typeof(int[,]) };
            yield return new object[] { typeof(int[][]) };
            yield return new object[] { typeof(List<List<int>>) };
            yield return new object[] { typeof(AbstractPayload) };
            yield return new object[] { typeof(CyclicPayload) };
        }

        [Theory]
        [MemberData(nameof(UnsupportedMessagePackShapes))]
        public void ReflectionBuilderRejectsUnsupportedShapesWithTheStableDiagnostic(Type type)
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(type));

            Assert.StartsWith("FOXRUN616:", error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("System.Char")]
        [InlineData("System.Decimal")]
        [InlineData("System.Object")]
        [InlineData("System.DateTime")]
        [InlineData("global::System.DateTimeOffset")]
        [InlineData("System.Guid")]
        [InlineData("TimeSpan")]
        [Trait("Phase", "185-F")]
        public void ManualUnsupportedScalarObjectShapesFailClosed(
            string typeName)
        {
            var shape = FoxRunTypeShape.Object(
                typeName,
                Array.Empty<FoxRunTypeField>());

            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                    shape,
                    string.Empty));
            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                    shape,
                    string.Empty));
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true)
            });
            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN616");
        }

        public static IEnumerable<object[]> InvalidManualCollectionShapes()
        {
            yield return new object[]
            {
                FoxRunTypeShape.Collection(
                    FoxRunCollectionKind.Array,
                    FoxRunTypeShape.Collection(
                        FoxRunCollectionKind.List,
                        FoxRunTypeShape.Canonical("int32")))
            };
            yield return new object[]
            {
                FoxRunTypeShape.Collection(
                    FoxRunCollectionKind.Binary,
                    FoxRunTypeShape.Canonical("int32"))
            };
            yield return new object[]
            {
                FoxRunTypeShape.Collection(
                    FoxRunCollectionKind.Binary,
                    FoxRunTypeShape.Canonical("uint8", nullable: true))
            };
            yield return new object[]
            {
                FoxRunTypeShape.Collection(
                    FoxRunCollectionKind.Array,
                    FoxRunTypeShape.Canonical("uint8"))
            };
            yield return new object[]
            {
                FoxRunTypeShape.Collection(
                    (FoxRunCollectionKind)99,
                    FoxRunTypeShape.Canonical("int32"))
            };
        }

        [Theory]
        [MemberData(nameof(InvalidManualCollectionShapes))]
        [Trait("Phase", "185-F")]
        public void ManualInvalidCollectionShapesFailClosed(
            FoxRunTypeShape shape)
        {
            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                    shape,
                    string.Empty));
            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                    shape,
                    string.Empty));
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true)
            });
            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN616");
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void ManualShapeDepthUsesTheSameExactBoundaryAsBothBuilders()
        {
            var accepted = ManualObjectChain(FoxServiceDtoRules.MaxDepth);
            var rejected = ManualObjectChain(FoxServiceDtoRules.MaxDepth + 1);

            Assert.True(
                FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                    accepted,
                    string.Empty));
            Assert.True(
                FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                    accepted,
                    string.Empty));
            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                    rejected,
                    string.Empty));
            Assert.False(
                FoxRunMessagePackTypeShapeRules.IsSubscribeSupported(
                    rejected,
                    string.Empty));

            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    rejected,
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true)
            });
            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN616");
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void DuplicateTopLevelJsonNamesFailClosedForMessagePackTopics()
        {
            var shape = FoxRunTypeShape.Canonical("int32");
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_first",
                    jsonFieldName: "same"),
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_second",
                    jsonFieldName: "same")
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN022");
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void OppositeDirectionsMayReuseATopLevelJsonName()
        {
            var shape = FoxRunTypeShape.Canonical("int32");
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_outbound",
                    jsonFieldName: "value"),
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_inbound",
                    jsonFieldName: "value")
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN022");
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void DuplicateSubscribeJsonNamesFailClosedForMessagePackTopics()
        {
            var shape = FoxRunTypeShape.Canonical("int32");
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_first",
                    jsonFieldName: "same"),
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_second",
                    jsonFieldName: "same")
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN022");
        }

        public static IEnumerable<object[]> MessagePackCollectionContractMatrix()
        {
            yield return new object[] { typeof(int[]), "int[]", true };
            yield return new object[] { typeof(List<int>), "List<int>", true };
            yield return new object[] { typeof(IList<int>), "IList<int>", true };
            yield return new object[] { typeof(IReadOnlyList<int>), "IReadOnlyList<int>", true };
            yield return new object[] { typeof(HashSet<int>), "HashSet<int>", false };
            yield return new object[] { typeof(ICollection<int>), "ICollection<int>", false };
            yield return new object[] { typeof(IReadOnlyCollection<int>), "IReadOnlyCollection<int>", false };
            yield return new object[] { typeof(Queue<int>), "Queue<int>", false };
            yield return new object[] { typeof(Stack<int>), "Stack<int>", false };
            yield return new object[] { typeof(Collection<int>), "Collection<int>", false };
        }

        [Theory]
        [MemberData(nameof(MessagePackCollectionContractMatrix))]
        public void RoslynAndReflectionBuildersShareTheExactLockedCollectionWhitelist(
            Type reflectionType,
            string sourceType,
            bool supported)
        {
            var compilation = CSharpCompilation.Create(
                "Phase185CollectionWhitelist_" + sourceType.GetHashCode(),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "using System.Collections.Generic;"
                        + "using System.Collections.ObjectModel;"
                        + "namespace Demo { public sealed class Payload { public "
                        + sourceType
                        + " Value { get; set; } } }")
                },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var property = compilation.GetTypeByMetadataName("Demo.Payload")
                ?.GetMembers("Value")
                .OfType<IPropertySymbol>()
                .Single();

            Assert.NotNull(property);
            if (supported)
            {
                Assert.NotNull(FoxRunReflectionTypeShapeBuilder.Build(reflectionType));
                Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(property.Type, out var roslyn));
                Assert.NotNull(roslyn);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(
                    () => FoxRunReflectionTypeShapeBuilder.Build(reflectionType));
                Assert.False(FoxRunRoslynTypeShapeBuilder.TryBuild(property.Type, out _));
            }
        }
    }
}
