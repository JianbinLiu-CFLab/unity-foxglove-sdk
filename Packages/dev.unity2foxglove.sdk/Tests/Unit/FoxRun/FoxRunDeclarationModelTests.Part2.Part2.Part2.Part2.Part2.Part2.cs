// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        [Fact]
        public void ReflectionLowererPreservesDeclaredWirePolicyAndFieldNumber()
        {
            var model = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "WireState", "_count", "field", "System.Int32", "int",
                    true, false, "", "/phase175/wire_state", "", 10f, 0, 0f, 0, "",
                    encoding: (int)FoxRunEncoding.Protobuf,
                    protobufFieldNumber: 17)
            });
            var member = model.Types.Single().Members.Single();

            Assert.Equal("protobuf", member.Encoding);
            Assert.Equal(17, member.ProtobufMetadata.FieldNumber);
        }



        [Fact]
        [Trait("Phase", "186-A")]
        public void ReflectionScannerPreservesDirectionSpecificTransportProviderSelection()
        {
            var snapshot = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.ProviderSelection)));
            const FoxRunNamedArgumentPresence providerAxes =
                FoxRunNamedArgumentPresence.PublishTransportIds
                | FoxRunNamedArgumentPresence.SubscribeTransportId;
            var presence = (FoxRunNamedArgumentPresence)ReadInt64Field(
                snapshot,
                "NamedArgumentPresence");

            Assert.Equal(providerAxes, presence & providerAxes);
            Assert.Equal(
                new[]
                {
                    "unity2foxglove.zeta",
                    "foxglove.websocket"
                },
                ReadField<string[]>(snapshot, "PublishTransportIds"));
            Assert.Equal(
                "unity2foxglove.alpha",
                ReadField<string>(snapshot, "SubscribeTransportId"));

            var member = new FoxrunCodeGenerator.MemberData(
                nameof(ReflectionArgumentsFixture.ProviderSelection),
                typeof(float),
                "field",
                typeof(ReflectionArgumentsFixture).Namespace ?? string.Empty,
                nameof(ReflectionArgumentsFixture),
                "/phase186/reflection/providers",
                -1f,
                string.Empty,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                namedArgumentPresence: presence,
                publishTransportIds:
                    ReadField<string[]>(snapshot, "PublishTransportIds"),
                subscribeTransportId:
                    ReadField<string>(snapshot, "SubscribeTransportId"));
            var lowered = Assert.Single(
                Assert.Single(
                    FoxRunReflectionGenerationModelLowerer.Lower(
                        new[] { member.ToReflectionMember() }).Types).Members);

            Assert.Equal(
                new[]
                {
                    "foxglove.websocket",
                    "unity2foxglove.zeta"
                },
                lowered.PublishTransportIds);
            Assert.Equal("unity2foxglove.alpha", lowered.SubscribeTransportId);
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void AggregateRoslynAndReflectionPreservePublishTransportProviders()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(
        ""/phase186/aggregate"",
        PublishTransportIds = new[]
        {
            ""unity2foxglove.zeta"",
            ""foxglove.websocket""
        })]
    public partial class AggregateState
    {
        [FoxRunField(""value"")]
        public int Value;
    }
}";
            var roslyn = ExtractRoslynMemberData(source);
            var topic = Assert.Single(roslyn.Topics);
            Assert.Equal(
                FoxRunNamedArgumentPresence.PublishTransportIds,
                topic.NamedArgumentPresence
                & FoxRunNamedArgumentPresence.PublishTransportIds);
            Assert.Equal(
                new[]
                {
                    "unity2foxglove.zeta",
                    "foxglove.websocket"
                },
                topic.PublishTransportIds);

            var aggregateSnapshot = ReadReflectionMessageAttributeSnapshot(
                typeof(ReflectionAggregateFixture));
            var aggregatePresence =
                (FoxRunNamedArgumentPresence)ReadInt64Field(
                    aggregateSnapshot,
                    "NamedArgumentPresence");
            var reflected = new FoxrunCodeGenerator.MemberData(
                nameof(ReflectionAggregateFixture.Value),
                typeof(int),
                "field",
                typeof(ReflectionAggregateFixture).Namespace ?? string.Empty,
                nameof(ReflectionAggregateFixture),
                "/phase186/reflection/aggregate",
                -1f,
                typeof(ReflectionAggregateFixture).FullName,
                isAggregateMember: true,
                jsonFieldName: "value",
                namedArgumentPresence: aggregatePresence,
                publishTransportIds:
                    ReadField<string[]>(
                        aggregateSnapshot,
                        "PublishTransportIds"));
            var reflectionModel = FoxRunReflectionGenerationModelLowerer.Lower(
                new[] { reflected.ToReflectionMember() });
            var reflectionMember = Assert.Single(
                Assert.Single(reflectionModel.Types).Members);
            var roslynModel = FoxRunRoslynGenerationModelLowerer.Lower(
                roslyn.ToRoslynMembers());
            var roslynMember = Assert.Single(
                Assert.Single(roslynModel.Types).Members);

            Assert.Equal(
                new[]
                {
                    "foxglove.websocket",
                    "unity2foxglove.zeta"
                },
                roslynMember.PublishTransportIds);
            Assert.Equal(
                roslynMember.PublishTransportIds,
                reflectionMember.PublishTransportIds);
            Assert.Null(roslynMember.SubscribeTransportId);
            Assert.Null(reflectionMember.SubscribeTransportId);
        }

        [Fact]
        [Trait("Phase", "187-R2-E01-003")]
        public void DuplicatePublishTransportIdsReachTheFailClosedValidator()
        {
            var member = new FoxrunCodeGenerator.MemberData(
                "Value",
                typeof(int),
                "field",
                "Phase187E01",
                "DuplicateProviders",
                "/phase187/e01/duplicate-providers",
                -1f,
                string.Empty,
                mode: (int)FoxRunFlow.Publish,
                encoding: (int)FoxRunEncoding.JSON,
                namedArgumentPresence: FoxRunNamedArgumentPresence.PublishTransportIds,
                publishTransportIds: new[]
                {
                    "foxglove.websocket",
                    "foxglove.websocket"
                });

            var model = FoxRunReflectionGenerationModelLowerer.Lower(
                new[] { member.ToReflectionMember() });
            var diagnostics = FoxRunGenerationModelValidator.Validate(model);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN620");
        }

        [Fact]
        public void ReflectionScannerPreservesInvalidExplicitEnumCast()
        {
            var invalid = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.InvalidPolicy)));

            Assert.Equal(99, ReadField<int>(invalid, "Policy"));
            var presence = (FoxRunNamedArgumentPresence)ReadInt64Field(
                invalid,
                "NamedArgumentPresence");
            Assert.True((presence & FoxRunNamedArgumentPresence.Policy) != 0);
        }



        [Fact]
        public void ReflectionAndRoslynLowerersProduceEquivalentCanonicalConditionModel()
        {
            const FoxRunNamedArgumentPresence presence =
                FoxRunNamedArgumentPresence.Hz
                | FoxRunNamedArgumentPresence.Tolerance
                | FoxRunNamedArgumentPresence.OnlyIf
                | FoxRunNamedArgumentPresence.Policy
                | FoxRunNamedArgumentPresence.Mode;
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo", "Parity", "_value", "field", "System.Int32", "int",
                    true, false, "", "/phase184/parity", "", 12f, 2, 0.5f, 0, "",
                    onlyIf: "CanApply",
                    mode: 2,
                    namedArgumentPresence: presence,
                    conditionMemberKind: FoxRunConditionMemberKind.Method)
            });
            var roslyn = FoxRunRoslynGenerationModelLowerer.Lower(new[]
            {
                new FoxRunRoslynGenerationMember(
                    "Demo", "Parity", "_value", "field", "System.Int32", "int",
                    true, false, "", "/phase184/parity", "", 12f, 2, 0.5f, 0, "",
                    onlyIf: "CanApply",
                    mode: 2,
                    namedArgumentPresence: presence,
                    conditionMemberKind: FoxRunConditionMemberKind.Method)
            });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(reflection, roslyn);

            Assert.True(
                comparison.IsSemanticEqual,
                string.Join(Environment.NewLine, comparison.SemanticDifferences));
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                reflection.Types.Single().Members.Single().ConditionMemberKind);
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                roslyn.Types.Single().Members.Single().ConditionMemberKind);
        }





        [Fact]
        [Trait("Phase", "186-A")]
        public void ExternalProviderConstantDoesNotEnableCoreWebSocketInput()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    public static class FoxRunRos2TransportProvider
    {
        public const string IdValue = ""unity2foxglove.r2fu"";
    }
}

