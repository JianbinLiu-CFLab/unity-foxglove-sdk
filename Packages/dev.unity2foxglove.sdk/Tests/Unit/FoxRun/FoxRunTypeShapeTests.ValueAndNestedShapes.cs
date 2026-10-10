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

        [Fact]
        public void PublishAndSubscribeMessagePackShapeCapabilitiesAreIndependent()
        {
            var noDefaultConstructor = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(NoDefaultConstructorPayload));
            var initOnly = FoxRunReflectionTypeShapeBuilder.Build(typeof(InitOnlyPayload));

            AssertDirectionAvailability(
                noDefaultConstructor,
                publishAvailable: true,
                subscribeAvailable: false);
            AssertDirectionAvailability(
                initOnly,
                publishAvailable: true,
                subscribeAvailable: false);
            AssertDirectionAvailability(
                FoxRunReflectionTypeShapeBuilder.Build(typeof(ListContractPayload)),
                publishAvailable: true,
                subscribeAvailable: true);
        }

        [Fact]
        public void ExplicitMessagePackSubscribeRejectsNonConstructibleAndInitOnlyDtosWithFoxRun616()
        {
            foreach (var type in new[]
                     {
                         typeof(NoDefaultConstructorPayload),
                         typeof(InitOnlyPayload)
                     })
            {
                var member = ShapedMember(
                    FoxRunReflectionTypeShapeBuilder.Build(type),
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true);

                Assert.Contains(
                    FoxRunGenerationModelValidator.Validate(
                        FoxRunGenerationModel.FromMembers(new[] { member })),
                    diagnostic => diagnostic.Id == "FOXRUN616");
            }
        }

        [Fact]
        public void InheritedMessagePackSubscribeMarksOnlyThatVariantUnavailableForInvalidDto()
        {
            var member = ShapedMember(
                FoxRunReflectionTypeShapeBuilder.Build(typeof(NoDefaultConstructorPayload)),
                (int)FoxRunFlow.Subscribe,
                FoxRunGenerationDescriptorConstants.InheritEncoding,
                explicitEncoding: false);
            var model = FoxRunGenerationModel.FromMembers(new[] { member });
            var variants = Assert.Single(model.Types).Members[0].EncodingVariants;

            Assert.True(Assert.Single(variants, value => value.Encoding == "json").SubscribeAvailable);
            Assert.True(Assert.Single(variants, value => value.Encoding == "protobuf").SubscribeAvailable);
            var messagePack = Assert.Single(variants, value => value.Encoding == "msgpack");
            Assert.False(messagePack.SubscribeAvailable);
            Assert.Equal("FOXRUN616", messagePack.SubscribeUnavailableDiagnosticId);
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN616");
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

        [Fact]
        public void RoslynAndReflectionCustomBuildersRejectStreamDerivedDtos()
        {
            var root = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "SourceGenerators",
                "src",
                "FoxRunRoslynRos2CustomDtoShapeBuilder.cs"));

            Assert.Contains("IsDerivedFrom(type, \"System.IO.Stream\")", source, StringComparison.Ordinal);
            Assert.Contains("IsDerivedFrom(type, \"System.Threading.Tasks.Task\")", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ReusedNullableObjectShapeKeepsCallSiteNullabilityAcrossBothHostsAndMemberOrders()
        {
            const string source = @"
namespace Demo
{
    public struct Sample { public int Value; }
    public sealed class RequiredFirst
    {
        public Sample Required;
        public Sample? Optional;
    }
    public sealed class OptionalFirst
    {
        public Sample? Optional;
        public Sample Required;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185NullableMemoParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            foreach (var metadataName in new[] { "Demo.RequiredFirst", "Demo.OptionalFirst" })
            {
                var symbol = compilation.GetTypeByMetadataName(metadataName);
                Assert.NotNull(symbol);
                Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
                AssertNullableReuseShape(roslyn);
            }

            AssertNullableReuseShape(
                FoxRunReflectionTypeShapeBuilder.Build(typeof(RequiredFirstNullableReusePayload)));
            AssertNullableReuseShape(
                FoxRunReflectionTypeShapeBuilder.Build(typeof(OptionalFirstNullableReusePayload)));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Phase", "185-F")]
        public void MemoizedSubtreeCannotBypassAbsoluteDepthAcrossEitherHost(
            bool shallowFirst)
        {
            var compilation = CreateDepthReuseCompilation(
                deepestIndex: 30,
                shallowFirst);
            var symbol = compilation.GetTypeByMetadataName("Demo.Root");
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(diagnostic =>
                        diagnostic.ToString())));
            var reflectionType = System.Reflection.Assembly
                .Load(image.ToArray())
                .GetType("Demo.Root");
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(reflectionType));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait("Phase", "185-F")]
        public void MemoizedSubtreeAcceptsExactDepthBoundaryAcrossEitherHost(
            bool shallowFirst)
        {
            var compilation = CreateDepthReuseCompilation(
                deepestIndex: 29,
                shallowFirst);
            var symbol = compilation.GetTypeByMetadataName("Demo.Root");
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(diagnostic =>
                        diagnostic.ToString())));
            var reflectionType = System.Reflection.Assembly
                .Load(image.ToArray())
                .GetType("Demo.Root");
            Assert.NotNull(
                FoxRunReflectionTypeShapeBuilder.Build(reflectionType));
        }
    }
}
