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
        public void GeneratedMethodNameCollisionsReportStableFoxRunDiagnostic()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerCollisions
    {
        [FoxRun(""/phase184/publish-a"", Policy = Trigger)]
        private int _command;

        [FoxRun(""/phase184/publish-b"", Policy = Trigger)]
        private int command;

        [FoxRun(""/phase184/subscribe"", Mode = Subscribe, Policy = Trigger,
            Encoding = FoxRunEncoding.JSON)]
        private int _incoming;

        public bool FoxRun_Publish_command_2() => false;
        public bool FoxRun_PublishAll() => false;
        public bool FoxRun_Apply_incoming() => false;
        public bool FoxRun_ApplyAll() => false;
    }
}";
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var output,
                out _);
            var messages = driver.GetRunResult().Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXRUN610")
                .Select(diagnostic => diagnostic.GetMessage())
                .ToArray();

            Assert.Contains(messages, message =>
                message.Contains("FoxRun_Publish_command_2", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_PublishAll", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_Apply_incoming", StringComparison.Ordinal));
            Assert.Contains(messages, message =>
                message.Contains("FoxRun_ApplyAll", StringComparison.Ordinal));
            Assert.DoesNotContain(output.GetDiagnostics(), diagnostic => diagnostic.Id == "CS0111");
        }

        [Fact]
        public void RoslynGeneratorPreservesAggregateInheritedWirePolicyAndFieldNumber()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    [FoxRunMessage(""/phase175/aggregate"")]
    public partial class AggregateState
    {
        [FoxRunField(""count"", ProtobufFieldNumber = 23)]
        private int _count;
    }
}");
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"encoding\\\":\\\"inherit\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains(
                "\\\"protobuf\\\":{\\\"fieldNumber\\\":23",
                descriptor,
                StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorAcceptsNestedDtoForProtobufContract()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class VehicleTelemetry
    {
        public string Label;
        public Pose Pose;
    }

    public sealed class Pose
    {
        public float X;
        public float Y;
    }

    public partial class WireState
    {
        [FoxRun(""/phase175/dto"", Encoding = FoxRunEncoding.Protobuf)]
        private VehicleTelemetry _telemetry;
    }
}");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void WebSocketValidationAllowsJsonDtoAndEnumShapesButRejectsUnknownScalars()
        {
            var objectShape = FoxRunTypeShape.Object(
                "Demo.Payload",
                Array.Empty<FoxRunTypeField>());
            var enumShape = FoxRunTypeShape.Enum(
                "Demo.State",
                new[]
                {
                    new FoxRunEnumValue("UNSPECIFIED", 0),
                    new FoxRunEnumValue("READY", 1),
                });
            var members = new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "JsonInputs", "_incomingPayload", "field", "Demo.Payload",
                    false, false, "", "/phase184/json/payload", 10f, "",
                    1, 0.1f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                    typeShape: objectShape),
                new FoxRunGenerationMember(
                    "Demo", "JsonInputs", "_incomingState", "field", "Demo.State",
                    true, false, "", "/phase184/json/state", 10f, "",
                    1, 0.1f, "UnitTest", 1, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                    typeShape: enumShape),
                new FoxRunGenerationMember(
                    "Demo", "JsonInputs", "_incomingUnknown", "field", "Demo.CustomScalar",
                    true, false, "", "/phase184/json/unknown", 10f, "",
                    1, 0.1f, "UnitTest", 2, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                    typeShape: FoxRunTypeShape.Canonical(
                        "demo.custom.scalar")),
            };

            var diagnostics = FoxRunGenerationModelValidator.Validate(
                FoxRunGenerationModel.FromMembers(members));

            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN006"
                              && diagnostic.MemberName == "_incomingPayload");
            Assert.DoesNotContain(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN006"
                              && diagnostic.MemberName == "_incomingState");
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN006"
                              && diagnostic.MemberName == "_incomingUnknown");
        }

        [Fact]
        public void RoslynGeneratorEmitsRecursiveDtoAndCollectionProtobufInputs()
        {
            var result = RunGenerator(@"
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Demo
{
    public sealed class Pose
    {
        public Vector3 Position { get; set; }
    }

    public sealed class Telemetry
    {
        public Pose Pose { get; set; }
        public List<float> Samples { get; set; }
    }

    public partial class CommandInput
    {
        [FoxRun(""/phase175/telemetry_in"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private Telemetry _incomingTelemetry;

        [FoxRun(""/phase175/samples_in"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private float[] _incomingSamples;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            Assert.Contains("__TryReadFoxRunProtobufObject", generated, StringComparison.Ordinal);
            Assert.Contains("TryReadRepeatedFloat", generated, StringComparison.Ordinal);
            Assert.Contains("__TryReadFoxRunProtobufCollection", generated, StringComparison.Ordinal);
            Assert.Contains("out global::Demo.Telemetry __value", generated, StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "187-R2-E02-001")]
        public void RoslynGeneratorDiscoversFoxRunAttributeAliases()
        {
            var result = RunGenerator(@"
using Run = Unity.FoxgloveSDK.Components.FoxRunAttribute;
using Service = Unity.FoxgloveSDK.Components.FoxServiceAttribute;

namespace Phase187E02
{
    public partial class AliasHost
    {
        [Run(""/phase187/e02/alias"")]
        private int _value;

        [Service(""/phase187/e02/service-alias"", Type = ""Phase187E02.Service"", RequestSchemaName = ""Phase187E02.Request"", ResponseSchemaName = ""Phase187E02.Response"")]
        private int Invoke(int request) => request;
    }
}");

            var generated = result.Results
                .SelectMany(run => run.GeneratedSources)
                .Select(source => source.SourceText.ToString())
                .ToArray();
            Assert.Contains(generated, source => source.Contains("/phase187/e02/alias", StringComparison.Ordinal));
            Assert.Contains(generated, source => source.Contains("/phase187/e02/service-alias", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
        }

        [Fact]
        [Trait("Phase", "187-R4-C5")]
        public void RoslynGeneratorIgnoresLookalikeFoxRunAttributes()
        {
            var result = RunGenerator(@"
using System;

namespace MyCorp
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class FoxRunAttribute : Attribute
    {
        public FoxRunAttribute(string topic) { }
    }
}

namespace Phase187R4C5
{
    public partial class Host
    {
        [MyCorp.FoxRun(""/c5/lookalike"")]
        private int _value;
    }
}");

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id.StartsWith(
                    "FOXRUN",
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                result.Results
                    .SelectMany(run => run.GeneratedSources)
                    .Select(source => source.SourceText.ToString()),
                source => source.Contains("/c5/lookalike", StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "187-R4-C5")]
        public void RoslynGeneratorDoesNotDiagnoseOrdinaryMultiDeclaratorAttributes()
        {
            var result = RunGenerator(@"
using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }
}

namespace Phase187R4C5
{
    public partial class Host
    {
        [UnityEngine.SerializeField]
        private int _first, _second;
    }
}");

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN004");
            Assert.DoesNotContain(
                result.Results
                    .SelectMany(run => run.GeneratedSources)
                    .Select(source => source.SourceText.ToString()),
                source => source.Contains("_first", StringComparison.Ordinal)
                          || source.Contains("_second", StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "187-R2-E02-002")]
        public void RoslynGeneratorRejectsCollidingFoxRunHostHintsBeforeAddSource()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace A.B
{
    public partial class C
    {
        [FoxRun(""/phase187/e02/collision-one"")]
        private int _value;
    }
}

namespace A
{
    public partial class B_C
    {
        [FoxRun(""/phase187/e02/collision-two"")]
        private int _value;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN623");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
        }

    }
}
