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
        [Trait("Phase", "185-F")]
        public void HiddenInheritedMemberNamesFailClosedAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class HiddenMemberBase
    {
        [JsonProperty(""baseValue"")] public int Value;
    }

    public sealed class HiddenMemberDerived : HiddenMemberBase
    {
        [JsonProperty(""derivedValue"")] public new int Value;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185HiddenMemberParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.HiddenMemberDerived");

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(HiddenMemberDerived)));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "Value",
                exception.Message,
                StringComparison.Ordinal);
        }

        private static void AssertHiddenLookupCollisionRejected(
            string source,
            string metadataName,
            Type reflectionType)
        {
            var compilation = CSharpCompilation.Create(
                "Phase185HiddenLookupParity_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(metadataName);

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(reflectionType));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "Value",
                exception.Message,
                StringComparison.Ordinal);
        }

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

    }
}
