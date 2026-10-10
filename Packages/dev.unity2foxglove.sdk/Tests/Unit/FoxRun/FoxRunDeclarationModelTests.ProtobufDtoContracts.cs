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
        public void RoslynGeneratorRejectsReadonlyNestedProtobufDtoInput()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class Command
    {
        public int Value { get; }
    }

    public partial class CommandInput
    {
        [FoxRun(""/phase175/readonly_dto"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private Command _incomingCommand;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN200");
        }

        [Fact]
        [Trait("Phase", "187-R2-E01-002")]
        public void ReadonlyResponseFieldsStillValidateUnsupportedTypesInBothHosts()
        {
            var reflection = FoxServiceDtoReflectionValidator.Validate(
                typeof(E01ReadonlyResponse),
                FoxServiceDtoSide.Response,
                "/phase187/e01/readonly-response");
            Assert.Contains(reflection, diagnostic => diagnostic.Id == "FOXSERVICE004"
                                                       && diagnostic.Path == "Response.Handle");
            Assert.Contains(reflection, diagnostic => diagnostic.Id == "FOXSERVICE004"
                                                       && diagnostic.Path == "Response.ReadonlyProperty");

            var result = RunGenerator(@"
using System;
using Unity.FoxgloveSDK.Components;

namespace Phase187E01
{
    public sealed class Response
    {
        public readonly IntPtr Handle;
        public IntPtr ReadonlyProperty { get { return IntPtr.Zero; } }
    }

    public partial class Host
    {
        [FoxService(""/phase187/e01/readonly-response"", Type = ""Phase187E01.Service"", RequestSchemaName = ""Phase187E01.Request"", ResponseSchemaName = ""Phase187E01.Response"")]
        private Response Invoke(int request) => new Response();
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXSERVICE004"
                                                               && diagnostic.GetMessage().Contains("Response.Handle", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXSERVICE004"
                                                               && diagnostic.GetMessage().Contains("Response.ReadonlyProperty", StringComparison.Ordinal));
        }

        [Fact]
        public void GeneratedProtobufDtoAndCollectionInputCompilesWithItsHostType()
        {
            var output = RunGeneratorAndUpdateCompilation(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public enum CommandKind { Unknown = 0, Start = 1 }

    public sealed class Command
    {
        public int Sequence { get; set; }
        public float Confidence { get; set; }
        public sbyte SignedByte { get; set; }
        public short SignedShort { get; set; }
        public byte UnsignedByte { get; set; }
        public ushort UnsignedShort { get; set; }
        public byte[] Bytes { get; set; }
        public List<short> Offsets { get; set; }
        public List<int> Values { get; set; }
        public List<CommandKind> Kinds { get; set; }
    }

    public partial class CommandInput
    {
        [FoxRun(""/phase175/commands"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private Command _incomingCommand;

        [FoxRun(""/phase175/ints"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private int[] _incomingInts;

        [FoxRun(""/phase175/bytes"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private byte[] _incomingBytes;

        [FoxRun(""/phase175/shorts"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private List<short> _incomingShorts;

        [FoxRun(""/phase175/byte"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private byte _incomingByte;

        [FoxRun(""/phase175/kind"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private CommandKind _incomingKind;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

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

    }
}
