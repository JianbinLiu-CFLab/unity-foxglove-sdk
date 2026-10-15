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

        [Fact]
        [Trait("Phase", "185-F")]
        public void DuplicateNestedJsonNamesFailClosedAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public sealed class DuplicateJsonNamePayload
    {
        [JsonProperty(""same"")] public int First;
        [JsonProperty(""same"")] public int Second;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185DuplicateJsonNameParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.DuplicateJsonNamePayload");

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(DuplicateJsonNamePayload)));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "same",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void InheritedDistinctMembersWithOneJsonNameFailClosedAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class DuplicateJsonNameBase
    {
        [JsonProperty(""same"")] public int First;
    }

    public sealed class DuplicateJsonNameDerived : DuplicateJsonNameBase
    {
        [JsonProperty(""same"")] public int Second;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185InheritedDuplicateJsonNameParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.DuplicateJsonNameDerived");

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(DuplicateJsonNameDerived)));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "same",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void PropertyOverridesRemainOneLogicalJsonMemberAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class OverrideBase
    {
        [JsonProperty(""same"")]
        public virtual int Value { get; set; }
    }

    public sealed class OverrideDerived : OverrideBase
    {
        [JsonProperty(""same"")]
        public override int Value { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185JsonOverrideParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.OverrideDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(OverrideDerived));
            Assert.Equal("same", Assert.Single(roslyn.Fields).JsonName);
            Assert.Equal(
                "same",
                Assert.Single(reflection.Fields).JsonName);
        }

    }
}
