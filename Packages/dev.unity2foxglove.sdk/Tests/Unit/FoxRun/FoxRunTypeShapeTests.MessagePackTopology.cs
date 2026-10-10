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
        public void RoslynAndReflectionBuildersExcludePropertiesWithoutPublicGetters()
        {
            const string source = @"
namespace Demo
{
    public sealed class PrivateGetterPayload
    {
        public int Value { private get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185PrivateGetterParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName("Demo.PrivateGetterPayload");

            Assert.NotNull(symbol);
            Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(PrivateGetterPayload));
            Assert.Empty(roslyn.Fields);
            Assert.Empty(reflection.Fields);
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
            bool explicitHz = false,
            string jsonFieldName = "")
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
                jsonFieldName: jsonFieldName,
                mode: mode,
                encoding: encoding,
                typeShape: shape,
                namedArgumentPresence: presence);
        }

        private static FoxRunTypeShape ManualObjectChain(int relativeDepth)
        {
            var shape = FoxRunTypeShape.Canonical("int32");
            for (var index = 0; index < relativeDepth; index++)
            {
                shape = FoxRunTypeShape.Object(
                    "Demo.Depth" + index,
                    new[]
                    {
                        new FoxRunTypeField(
                            "next",
                            "Next",
                            shape)
                    });
            }
            return shape;
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

        private static void AssertNullableReuseShape(FoxRunTypeShape shape)
        {
            var required = Assert.Single(
                shape.Fields,
                field => field.MemberName == "Required");
            var optional = Assert.Single(
                shape.Fields,
                field => field.MemberName == "Optional");

            Assert.False(required.IsNullable);
            Assert.False(required.TypeShape.Nullable);
            Assert.True(optional.IsNullable);
            Assert.True(optional.TypeShape.Nullable);
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
                .Split(Path.PathSeparator)
                .Append(
                    typeof(Newtonsoft.Json.JsonPropertyAttribute)
                        .Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations.Select(location => MetadataReference.CreateFromFile(location));
        }
    }
}
