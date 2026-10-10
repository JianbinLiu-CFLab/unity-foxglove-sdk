// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the neutral, Manager-local FoxRun transport provider contract.

using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests
{
    public sealed partial class FoxRunTransportProviderTests
    {
        [Fact]
        public void RuntimeRegistryHasNoStaticProviderCollection()
        {
            var forbidden = typeof(FoxRunTransportProviderRegistry)
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field =>
                    typeof(IFoxRunTransportProvider).IsAssignableFrom(field.FieldType)
                    || field.FieldType.Name.Contains("Dictionary", StringComparison.Ordinal)
                    || field.FieldType.Name.Contains("List", StringComparison.Ordinal))
                .ToArray();

            Assert.Empty(forbidden);
        }

        [Fact]
        public void GenerationHostsPreserveCanonicalDirectionSpecificTransportIds()
        {
            var presence =
                FoxRunNamedArgumentPresence.PublishTransportIds
                | FoxRunNamedArgumentPresence.SubscribeTransportId;
            var publishIds = new[]
            {
                "unity2foxglove.zeta",
                FoxgloveWebSocketTransport.Id
            };
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/demo/value",
                    schemaName: "Demo.Value",
                    hz: 10f,
                    policy: (int)FoxRunPolicy.FixedRate,
                    tolerance: 0f,
                    rawMemberOrder: 1,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: publishIds,
                    subscribeTransportId: "unity2foxglove.alpha",
                    namedArgumentPresence: presence)
            });
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/demo/value",
                    schemaName: "Demo.Value",
                    hz: 10f,
                    policy: (int)FoxRunPolicy.FixedRate,
                    tolerance: 0f,
                    rawMemberOrder: 1,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe,
                    publishTransportIds: publishIds.Reverse().ToArray(),
                    subscribeTransportId: "unity2foxglove.alpha",
                    namedArgumentPresence: presence)
            });

            var roslynMember = Assert.Single(Assert.Single(roslyn.Types).Members);
            var reflectionMember = Assert.Single(Assert.Single(reflection.Types).Members);
            Assert.Equal(
                new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.zeta"
                },
                roslynMember.PublishTransportIds);
            Assert.Equal(
                roslynMember.PublishTransportIds,
                reflectionMember.PublishTransportIds);
            Assert.Equal(
                "unity2foxglove.alpha",
                roslynMember.SubscribeTransportId);
            Assert.Equal(
                roslynMember.SubscribeTransportId,
                reflectionMember.SubscribeTransportId);
            var comparison =
                FoxRunGenerationDescriptorComparer.Compare(roslyn, reflection);
            Assert.True(
                comparison.IsSemanticEqual,
                string.Join(Environment.NewLine, comparison.SemanticDifferences));

            var json = FoxRunGenerationDescriptorJsonWriter.Write(roslyn);
            Assert.Contains("\"descriptorVersion\":6", json);
            Assert.Contains(
                "\"publishTransportIds\":[\"foxglove.websocket\",\"unity2foxglove.zeta\"]",
                json);
            Assert.Contains(
                "\"subscribeTransportId\":\"unity2foxglove.alpha\"",
                json);
            Assert.Contains(
                "\"explicitArguments\":\"PublishTransportIds,SubscribeTransportId\"",
                json);

            var roslynManifest = FoxRunManifestBuilder.Build(
                roslyn.Types.Single().Members
                    .Select(FoxRunManifestMember.FromGenerationMember)
                    .ToArray(),
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var reflectionManifest = FoxRunManifestBuilder.Build(
                reflection.Types.Single().Members
                    .Select(FoxRunManifestMember.FromGenerationMember)
                    .ToArray(),
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contract = Assert.Single(
                Assert.Single(roslynManifest.Sections.FoxRun.Types).Contracts,
                candidate =>
                    candidate.Encoding
                    == FoxRunGenerationDescriptorConstants.JsonEncoding);
            Assert.True(contract.IncludesTransportSelection);
            Assert.Equal(
                new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.zeta"
                },
                contract.PublishTransportIds);
            Assert.Equal(
                "unity2foxglove.alpha",
                contract.SubscribeTransportId);
            Assert.Equal(
                roslynManifest.GlobalManifestHash,
                reflectionManifest.GlobalManifestHash);
            var manifestJson =
                FoxRunManifestJsonWriter.WriteCanonical(roslynManifest);
            Assert.Contains(
                "\"publishTransportIds\":[\"foxglove.websocket\",\"unity2foxglove.zeta\"]",
                manifestJson);
            Assert.Contains(
                "\"subscribeTransportId\":\"unity2foxglove.alpha\"",
                manifestJson);
        }

        [Fact]
        public void GeneratedCoreEmitsStableDirectProviderMemberAccess()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo",
                    "Source",
                    "_value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    true,
                    false,
                    "",
                    "/phase186/value",
                    10f,
                    "Demo.Value",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    "UnitTest",
                    1,
                    "",
                    mode: (int)FoxRunFlow.PublishAndSubscribe)
            });
            var type = Assert.Single(model.Types);
            var member = Assert.Single(type.Members);
            var stableId = FoxRunGeneratedMemberIdentity.Build(
                type.DeclaringType,
                member.MemberKind,
                member.MemberName,
                member.Topic,
                member.Mode,
                member.JsonFieldName);
            var fingerprint =
                FoxRunGeneratedMemberIdentity.Fingerprint(stableId);

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("__FoxRunRead_value_" + fingerprint, source);
            Assert.Contains("__FoxRunWrite_value_" + fingerprint, source);
            Assert.Contains(
                "=> __foxRunCapture_0_0;",
                source);
            Assert.Contains("IFoxRunGeneratedTransportSource", source);
            Assert.Contains(
                "IFoxRunGeneratedTransportSource.FoxRunTransport_MemberCount => 1;",
                source);
            Assert.Contains(
                "IFoxRunGeneratedTransportSource.FoxRunTransport_GetMember(int index)",
                source);
            Assert.Contains(
                "new FoxRunGeneratedMemberAccess<int>",
                source);
            Assert.Contains(
                "\""
                + StringLiteralEmitter.CSharpStringLiteral(stableId)
                + "\"",
                source);
            Assert.Contains(
                "FoxRunTransport_GetCaptureSequence(int topicIndex)",
                source);
            Assert.DoesNotContain("System.Reflection", source);
            Assert.DoesNotContain("GetField(", source);
            Assert.DoesNotContain("GetProperty(", source);
        }

        [Fact]
        public void GeneratedProviderAccessUsesTheManifestLogicalSchemaFallback()
        {
            var member = new FoxRunGenerationMember(
                ns: "Demo",
                className: "Source",
                memberName: "_log",
                memberKind: "field",
                rawObservedTypeName: "Foxglove.Log",
                emissionTypeName: "global::Foxglove.Log",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic: "/phase186/log",
                hz: 10f,
                schemaName: string.Empty,
                policy: (int)FoxRunPolicy.Change,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 0,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                typeShape: FoxRunTypeShape.Object(
                    "Foxglove.Log",
                    Array.Empty<FoxRunTypeField>()),
                generatesWebSocketCodec: true,
                publishTransportIds: new[]
                {
                    FoxgloveWebSocketTransport.Id,
                    "unity2foxglove.ros2bridge"
                },
                subscribeTransportId: "unity2foxglove.ros2bridge");
            var model = FoxRunGenerationModel.FromMembers(new[] { member });
            var type = Assert.Single(model.Types);
            var manifest = FoxRunManifestBuilder.Build(
                new[]
                {
                    FoxRunManifestMember.FromGenerationMember(member)
                },
                manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var contracts = Assert.Single(
                manifest.Sections.FoxRun.Types).Contracts;

            Assert.All(
                contracts,
                contract => Assert.Equal(
                    "Foxglove.Log",
                    contract.LogicalSchemaName));
            var source = FoxgloveSourceEmitter.EmitClass(type);
            Assert.Matches(
                @"new FoxRunGeneratedMemberAccess<(?:global::)?Foxglove\.Log>\s*\(\s*""[^""]+"",\s*""/phase186/log"",\s*""Foxglove\.Log"",\s*\(FoxRunFlow\)3,",
                source);
        }

        [Fact]
        public void PhysicalProviderContributionUsesDeterministicIndependentFile()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo",
                    "Source",
                    "Value",
                    "field",
                    "System.Int32",
                    "global::System.Int32",
                    true,
                    false,
                    "",
                    "/phase186/value",
                    10f,
                    "Demo.Value",
                    (int)FoxRunPolicy.FixedRate,
                    0f,
                    "UnitTest",
                    1,
                    "")
            });
            var type = Assert.Single(model.Types);
            var contribution = new FakeEmitterContribution();

            var source =
                FoxRunTransportContributionSource.EmitSourceFile(
                    model,
                    type,
                    contribution);
            var name = FoxRunTransportContributionSource.SourceName(
                type.Namespace,
                type.ClassName,
                contribution);

            Assert.Equal(
                "Demo_Source_unity2foxglove_example_transport_FoxRun.g.cs",
                name);
            Assert.Contains("// <auto-generated/>", source);
            Assert.Contains(
                "// Optional transport contribution: unity2foxglove.example",
                source);
            Assert.Contains("#if !UNITY_EDITOR", source);
            Assert.Contains(
                "partial class Source { private const int ProviderMarker = 1; }",
                source);
        }

    }
}