namespace ROS2
{
    public interface Message
    {
    }
}

namespace std_msgs.msg
{
    public sealed class String : ROS2.Message
    {
        public string Data { get; set; }
    }
}

namespace Demo
{
    public partial class R2fuOnlyInput
    {
        [FoxRun(""/phase186/r2fu-only"",
            Mode = FoxRunFlow.Subscribe,
            SubscribeTransportId =
                Unity2Foxglove.Ros2ForUnity.Native
                    .FoxRunRos2TransportProvider.IdValue)]
        private std_msgs.msg.String _value;
    }
}";
            var result = RunGenerator(source);
            var generated = result.Results
                .Single()
                .GeneratedSources
                .Single(item =>
                    item.HintName
                    == "Demo_R2fuOnlyInput_FoxRun.g.cs")
                .SourceText
                .ToString();

            Assert.DoesNotContain(
                "IFoxgloveInputSource",
                generated,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "FoxRunInboundJson",
                generated,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void RoslynExtractionPreservesCanonicalTransportProviderSelection()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ProviderSelection
    {
        [FoxRun(""/phase186/provider"",
            Mode = FoxRunFlow.PublishAndSubscribe,
            PublishTransportIds = new[]
            {
                ""unity2foxglove.zeta"",
                ""foxglove.websocket""
            },
            SubscribeTransportId = ""unity2foxglove.alpha"")]
        private int _value;
    }
}";
            var extracted = ExtractRoslynMemberData(source);
            var topic = Assert.Single(extracted.Topics);

            Assert.Equal(
                FoxRunNamedArgumentPresence.PublishTransportIds
                | FoxRunNamedArgumentPresence.SubscribeTransportId,
                topic.NamedArgumentPresence
                & (FoxRunNamedArgumentPresence.PublishTransportIds
                   | FoxRunNamedArgumentPresence.SubscribeTransportId));
            Assert.Equal(
                new[]
                {
                    "unity2foxglove.zeta",
                    "foxglove.websocket"
                },
                topic.PublishTransportIds);
            Assert.Equal("unity2foxglove.alpha", topic.SubscribeTransportId);

            var model = FoxRunRoslynGenerationModelLowerer.Lower(
                extracted.ToRoslynMembers());
            var member = Assert.Single(Assert.Single(model.Types).Members);
            Assert.Equal(
                new[]
                {
                    "foxglove.websocket",
                    "unity2foxglove.zeta"
                },
                member.PublishTransportIds);
            Assert.Equal("unity2foxglove.alpha", member.SubscribeTransportId);
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN620"
                              || diagnostic.Id == "FOXRUN621");

            var generated = RunGenerator(source);
            Assert.DoesNotContain(
                generated.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = generated.Results
                .Single()
                .GeneratedSources
                .Single(item =>
                    item.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();
            Assert.Contains(
                "\\\"publishTransportIds\\\":[\\\"foxglove.websocket\\\",\\\"unity2foxglove.zeta\\\"]",
                descriptor,
                StringComparison.Ordinal);
            Assert.Contains(
                "\\\"subscribeTransportId\\\":\\\"unity2foxglove.alpha\\\"",
                descriptor,
                StringComparison.Ordinal);
        }

    }
}
