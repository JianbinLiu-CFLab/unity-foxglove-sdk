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
    [Trait("Phase", "185-A")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunTypeShapeTests
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
            yield return new object[] { typeof(object) };
            yield return new object[] { typeof(Dictionary<string, int>) };
            yield return new object[] { typeof(HashSet<int>) };
            yield return new object[] { typeof(ICollection<int>) };
            yield return new object[] { typeof(IReadOnlyCollection<int>) };
            yield return new object[] { typeof(Queue<int>) };
            yield return new object[] { typeof(Stack<int>) };
            yield return new object[] { typeof(Collection<int>) };
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

        [Fact]
        public void UnityValueTypesUseStableComponentObjectShapesAcrossBothHosts()
        {
            const string source = @"
namespace UnityEngine
{
    public struct Vector2 { public float x; public float y; }
    public struct Vector3 { public float x; public float y; public float z; }
    public struct Quaternion { public float x; public float y; public float z; public float w; }
    public struct Color { public float r; public float g; public float b; public float a; }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185UnityShapeParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var cases = new[]
            {
                (typeof(UnityEngine.Vector2), "UnityEngine.Vector2", new[] { "x", "y" }),
                (typeof(UnityEngine.Vector3), "UnityEngine.Vector3", new[] { "x", "y", "z" }),
                (typeof(UnityEngine.Quaternion), "UnityEngine.Quaternion", new[] { "x", "y", "z", "w" }),
                (typeof(UnityEngine.Color), "UnityEngine.Color", new[] { "r", "g", "b", "a" })
            };

            foreach (var (reflectionType, metadataName, components) in cases)
            {
                var reflection = FoxRunReflectionTypeShapeBuilder.Build(reflectionType);
                var symbol = compilation.GetTypeByMetadataName(metadataName);

                Assert.NotNull(symbol);
                Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
                AssertComponentShape(reflection, metadataName, components);
                AssertComponentShape(roslyn, metadataName, components);
                Assert.Equal(
                    reflection.Fields.Select(FieldIdentity),
                    roslyn.Fields.Select(FieldIdentity));
            }
        }

        [Fact]
        public void ObjectShapeFieldOrderIsCanonicalAcrossNestedHostDiscoveryOrder()
        {
            var firstNested = FoxRunTypeShape.Object(
                "Demo.Nested",
                new[]
                {
                    new FoxRunTypeField("second", "Second", FoxRunTypeShape.Canonical("float32")),
                    new FoxRunTypeField("first", "First", FoxRunTypeShape.Canonical("int32"))
                });
            var secondNested = FoxRunTypeShape.Object(
                "Demo.Nested",
                new[]
                {
                    new FoxRunTypeField("first", "First", FoxRunTypeShape.Canonical("int32")),
                    new FoxRunTypeField("second", "Second", FoxRunTypeShape.Canonical("float32"))
                });
            var first = FoxRunTypeShape.Object(
                "Demo.Root",
                new[]
                {
                    new FoxRunTypeField("zeta", "Zeta", FoxRunTypeShape.Canonical("string")),
                    new FoxRunTypeField("alpha", "Alpha", firstNested)
                });
            var second = FoxRunTypeShape.Object(
                "Demo.Root",
                new[]
                {
                    new FoxRunTypeField("alpha", "Alpha", secondNested),
                    new FoxRunTypeField("zeta", "Zeta", FoxRunTypeShape.Canonical("string"))
                });

            Assert.Equal(new[] { "alpha", "zeta" }, first.Fields.Select(field => field.JsonName));
            Assert.Equal(
                new[] { "first", "second" },
                first.Fields[0].TypeShape.Fields.Select(field => field.JsonName));
            Assert.Equal(ShapeIdentity(first), ShapeIdentity(second));

            var firstModel = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(first, memberName: "_root")
            });
            var secondModel = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(second, memberName: "_root")
            });

            Assert.Equal(
                FoxRunGenerationDescriptorJsonWriter.Write(firstModel),
                FoxRunGenerationDescriptorJsonWriter.Write(secondModel));
        }

        [Fact]
        public void EnumOutsideSignedInt32FailsWithTheStableMessagePackDiagnostic()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(typeof(WideEnum)));

            Assert.Contains("FOXRUN616", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void EncodingNeutralEnumShapeContainsOnlyDeclaredValuesAcrossBothBuilders()
        {
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(typeof(NoZeroEnum));
            var compilation = CSharpCompilation.Create(
                "Phase185EnumShapeParity",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        "namespace Demo { public enum NoZeroEnum { First = 1, Second = 2 } }")
                },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName("Demo.NoZeroEnum");

            Assert.NotNull(symbol);
            Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
            Assert.Equal(
                new[] { "First:1", "Second:2" },
                reflection.EnumValues.Select(value => value.Name + ":" + value.Number).ToArray());
            Assert.Equal(
                new[] { "First:1", "Second:2" },
                roslyn.EnumValues.Select(value => value.Name + ":" + value.Number).ToArray());
        }

        [Fact]
        public void RoslynAndReflectionBuildersAgreeThatInitOnlyMembersAreNotInboundAssignable()
        {
            const string source = @"
namespace Demo
{
    public sealed class InitOnlyPayload
    {
        public int Value { get; init; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185InitOnlyParity",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.Latest))
                },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName("Demo.InitOnlyPayload");

            Assert.NotNull(symbol);
            Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(typeof(InitOnlyPayload));
            Assert.False(Assert.Single(roslyn.Fields).CanAssign);
            Assert.False(Assert.Single(reflection.Fields).CanAssign);
        }

        private static FoxRunGenerationMember ShapedMember(
            FoxRunTypeShape shape,
            string memberName)
            => new FoxRunGenerationMember(
                "Demo",
                "ShapeOwner",
                memberName,
                "field",
                shape.TypeName,
                false,
                false,
                string.Empty,
                "/phase185/shape",
                10f,
                shape.TypeName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                "Test",
                0,
                string.Empty,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                typeShape: shape,
                namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding);

        private static void AssertComponentShape(
            FoxRunTypeShape shape,
            string typeName,
            IReadOnlyList<string> components)
        {
            Assert.NotNull(shape);
            Assert.Equal(FoxRunTypeShapeKind.Object, shape.Kind);
            Assert.Equal(typeName, shape.TypeName);
            Assert.True(shape.CanConstruct);
            Assert.Equal(components, shape.Fields.Select(field => field.JsonName).ToArray());
            Assert.All(shape.Fields, field =>
            {
                Assert.Equal(field.JsonName, field.MemberName);
                Assert.True(field.CanAssign);
                Assert.Equal(FoxRunTypeShapeKind.Canonical, field.TypeShape.Kind);
                Assert.Equal("float32", field.TypeShape.CanonicalType);
            });
        }

        private static string FieldIdentity(FoxRunTypeField field)
            => field.JsonName
               + "|"
               + field.MemberName
               + "|"
               + field.TypeShape.Kind
               + "|"
               + field.TypeShape.CanonicalType;

        private static string ShapeIdentity(FoxRunTypeShape shape)
            => shape.Kind
               + ":"
               + shape.TypeName
               + ":"
               + shape.CanonicalType
               + "["
               + string.Join(
                   ",",
                   shape.Fields.Select(field =>
                       field.JsonName
                       + "="
                       + ShapeIdentity(field.TypeShape)))
               + "]"
               + (shape.ElementShape == null
                   ? string.Empty
                   : "<" + ShapeIdentity(shape.ElementShape) + ">");

        private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        {
            var locations = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator);
            return locations.Select(location => MetadataReference.CreateFromFile(location));
        }

        private enum WideEnum : long
        {
            TooLarge = (long)int.MaxValue + 1L
        }

        private enum SmallEnum
        {
            Unknown = 0,
            Active = 1
        }

        private enum NoZeroEnum
        {
            First = 1,
            Second = 2
        }

        private sealed class CollectionPayload
        {
            public List<CollectionSample> Samples { get; set; }
            public byte[] Payload { get; set; }
        }

        private sealed class CollectionSample
        {
            public int Value { get; set; }
        }

        private sealed class InitOnlyPayload
        {
            public int Value { get; init; }
        }

        private abstract class AbstractPayload
        {
            public int Value { get; set; }
        }

        private sealed class CyclicPayload
        {
            public CyclicPayload Next { get; set; }
        }
    }
}
