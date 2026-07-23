// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunFlowTests
    {
        [Fact]
        public void FoxRunAttributeDefaultsToPublishFlow()
        {
            var attr = new FoxRunAttribute("/phase157/default");

            Assert.Equal(FoxRunFlow.Publish, attr.Mode);
        }

        [Fact]
        public void FreshDeclarationEnumsExposeOnlyNewNonZeroFlowAndPolicyValues()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var flowType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunFlow");
            var policyType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunPolicy");

            Assert.NotNull(flowType);
            Assert.NotNull(policyType);
            Assert.True(flowType.IsEnum);
            Assert.True(policyType.IsEnum);
            Assert.Equal(
                new[] { "Publish", "Subscribe", "PublishAndSubscribe" },
                Enum.GetNames(flowType));
            Assert.Equal(
                new[] { "FixedRate", "Change", "ChangeOrInterval", "Trigger" },
                Enum.GetNames(policyType));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(flowType, "Publish")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(flowType, "Subscribe")));
            Assert.Equal(3, Convert.ToInt32(Enum.Parse(flowType, "PublishAndSubscribe")));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(policyType, "FixedRate")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(policyType, "Change")));
            Assert.Equal(3, Convert.ToInt32(Enum.Parse(policyType, "ChangeOrInterval")));
            Assert.Equal(4, Convert.ToInt32(Enum.Parse(policyType, "Trigger")));
        }

        [Fact]
        public void InvalidPolicyDiagnosticNamesTheSupportedPolicies()
        {
            var message = Diags.InvalidPolicy.MessageFormat.ToString();

            Assert.Contains("FixedRate", message, StringComparison.Ordinal);
            Assert.Contains("Change", message, StringComparison.Ordinal);
            Assert.Contains("Trigger", message, StringComparison.Ordinal);
            Assert.DoesNotContain("between 0 and 3", message, StringComparison.Ordinal);
        }

        [Fact]
        public void StaticImportDeclarationGrammarCompilesAllFlowsAndFixedRatePolicy()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class DeclarationGrammar
    {
        [FoxRun(""/phase183/default"")]
        private float _defaultValue;

        [FoxRun(""/phase183/publish"", Mode = Publish, Policy = FixedRate)]
        private float _publishedValue;

        [FoxRun(""/phase183/subscribe"", Mode = Subscribe, Policy = FixedRate)]
        private float _subscribedValue;

        [FoxRun(""/phase183/full-duplex"", Mode = PublishAndSubscribe,
            Policy = FixedRate, Encoding = FoxRunWireEncoding.Protobuf)]
        private float _sharedValue;
    }
}");

            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);
            var generated = string.Join(
                Environment.NewLine,
                result.GeneratedTrees.Select(tree => tree.GetText().ToString()));
            Assert.Contains("/phase183/publish", generated, StringComparison.Ordinal);
            Assert.Contains("/phase183/subscribe", generated, StringComparison.Ordinal);
            Assert.Contains("/phase183/full-duplex", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void UpdatePolicySeparatesFreshInputFromStaleTimerTicks()
        {
            Assert.True(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change, 1d, false, false, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change, 2d, true, false, 1d, 0d));
            Assert.True(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.ChangeOrInterval, 3d, true, false, 1d, 2d));

            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.FixedRate, false, true, false, 3d, 1d, 0d));
            Assert.True(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, true, false, false, 3d, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, true, true, false, 3d, 1d, 0d));
            Assert.True(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.ChangeOrInterval, true, true, false, 3d, 1d, 2d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.ChangeOrInterval, false, true, false, 4d, 1d, 2d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Trigger, true, false, true, 1d, 0d, 0d));
        }

        [Fact]
        public void UpdatePolicyFailsClosedForUnknownPolicyAndNonFiniteClock()
        {
            Assert.False(FoxRunUpdatePolicy.ShouldPublish(
                (FoxRunPolicy)0, 1d, false, true, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                (FoxRunPolicy)99, true, false, true, 1d, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.FixedRate, double.NaN, false, true, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.FixedRate, true, false, true, double.PositiveInfinity, 0d, 0d));
        }

        [Fact]
        public void FoxRunWireEncodingMembersAndValuesRemainStable()
        {
            var values = Enum.GetValues(typeof(FoxRunWireEncoding))
                .Cast<FoxRunWireEncoding>()
                .ToArray();

            Assert.Equal(
                new[]
                {
                    FoxRunWireEncoding.Inherit,
                    FoxRunWireEncoding.Protobuf,
                    FoxRunWireEncoding.Json
                },
                values);
            Assert.Equal(0, (int)FoxRunWireEncoding.Inherit);
            Assert.Equal(1, (int)FoxRunWireEncoding.Protobuf);
            Assert.Equal(2, (int)FoxRunWireEncoding.Json);
        }

        [Fact]
        public void FoxRunWirePolicyDefaultsToInheritAcrossRegularAndAggregateDeclarations()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var encodingType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunWireEncoding");

            Assert.NotNull(encodingType);
            Assert.True(encodingType.IsEnum);
            var inherit = Enum.Parse(encodingType, "Inherit");

            var regularEncoding = typeof(FoxRunAttribute).GetProperty("Encoding");
            var regularFieldNumber = typeof(FoxRunAttribute).GetProperty("ProtobufFieldNumber");
            var aggregateEncoding = typeof(FoxRunMessageAttribute).GetProperty("Encoding");
            var aggregateFieldNumber = typeof(FoxRunFieldAttribute).GetProperty("ProtobufFieldNumber");

            Assert.NotNull(regularEncoding);
            Assert.NotNull(regularFieldNumber);
            Assert.NotNull(aggregateEncoding);
            Assert.NotNull(aggregateFieldNumber);
            Assert.Equal(inherit, regularEncoding.GetValue(new FoxRunAttribute("/phase175/regular")));
            Assert.Equal(0, regularFieldNumber.GetValue(new FoxRunAttribute("/phase175/regular")));
            Assert.Equal(inherit, aggregateEncoding.GetValue(new FoxRunMessageAttribute("/phase175/aggregate")));
            Assert.Equal(0, aggregateFieldNumber.GetValue(new FoxRunFieldAttribute()));
        }

        [Fact]
        public void SubscribeMembersStayOutOfGeneratedPublishDispatch()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "CommandInput",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "_status", "field", "System.String",
                        true, false, "", "/phase157/status", 10f, "",
                        0, 0f, 0f, "UnitTest", 0, ""),
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                        true, false, "", "/phase157/cmd_vel", 10f, "",
                        0, 0f, 0f, "UnitTest", 1, "",
                        mode: (int)FoxRunFlow.Subscribe)
                });

            var source = FoxgloveSourceEmitter.EmitClass(type);

            Assert.Contains("FoxgloveLog_TopicCount => 1", source, StringComparison.Ordinal);
            Assert.Contains("/phase157/status", source, StringComparison.Ordinal);
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorLowersSubscribeModeWithoutPublishingTopic()
        {
            var source = @"
using UnityEngine;
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/status"")]
        private string _status;

        [FoxRun(""/phase157/cmd_vel"", Mode = FoxRunFlow.Subscribe)]
        private Vector3 _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .SingleOrDefault(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.True(
                generated != null,
                "Expected CommandInput generated source. Diagnostics: " +
                string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            Assert.Contains("/phase157/status", generated, StringComparison.Ordinal);
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract(1)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsTypedSubscribeAssignment()
        {
            var source = @"
using UnityEngine;
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/cmd_vel"", Mode = FoxRunFlow.Subscribe)]
        private Vector3 _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("partial class CommandInput : IFoxgloveInputSource", generated, StringComparison.Ordinal);
            Assert.Contains("int IFoxgloveInputSource.FoxgloveInput_TopicCount => 1", generated, StringComparison.Ordinal);
            Assert.Contains(
                "new FoxgloveInputTopicInfo(/phase157/cmd_vel",
                generated.Replace("\"", string.Empty, StringComparison.Ordinal),
                StringComparison.Ordinal);
            Assert.Contains("policy: FoxRunPolicy.FixedRate", generated, StringComparison.Ordinal);
            Assert.Contains("hasExplicitRateHz: false", generated, StringComparison.Ordinal);
            Assert.Contains("string.Equals(encoding, \"protobuf\", global::System.StringComparison.OrdinalIgnoreCase)", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"incomingVelocity\", out global::UnityEngine.Vector3 __value", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundProtobuf.TryRead", generated, StringComparison.Ordinal);
            Assert.Contains("__foxRunInputPending_0 = __value", generated, StringComparison.Ordinal);
            Assert.Contains("this._incomingVelocity = __foxRunInputPending_0", generated, StringComparison.Ordinal);
            Assert.Contains("IFoxgloveInputSource.FoxgloveInput_Flush", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("IFoxgloveLogSource", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsPublishAndSubscribeOnBothSurfaces()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class SharedState
    {
        [FoxRun(""/phase157/state"", Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunWireEncoding.Protobuf)]
        private string _state;
    }
}";
            var result = RunGenerator(source);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class SharedState", StringComparison.Ordinal));

            Assert.Contains("IFoxgloveLogSource", generated, StringComparison.Ordinal);
            Assert.Contains("IFoxgloveInputSource", generated, StringComparison.Ordinal);
            Assert.Contains("__foxRunInputPending_0 = __value", generated, StringComparison.Ordinal);
            Assert.Contains("this._state = __foxRunInputPending_0", generated, StringComparison.Ordinal);
            Assert.Contains("__foxRunSuppressNextPublish_0 = true", generated, StringComparison.Ordinal);
            Assert.Contains("if (__foxRunSuppressNextPublish_0)", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorReadsFoxRunFlowFromSemanticConstant()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        private const FoxRunFlow Inbound = FoxRunFlow.Subscribe;

        [FoxRun(""/phase157/cmd_vel"", Mode = Inbound)]
            private float _incomingVelocity;
    }
}";
            var result = RunGenerator(source);
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .SingleOrDefault(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.True(
                generated != null,
                "Expected CommandInput generated source. Diagnostics: " +
                string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            Assert.Contains("FoxgloveInputTopicInfo(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("mgr.PublishJson(\"/phase157/cmd_vel\"", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorEmitsPrimitiveInboundAssignmentsWithValidTypeName()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/target-speed"", Mode = FoxRunFlow.Subscribe)]
        private float requestedTargetSpeed;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"requestedTargetSpeed\", out float __value", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("out global::float __value", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorScopesInboundAssignmentLocalsPerTopic()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/shared-state"", Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunWireEncoding.Json)]
        private float sharedState;

        [FoxRun(""/phase157/target-speed"", Mode = FoxRunFlow.Subscribe)]
        private float requestedTargetSpeed;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString().Replace("\r\n", "\n", StringComparison.Ordinal))
                .Single(text => text.Contains("partial class CommandInput", StringComparison.Ordinal));

            Assert.Contains("case 0:\n                    {", generated, StringComparison.Ordinal);
            Assert.Contains("case 1:\n                    {", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"requestedTargetSpeed\", out float __value", generated, StringComparison.Ordinal);
            Assert.Contains("FoxRunInboundJson.TryRead(payload, \"sharedState\", out float __value", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorRejectsBidirectionalInheritedWireEncoding()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class SharedState
    {
        [FoxRun(""/phase176/ambiguous-state"", Mode = FoxRunFlow.PublishAndSubscribe)]
        private float sharedState;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN401");
        }

        [Fact]
        public void RoslynAttributeDataExposesFoxRunFlowConstant()
        {
            var compilation = CreateCompilation(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        private const FoxRunFlow Inbound = FoxRunFlow.Subscribe;

        [FoxRun(""/phase157/cmd_vel"", Mode = Inbound)]
        private float _incomingVelocity;
    }
}");
            Assert.DoesNotContain(
                compilation.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var member = compilation.GetTypeByMetadataName("Demo.CommandInput")
                .GetMembers("_incomingVelocity")
                .Single();
            var mode = member.GetAttributes()
                .Single()
                .NamedArguments
                .Single(argument => argument.Key == "Mode")
                .Value;

            Assert.Equal(2, Convert.ToInt32(mode.Value));
        }

        [Fact]
        public void RoslynGeneratorPreservesDeclaredWireEncodingAndFieldNumberInDescriptor()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class WireState
    {
        [FoxRun(""/phase175/wire_state"", Encoding = FoxRunWireEncoding.Protobuf, ProtobufFieldNumber = 17)]
        private int _count;
    }
}");
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"encoding\\\":\\\"protobuf\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"protobufFieldNumber\\\":17", descriptor, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorRejectsInvalidDeclaredWireEncoding()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class WireState
    {
        [FoxRun(""/phase175/wire_state"", Encoding = (FoxRunWireEncoding)99)]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN602");
        }

        [Fact]
        public void RoslynGeneratorRejectsTriggerWithExplicitRateUsingReservedDiagnostic()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerState
    {
        [FoxRun(""/phase183/trigger"", Policy = Trigger, RateHz = 10f)]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN609");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN000");
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
            Assert.Contains("\\\"protobufFieldNumber\\\":23", descriptor, StringComparison.Ordinal);
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
        [FoxRun(""/phase175/dto"", Encoding = FoxRunWireEncoding.Protobuf)]
        private VehicleTelemetry _telemetry;
    }
}");

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN006");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
        [FoxRun(""/phase175/telemetry_in"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
        private Telemetry _incomingTelemetry;

        [FoxRun(""/phase175/samples_in"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
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
        [FoxRun(""/phase175/readonly_dto"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
        private Command _incomingCommand;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN200");
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
        public List<int> Values { get; set; }
        public List<CommandKind> Kinds { get; set; }
    }

    public partial class CommandInput
    {
        [FoxRun(""/phase175/commands"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
        private Command _incomingCommand;

        [FoxRun(""/phase175/ints"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
        private int[] _incomingInts;

        [FoxRun(""/phase175/kind"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunWireEncoding.Protobuf)]
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
                    true, false, "", "/phase175/wire_state", "", 10f, 0, 0f, 0f, 0, "",
                    encoding: (int)FoxRunWireEncoding.Protobuf,
                    protobufFieldNumber: 17)
            });
            var member = model.Types.Single().Members.Single();

            Assert.Equal("protobuf", member.Encoding);
            Assert.Equal(17, member.ProtobufFieldNumber);
        }

        [Fact]
        public void DescriptorJsonIncludesExplicitFoxRunFlow()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    1, 0f, 0f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);

            Assert.Contains("\"mode\":\"Subscribe\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void DescriptorComparerTreatsFoxRunFlowAsSemanticState()
        {
            var publish = ModelWithMode(FoxRunFlow.Publish);
            var subscribe = ModelWithMode(FoxRunFlow.Subscribe);

            var comparison = FoxRunGenerationDescriptorComparer.Compare(publish, subscribe);

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains("mode", StringComparison.Ordinal));
        }

        [Fact]
        public void DescriptorComparerTreatsMatchingNanFloatsAsSameValue()
        {
            var compare = typeof(FoxRunGenerationDescriptorComparer).GetMethod(
                "CompareSemantic",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string), typeof(float), typeof(float), typeof(List<string>) },
                null);
            Assert.NotNull(compare);
            var diffs = new List<string>();

            compare.Invoke(null, new object[] { "member", "rateHz", float.NaN, float.NaN, diffs });

            Assert.Empty(diffs);
        }

        [Fact]
        public void FoxRunJsonSchemaBuilderAcceptsDecimalFieldsAsNumbers()
        {
            var contract = new FoxRunSchemaContractInfo(
                "Demo.DecimalState",
                "/phase173/decimal",
                "",
                "json",
                "contract",
                "binding",
                "policy",
                "FixedRate",
                10f,
                0f,
                0f,
                new[]
                {
                    new FoxRunSchemaFieldInfo("amount", "_amount", "field", "decimal", false, false)
                });

            var json = FoxRunJsonSchemaBuilder.Build(contract);

            Assert.Contains("\"amount\":{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}", json, StringComparison.Ordinal);
        }

        [Fact]
        public void ManifestRecordsInboundFlowWithoutChangingDefaultCanonicalShape()
        {
            var publish = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.Publish)
            });
            var subscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.Subscribe)
            });
            var publishAndSubscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.PublishAndSubscribe)
            });

            var publishJson = FoxRunManifestJsonWriter.WriteCanonical(publish);
            var subscribeJson = FoxRunManifestJsonWriter.WriteCanonical(subscribe);
            var publishAndSubscribeJson = FoxRunManifestJsonWriter.WriteCanonical(publishAndSubscribe);

            Assert.DoesNotContain("\"flow\"", publishJson, StringComparison.Ordinal);
            Assert.Contains("\"flow\":\"Subscribe\"", subscribeJson, StringComparison.Ordinal);
            Assert.Contains("\"flow\":\"PublishAndSubscribe\"", publishAndSubscribeJson, StringComparison.Ordinal);
            Assert.NotEqual(
                publish.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                subscribe.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
            Assert.NotEqual(
                publish.Sections.FoxRun.Types[0].Contracts[0].ContractHash,
                publishAndSubscribe.Sections.FoxRun.Types[0].Contracts[0].ContractHash);
        }

        [Fact]
        public void ManifestExpandsInheritedWirePolicyIntoJsonAndProtobufContracts()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "WireState",
                    "_count",
                    "field",
                    "System.Int32",
                    true,
                    false,
                    "",
                    "/phase175/wire_state",
                    10f,
                    "Demo.WireState",
                    1,
                    0f,
                    0f,
                    encoding: (int)FoxRunWireEncoding.Inherit,
                    protobufFieldNumber: 17)
            });

            var contracts = manifest.Sections.FoxRun.Types.Single().Contracts;

            Assert.Equal(new[] { "json", "protobuf" }, contracts.Select(contract => contract.Encoding).OrderBy(encoding => encoding));
            Assert.Equal(0, contracts.Single(contract => contract.Encoding == "json").Fields.Single().ProtobufFieldNumber);
            Assert.Equal(17, contracts.Single(contract => contract.Encoding == "protobuf").Fields.Single().ProtobufFieldNumber);
        }

        [Fact]
        public void ManifestRejectsUnknownPolicy()
        {
            var member = new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                "_incomingVelocity",
                "field",
                "UnityEngine.Vector3",
                true,
                false,
                "",
                "/phase157/cmd_vel",
                10f,
                "",
                99,
                0f,
                0f);

            var ex = Assert.Throws<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[] { member }));

            Assert.Contains("Policy", ex.Message, StringComparison.Ordinal);
            Assert.Contains("1..4", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ManifestGroupsIdenticalContractsWithOrdinalKeys()
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember("_speed", "/phase157/state", "speed"),
                ManifestMember("_state", "/phase157/state", "state"),
                ManifestMember("_speedUpper", "/phase157/State", "speedUpper")
            });

            var contracts = manifest.Sections.FoxRun.Types[0].Contracts;

            Assert.Equal(2, contracts.Count);
            Assert.Contains(contracts, contract => contract.Topic == "/phase157/state" && contract.Fields.Count == 2);
            Assert.Contains(contracts, contract => contract.Topic == "/phase157/State" && contract.Fields.Count == 1);
        }

        [Fact]
        public void ManifestPolicyHashInputCanonicalizesNonFiniteFloats()
        {
            var hashInput = FoxRunManifestJsonWriter.WritePolicyHashInput(new FoxRunManifestPolicy(
                "Change",
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity));

            Assert.Contains("\"rateHz\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"changeEpsilon\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"forceIntervalSeconds\":0", hashInput, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundValidationRejectsJsonArraysWithoutLegacyIgnoredOptionWarning()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase157/samples", 10f, "",
                    1, 0.1f, 2f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200" && diagnostic.Severity == "Error");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN201");
        }

        [Fact]
        public void InboundValidationAllowsExplicitProtobufArrays()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase175/samples", 10f, "",
                    1, 0.1f, 2f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe,
                    encoding: "protobuf",
                    protobufTypeShape: FoxRunProtobufTypeShape.Canonical("float32"))
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200" && diagnostic.Severity == "Error");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN201");
        }

        [Fact]
        public void RoslynGeneratorDoesNotEmitNullableProtobufWriterConversionErrors()
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
    public sealed class OptionalPayload
    {
        public int? OptionalCount;
        public List<int?> Samples = new List<int?>();
    }

    public partial class NullablePublisher
    {
        [FoxRun(""/phase175/optional-root"", Encoding = FoxRunWireEncoding.Protobuf)]
        public int? OptionalRoot;

        [FoxRun(""/phase175/optional-payload"", Encoding = FoxRunWireEncoding.Protobuf)]
        public OptionalPayload Payload = new OptionalPayload();
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Id == "CS1503");
        }

        [Fact]
        public void RoslynGeneratorRejectsReadOnlyInboundProperty()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class CommandInput
    {
        [FoxRun(""/phase157/cmd"", Mode = FoxRunFlow.Subscribe)]
        private float IncomingCommand => 0;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN203");
        }

        [Fact]
        public void SourceEmitterRejectsMalformedInboundMembers()
        {
            var type = new FoxRunGenerationType(
                "Demo",
                "CommandInput",
                new[]
                {
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "", "field", "System.Single",
                        false, false, "", "/phase173/input", 10f, "",
                        0, 0f, 0f, "UnitTest", 0, "",
                        mode: (int)FoxRunFlow.Subscribe)
                });

            var ex = Assert.Throws<ArgumentException>(() => FoxgloveSourceEmitter.EmitClass(type));

            Assert.Contains("Input TopicMember has empty MemberName", ex.Message, StringComparison.Ordinal);
        }

        private static FoxRunGenerationModel ModelWithMode(FoxRunFlow mode)
        {
            return FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    1, 0f, 0f, "UnitTest", 0, "",
                    mode: (int)mode)
            });
        }

        private static FoxRunManifestMember ManifestMember(FoxRunFlow mode)
        {
            return new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                "_incomingVelocity",
                "field",
                "UnityEngine.Vector3",
                true,
                false,
                "",
                "/phase157/cmd_vel",
                10f,
                "",
                1,
                0f,
                0f,
                flow: (int)mode);
        }

        private static FoxRunManifestMember ManifestMember(string memberName, string topic, string jsonFieldName)
        {
            return new FoxRunManifestMember(
                "Demo",
                "CommandInput",
                memberName,
                "field",
                "System.Single",
                true,
                false,
                "",
                topic,
                10f,
                "",
                1,
                0f,
                0f,
                jsonFieldName: jsonFieldName);
        }

        private static MetadataReference[] BasicReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
            var trusted = trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path));

            return trusted
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(UnityEngine.Vector3).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location)
                })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var compilation = CreateCompilation(source);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static Compilation RunGeneratorAndUpdateCompilation(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            return outputCompilation;
        }

        private static CSharpCompilation CreateCompilation(string source)
        {
            return CSharpCompilation.Create(
                "Phase157GeneratorProbe",
                new[] { CSharpSyntaxTree.ParseText(source) },
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
