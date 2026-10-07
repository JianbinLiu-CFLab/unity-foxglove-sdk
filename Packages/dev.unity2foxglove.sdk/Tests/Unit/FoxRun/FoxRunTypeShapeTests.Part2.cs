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
        [Trait("Phase", "185-F")]
        public void ObjectShapesCarryValueTypeIdentityAcrossBothHosts()
        {
            const string source = @"
namespace Demo
{
    public struct ValuePayload { public int Value; }
    public sealed class ReferencePayload { public int Value; }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185ObjectValueTypeParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));

            var valueSymbol = compilation.GetTypeByMetadataName(
                "Demo.ValuePayload");
            var referenceSymbol = compilation.GetTypeByMetadataName(
                "Demo.ReferencePayload");
            Assert.NotNull(valueSymbol);
            Assert.NotNull(referenceSymbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    valueSymbol,
                    out var roslynValue));
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    referenceSymbol,
                    out var roslynReference));

            Assert.True(roslynValue.IsValueType);
            Assert.False(roslynReference.IsValueType);
            Assert.True(
                FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(ValuePayload)).IsValueType);
            Assert.False(
                FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(ReferencePayload)).IsValueType);
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
            Assert.Equal(
                ShapeIdentity(first),
                ShapeIdentity(second));

            var firstModel = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    first,
                    (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_root")
            });
            var secondModel = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    second,
                    (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    memberName: "_root")
            });

            Assert.Equal(
                FoxRunGenerationDescriptorJsonWriter.Write(firstModel),
                FoxRunGenerationDescriptorJsonWriter.Write(secondModel));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void TypeShapeIdentityIsInjectiveForDelimiterBearingNames()
        {
            var scalar = FoxRunTypeShape.Canonical("int32");
            var scalarIdentity =
                FoxRunTypeShapeIdentityFormatter.Build(
                    scalar,
                    includeUsageTraits: true);
            var fieldSuffix =
                ":0:0:1:0{" + scalarIdentity + "};";
            var twoFields = FoxRunTypeShape.Object(
                "Demo.Collision",
                new[]
                {
                    new FoxRunTypeField("a", "X", scalar),
                    new FoxRunTypeField("b", "Y", scalar)
                });
            var oneField = FoxRunTypeShape.Object(
                "Demo.Collision",
                new[]
                {
                    new FoxRunTypeField(
                        "a=X" + fieldSuffix + "b",
                        "Y",
                        scalar)
                });

            Assert.NotEqual(
                FoxRunTypeShapeIdentityFormatter.Build(
                    twoFields,
                    includeUsageTraits: true),
                FoxRunTypeShapeIdentityFormatter.Build(
                    oneField,
                    includeUsageTraits: true));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExplicitMessagePackAcceptsOneOrdinaryOrExactlyOneStreamSubscribeMember(
            bool isStream)
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member(
                    "_incoming",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    isStream: isStream)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN618");
        }

        [Fact]
        public void ExplicitMessagePackAcceptsEqualNormalizedScheduleTuples()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member(
                    "_first",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    hz: 20f,
                    explicitHz: true),
                Member(
                    "_second",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    hz: 20f,
                    explicitHz: true)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN619");
            Assert.All(
                model.Types.Single().Members,
                member => Assert.True(Assert.Single(member.EncodingVariants).PublishAvailable));
        }

        [Fact]
        public void ExplicitMessagePackTreatsByteArrayAsSupportedBinaryWithoutLegacyBlobWarning()
        {
            var memberData = new FoxrunCodeGenerator.MemberData(
                "_payload",
                typeof(byte[]),
                "field",
                "Demo",
                "BinaryPublisher",
                "/phase185/binary",
                10f,
                "Demo.Binary",
                mode: (int)FoxRunFlow.Publish,
                encoding: (int)FoxRunEncoding.MessagePack,
                namedArgumentPresence: FoxRunNamedArgumentPresence.Encoding);
            var model = FoxRunReflectionGenerationModelLowerer.Lower(
                new[] { memberData.ToReflectionMember() });
            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN010"
                              || diagnostic.Id == "FOXRUN616");
            Assert.True(Assert.Single(model.Types.Single().Members).TypeShape.IsBinary);
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

    }
}
