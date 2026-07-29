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
        public void ExplicitMessagePackRejectsProtobufOnlyFieldNumbers()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member(
                    "_state",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    protobufFieldNumber: 17,
                    explicitEncoding: true)
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN617");
        }

        [Fact]
        public void ExplicitMessagePackRejectsMixedOrdinaryAndStreamSubscribeTopology()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member(
                    "_ordinary",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true),
                Member(
                    "_stream",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    isStream: true)
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN618");
        }

        [Fact]
        public void InheritedTopologyConflictKeepsLegacyVariantsAndMarksOnlyMessagePackSubscribeUnavailable()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member("_ordinary", mode: (int)FoxRunFlow.Subscribe),
                Member("_stream", mode: (int)FoxRunFlow.Subscribe, isStream: true)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN618");
            var variants = Assert.Single(model.Types).Members[0].EncodingVariants;
            Assert.True(Assert.Single(variants, value => value.Encoding == "json").SubscribeAvailable);
            Assert.True(Assert.Single(variants, value => value.Encoding == "protobuf").SubscribeAvailable);
            var messagePack = Assert.Single(variants, value => value.Encoding == "msgpack");
            Assert.False(messagePack.SubscribeAvailable);
            Assert.Equal("FOXRUN618", messagePack.SubscribeUnavailableDiagnosticId);
        }

        [Fact]
        public void ExplicitMessagePackRejectsDifferentNormalizedSchedules()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member(
                    "_first",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    hz: 10f,
                    explicitHz: true),
                Member(
                    "_second",
                    mode: (int)FoxRunFlow.Publish,
                    encoding: FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    explicitEncoding: true,
                    hz: 20f,
                    explicitHz: true)
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN619");
        }

        [Fact]
        public void InheritedScheduleConflictKeepsLegacyVariantsAndMarksOnlyMessagePackPublishUnavailable()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                Member("_first", mode: (int)FoxRunFlow.Publish, hz: 10f, explicitHz: true),
                Member("_second", mode: (int)FoxRunFlow.Publish, hz: 20f, explicitHz: true)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN619");
            var variants = Assert.Single(model.Types).Members[0].EncodingVariants;
            Assert.True(Assert.Single(variants, value => value.Encoding == "json").PublishAvailable);
            Assert.True(Assert.Single(variants, value => value.Encoding == "protobuf").PublishAvailable);
            var messagePack = Assert.Single(variants, value => value.Encoding == "msgpack");
            Assert.False(messagePack.PublishAvailable);
            Assert.Equal("FOXRUN619", messagePack.PublishUnavailableDiagnosticId);
        }

        [Fact]
        public void InheritedFullDuplexMessagePackKeepsDirectionSpecificUnavailableDiagnostics()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    FoxRunReflectionTypeShapeBuilder.Build(typeof(NoDefaultConstructorPayload)),
                    (int)FoxRunFlow.PublishAndSubscribe,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false,
                    memberName: "_fullDuplex",
                    hz: 10f,
                    explicitHz: true),
                ShapedMember(
                    FoxRunTypeShape.Canonical("int32"),
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false,
                    memberName: "_secondPublisher",
                    hz: 20f,
                    explicitHz: true)
            });

            var messagePack = Assert.Single(
                Assert.Single(model.Types).Members[0].EncodingVariants,
                value => value.Encoding == "msgpack");
            Assert.False(messagePack.PublishAvailable);
            Assert.False(messagePack.SubscribeAvailable);
            Assert.Equal("FOXRUN619", messagePack.PublishUnavailableDiagnosticId);
            Assert.Equal("FOXRUN616", messagePack.SubscribeUnavailableDiagnosticId);
            Assert.Contains(
                "schedule",
                messagePack.PublishUnavailableReason,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "constructible",
                messagePack.SubscribeUnavailableReason,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FromMembersDoesNotMutateCallerMembersOrPreviouslyBuiltModels()
        {
            var first = Member(
                "_first",
                mode: (int)FoxRunFlow.Publish,
                hz: 10f,
                explicitHz: true);
            var second = Member(
                "_second",
                mode: (int)FoxRunFlow.Publish,
                hz: 20f,
                explicitHz: true);

            Assert.True(MessagePackPublishAvailable(first));
            var invalidFirst = FoxRunGenerationModel.FromMembers(new[] { first, second });
            Assert.False(MessagePackPublishAvailable(
                Assert.Single(invalidFirst.Types).Members[0]));
            Assert.True(MessagePackPublishAvailable(first));

            var validAfterInvalid = FoxRunGenerationModel.FromMembers(new[] { first });
            Assert.True(MessagePackPublishAvailable(
                Assert.Single(validAfterInvalid.Types).Members[0]));
            Assert.True(MessagePackPublishAvailable(first));

            var third = Member(
                "_third",
                mode: (int)FoxRunFlow.Publish,
                hz: 10f,
                explicitHz: true);
            var fourth = Member(
                "_fourth",
                mode: (int)FoxRunFlow.Publish,
                hz: 20f,
                explicitHz: true);
            var validBeforeInvalid = FoxRunGenerationModel.FromMembers(new[] { third });
            Assert.True(MessagePackPublishAvailable(
                Assert.Single(validBeforeInvalid.Types).Members[0]));

            var invalidSecond = FoxRunGenerationModel.FromMembers(new[] { third, fourth });
            Assert.False(MessagePackPublishAvailable(
                Assert.Single(invalidSecond.Types).Members[0]));
            Assert.True(MessagePackPublishAvailable(
                Assert.Single(validBeforeInvalid.Types).Members[0]));
            Assert.True(MessagePackPublishAvailable(third));
        }

        private static FoxRunGenerationMember Member(
            string memberName,
            int mode,
            string encoding = FoxRunGenerationDescriptorConstants.InheritEncoding,
            int protobufFieldNumber = 0,
            bool explicitEncoding = false,
            bool isStream = false,
            float hz = 10f,
            bool explicitHz = false)
        {
            var presence = FoxRunNamedArgumentPresence.None;
            if (explicitEncoding)
                presence |= FoxRunNamedArgumentPresence.Encoding;
            if (explicitHz)
                presence |= FoxRunNamedArgumentPresence.Hz;
            return new FoxRunGenerationMember(
                "Demo",
                "State",
                memberName,
                "field",
                "System.Int32",
                true,
                false,
                string.Empty,
                "/phase185/state",
                hz,
                "Demo.State",
                (int)FoxRunPolicy.FixedRate,
                0f,
                "Test",
                0,
                string.Empty,
                mode: mode,
                encoding: encoding,
                protobufFieldNumber: protobufFieldNumber,
                typeShape: FoxRunTypeShape.Canonical("int32"),
                namedArgumentPresence: presence,
                isStream: isStream);
        }

        private static FoxRunGenerationMember ShapedMember(
            FoxRunTypeShape shape,
            int mode,
            string encoding,
            bool explicitEncoding,
            string memberName = "_incoming",
            float hz = 10f,
            bool explicitHz = false)
        {
            var presence = explicitEncoding
                ? FoxRunNamedArgumentPresence.Encoding
                : FoxRunNamedArgumentPresence.None;
            if (explicitHz)
                presence |= FoxRunNamedArgumentPresence.Hz;
            return new FoxRunGenerationMember(
                "Demo",
                "ShapeOwner",
                memberName,
                "field",
                shape.TypeName,
                false,
                false,
                string.Empty,
                "/phase185/shape",
                hz,
                shape.TypeName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                "Test",
                0,
                string.Empty,
                mode: mode,
                encoding: encoding,
                typeShape: shape,
                namedArgumentPresence: presence);
        }

        private static void AssertDirectionAvailability(
            FoxRunTypeShape shape,
            bool publishAvailable,
            bool subscribeAvailable)
        {
            var publish = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false)
            });
            var subscribe = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false)
            });

            Assert.Equal(
                publishAvailable,
                Assert.Single(
                    Assert.Single(publish.Types).Members[0].EncodingVariants,
                    value => value.Encoding == "msgpack").PublishAvailable);
            Assert.Equal(
                subscribeAvailable,
                Assert.Single(
                    Assert.Single(subscribe.Types).Members[0].EncodingVariants,
                    value => value.Encoding == "msgpack").SubscribeAvailable);
        }

        private static bool MessagePackPublishAvailable(FoxRunGenerationMember member)
            => Assert.Single(
                member.EncodingVariants,
                value => value.Encoding == FoxRunGenerationDescriptorConstants.MessagePackEncoding)
                .PublishAvailable;

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

        private sealed class NoDefaultConstructorPayload
        {
            public NoDefaultConstructorPayload(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        private sealed class InitOnlyPayload
        {
            public int Value { get; init; }
        }

        private sealed class ListContractPayload
        {
            public List<int> Concrete { get; set; }
            public IList<int> Mutable { get; set; }
            public IReadOnlyList<int> ReadOnly { get; set; }
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
