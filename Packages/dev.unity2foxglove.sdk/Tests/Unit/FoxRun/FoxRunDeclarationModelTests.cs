// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class FoxRunDeclarationModelTests
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
                new[] { "FixedRate", "Change", "Trigger" },
                Enum.GetNames(policyType));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(flowType, "Publish")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(flowType, "Subscribe")));
            Assert.Equal(3, Convert.ToInt32(Enum.Parse(flowType, "PublishAndSubscribe")));
            Assert.Equal(1, Convert.ToInt32(Enum.Parse(policyType, "FixedRate")));
            Assert.Equal(2, Convert.ToInt32(Enum.Parse(policyType, "Change")));
            Assert.Equal(4, Convert.ToInt32(Enum.Parse(policyType, "Trigger")));
        }

        [Fact]
        public void InvalidPolicyDiagnosticNamesTheSupportedPolicies()
        {
            var message = Diags.InvalidPolicy.MessageFormat.ToString();

            Assert.Contains("FixedRate", message, StringComparison.Ordinal);
            Assert.Contains("Change", message, StringComparison.Ordinal);
            Assert.Contains("Trigger", message, StringComparison.Ordinal);
            Assert.DoesNotContain("ChangeOrInterval", message, StringComparison.Ordinal);
            Assert.DoesNotContain("between 0 and 3", message, StringComparison.Ordinal);
        }

        [Fact]
        public void ShortSchedulingDeclarationGrammarCompiles()
        {
            var output = CreateCompilation(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class SchedulingGrammar
    {
        private bool TelemetryEnabled => true;

        [FoxRun(""/phase184/change"", Policy = Change, Hz = 10f,
            Tolerance = 0.01f, OnlyIf = nameof(TelemetryEnabled))]
        private float _changed;

        [FoxRun(""/phase184/subscribe"", Mode = Subscribe, Hz = 20f,
            OnlyIf = nameof(TelemetryEnabled))]
        private int _subscribed;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
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
            Policy = FixedRate, Encoding = FoxRunEncoding.Protobuf)]
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
        public void ChangeWithoutHzPublishesAndAppliesOnlyFreshSemanticChanges()
        {
            Assert.True(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change, 1d, false, false, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change, 2d, true, false, 1d, 0d));

            Assert.True(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, true, false, false, 3d, 0d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, true, true, false, 3d, 1d, 0d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, false, true, true, 3d, 1d, 0d));
        }

        [Fact]
        public void ChangeWithHzProvidesPublishHeartbeatAndFreshDuplicateRefresh()
        {
            Assert.True(FoxRunUpdatePolicy.ShouldPublish(
                FoxRunPolicy.Change, 3d, true, false, 1d, 2d));
            Assert.True(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, true, true, false, 3d, 1d, 2d));
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.Change, false, true, false, 4d, 1d, 2d));
        }

        [Fact]
        public void FixedRateAndTriggerRetainDirectionIndependentDecisions()
        {
            Assert.False(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.FixedRate, false, true, false, 3d, 1d, 0d));
            Assert.True(FoxRunUpdatePolicy.ShouldApply(
                FoxRunPolicy.FixedRate, true, true, false, 3d, 1d, 0d));
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
        public void FoxRunEncodingMembersAndValuesRemainStable()
        {
            var values = Enum.GetValues(typeof(FoxRunEncoding))
                .Cast<FoxRunEncoding>()
                .ToArray();

            Assert.Equal(
                new[]
                {
                    FoxRunEncoding.Protobuf,
                    FoxRunEncoding.JSON
                },
                values);
            Assert.Equal(0, (int)(FoxRunEncoding)0);
            Assert.Equal(1, (int)FoxRunEncoding.Protobuf);
            Assert.Equal(2, (int)FoxRunEncoding.JSON);
        }

        [Fact]
        public void FoxRunEncodingOmissionUsesAnInternalZeroSentinel()
        {
            var assembly = typeof(FoxRunAttribute).Assembly;
            var encodingType = assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunEncoding");

            Assert.NotNull(encodingType);
            Assert.True(encodingType.IsEnum);
            var regularEncoding = typeof(FoxRunAttribute).GetProperty("Encoding");
            var regularFieldNumber = typeof(FoxRunAttribute).GetProperty("ProtobufFieldNumber");
            var aggregateEncoding = typeof(FoxRunMessageAttribute).GetProperty("Encoding");
            var aggregateFieldNumber = typeof(FoxRunFieldAttribute).GetProperty("ProtobufFieldNumber");

            Assert.NotNull(regularEncoding);
            Assert.NotNull(regularFieldNumber);
            Assert.NotNull(aggregateEncoding);
            Assert.NotNull(aggregateFieldNumber);
            Assert.DoesNotContain("Inherit", Enum.GetNames(encodingType));
            Assert.Equal((FoxRunEncoding)0, regularEncoding.GetValue(new FoxRunAttribute("/phase175/regular")));
            Assert.Equal(0, regularFieldNumber.GetValue(new FoxRunAttribute("/phase175/regular")));
            Assert.Equal((FoxRunEncoding)0, aggregateEncoding.GetValue(new FoxRunMessageAttribute("/phase175/aggregate")));
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
                        0, 0f, "UnitTest", 0, ""),
                    new FoxRunGenerationMember(
                        "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                        true, false, "", "/phase157/cmd_vel", 10f, "",
                        0, 0f, "UnitTest", 1, "",
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
            Assert.Contains("hasExplicitHz: false", generated, StringComparison.Ordinal);
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
        [FoxRun(""/phase157/state"", Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunEncoding.Protobuf)]
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
        public void TriggerMethodsAreDirectionSpecificAndExposeBulkOperations()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class DirectionalTriggers
    {
        [FoxRun(""/phase184/publish"", Policy = Trigger)]
        private int _outbound;

        [FoxRun(""/phase184/subscribe"", Mode = Subscribe, Policy = Trigger)]
        private int _inbound;

        [FoxRun(""/phase184/full-duplex"", Mode = PublishAndSubscribe,
            Policy = Trigger, Encoding = FoxRunEncoding.Protobuf)]
        private int _shared;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class DirectionalTriggers", StringComparison.Ordinal));
            var methods = CSharpSyntaxTree.ParseText(generated)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .ToArray();

            AssertGeneratedBooleanMethod(methods, "FoxRun_Publish_outbound");
            Assert.DoesNotContain(methods, method =>
                method.Identifier.ValueText == "FoxRun_Apply_outbound");
            AssertGeneratedBooleanMethod(methods, "FoxRun_Apply_inbound");
            Assert.DoesNotContain(methods, method =>
                method.Identifier.ValueText == "FoxRun_Publish_inbound");
            AssertGeneratedBooleanMethod(methods, "FoxRun_Publish_shared");
            AssertGeneratedBooleanMethod(methods, "FoxRun_Apply_shared");
            AssertGeneratedBooleanMethod(methods, "FoxRun_PublishAll");
            AssertGeneratedBooleanMethod(methods, "FoxRun_ApplyAll");
            Assert.DoesNotContain(methods, method =>
                method.Identifier.ValueText.StartsWith("FoxRun_Trigger_", StringComparison.Ordinal));
            Assert.DoesNotContain(methods, method =>
                method.Identifier.ValueText == "FoxRun_TriggerAll");
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
        [FoxRun(""/phase157/shared-state"", Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunEncoding.JSON)]
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
        public void RoslynGeneratorAllowsBidirectionalDirectionalProfileEncodings()
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

            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN401");
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();
            Assert.Contains("\\\"encoding\\\":\\\"inherit\\\"", descriptor, StringComparison.Ordinal);
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
        public void RoslynGeneratorPreservesDeclaredTargetsEncodingAndFieldNumberInDescriptor()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public sealed class WirePayload
    {
        public int Count { get; set; }
    }

    public partial class WireState
    {
        [FoxRun(
            ""/phase175/wire_state"",
            Targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge,
            Encoding = FoxRunEncoding.Protobuf,
            ProtobufFieldNumber = 17)]
        private WirePayload _payload;
    }
}");
            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains("\\\"encoding\\\":\\\"protobuf\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains("\\\"targets\\\":\\\"foxglove,ros2-bridge\\\"", descriptor, StringComparison.Ordinal);
            Assert.Contains(
                "\\\"explicitArguments\\\":\\\"Encoding,Targets,ProtobufFieldNumber\\\"",
                descriptor,
                StringComparison.Ordinal);
            Assert.Contains("\\\"protobufFieldNumber\\\":17", descriptor, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorRejectsInvalidDeclaredEncoding()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class WireState
    {
        [FoxRun(""/phase175/wire_state"", Encoding = (FoxRunEncoding)99)]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN602");
        }

        [Fact]
        public void RoslynGeneratorRejectsTriggerWithExplicitHzUsingReservedDiagnostic()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerState
    {
        [FoxRun(""/phase184/trigger"", Policy = Trigger, Hz = 10f)]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN609");
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN000");
        }

        [Theory]
        [InlineData("0f")]
        [InlineData("-1f")]
        [InlineData("float.NaN")]
        [InlineData("float.PositiveInfinity")]
        public void RoslynGeneratorRejectsTriggerWithEveryExplicitHzValue(string hzExpression)
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

namespace Demo
{
    public partial class TriggerState
    {
        [FoxRun(""/phase184/trigger"", Policy = Trigger, Hz = " + hzExpression + @")]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN609");
        }

        [Fact]
        public void RoslynGeneratorSupportsZeroArgumentBoolMethodOnlyIf()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        private bool CanSend() => true;

        [FoxRun(""/phase184/conditional"", OnlyIf = nameof(CanSend))]
        private int _count;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class ConditionalState", StringComparison.Ordinal));

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN015" || diagnostic.Id == "FOXRUN016");
            Assert.Contains("CanSend()", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void RoslynGeneratorRejectsExplicitEmptyOnlyIf()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        [FoxRun(""/phase184/conditional"", OnlyIf = """")]
        private int _count;
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
        }

        [Fact]
        public void RoslynAndReflectionRejectWhitespacePaddedOnlyIf()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConditionalState
    {
        private bool Enabled => true;

        [FoxRun(""/phase184/conditional"", OnlyIf = "" Enabled "")]
        private int _count;
    }
}");
            var snapshot = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.WhitespaceCondition)));
            var onlyIf = ReadField<string>(snapshot, "OnlyIf");
            var presence = (FoxRunNamedArgumentPresence)ReadInt64Field(
                snapshot,
                "NamedArgumentPresence");
            var reflection = FoxRunReflectionGenerationModelLowerer.Lower(
                new[]
                {
                    new FoxRunReflectionGenerationMember(
                        "Demo", "ReflectionArgumentsFixture", "WhitespaceCondition",
                        "field", "System.Single", "float",
                        true, false, "", "/phase184/reflection/whitespace-condition",
                        "", -1f, 1, 0f, 0, "",
                        onlyIf: onlyIf,
                        namedArgumentPresence: presence,
                        conditionMemberKind: FoxRunConditionMemberKind.Missing)
                });

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(" Enabled ", onlyIf);
            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(reflection),
                diagnostic => diagnostic.Id == "FOXRUN015");
        }

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
        [FoxRun(""/phase175/dto"", Encoding = FoxRunEncoding.Protobuf)]
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
        [FoxRun(""/phase175/commands"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private Command _incomingCommand;

        [FoxRun(""/phase175/ints"", Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.Protobuf)]
        private int[] _incomingInts;

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
            Assert.Equal(17, member.ProtobufFieldNumber);
        }

        [Fact]
        public void ReflectionScannerPreservesOmittedAndExplicitDefaultNamedArguments()
        {
            var omitted = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.Omitted)));
            var explicitDefaults = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.ExplicitDefaults)));
            const long scheduling = (1L << 0) | (1L << 1) | (1L << 2);
            const long existingAxes = (1L << 4) | (1L << 5) | (1L << 6)
                                      | (1L << 7) | (1L << 8) | (1L << 10);

            Assert.Equal(0L, ReadInt64Field(omitted, "NamedArgumentPresence") & scheduling);
            Assert.Equal(0L, ReadInt64Field(omitted, "NamedArgumentPresence") & existingAxes);

            Assert.Equal(scheduling, ReadInt64Field(explicitDefaults, "NamedArgumentPresence") & scheduling);
            Assert.Equal(existingAxes, ReadInt64Field(explicitDefaults, "NamedArgumentPresence") & existingAxes);
            Assert.Equal(-1f, ReadField<float>(explicitDefaults, "Hz"));
            Assert.Equal(0f, ReadField<float>(explicitDefaults, "Tolerance"));
            Assert.Equal(string.Empty, ReadField<string>(explicitDefaults, "OnlyIf"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Policy"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Mode"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Encoding"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Source"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Targets"));
            Assert.Equal(0, ReadField<int>(explicitDefaults, "Ros2Qos"));
        }

        [Fact]
        public void ReflectionScannerPreservesInvalidExplicitEnumCast()
        {
            var invalid = ReadReflectionAttributeSnapshot(
                typeof(ReflectionArgumentsFixture).GetField(
                    nameof(ReflectionArgumentsFixture.InvalidPolicy)));

            Assert.Equal(99, ReadField<int>(invalid, "Policy"));
            Assert.NotEqual(
                0L,
                ReadInt64Field(invalid, "NamedArgumentPresence") & (1L << 4));
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
        public void RoslynDescriptorRecordsOnlyExplicitNewNamedArguments()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class PresenceProbe
    {
        private bool Enabled => true;

        [FoxRun(""/phase184/omitted"")]
        private float _omitted;

        [FoxRun(""/phase184/explicit"", Hz = 10f, Tolerance = 0f,
            OnlyIf = nameof(Enabled), Policy = FoxRunPolicy.FixedRate,
            Mode = FoxRunFlow.PublishAndSubscribe, Encoding = FoxRunEncoding.Protobuf,
            Source = FoxRunEndpoint.Foxglove, Targets = FoxRunEndpoint.Foxglove,
            Ros2Qos = FoxRunRos2QosPreset.Inherit)]
        private float _explicit;
    }
}");
            var descriptor = result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

            Assert.Contains(
                "\\\"explicitArguments\\\":\\\"Hz,Tolerance,OnlyIf,Policy,Mode,Encoding,Source,Targets,Ros2Qos\\\"",
                descriptor,
                StringComparison.Ordinal);
            Assert.Contains("\\\"explicitArguments\\\":\\\"\\\"", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"rateHz\\\"", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"changeEpsilon\\\"", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"forceIntervalSeconds\\\"", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"when\\\"", descriptor, StringComparison.Ordinal);
            Assert.DoesNotContain("\\\"unless\\\"", descriptor, StringComparison.Ordinal);
        }

        [Fact]
        public void DescriptorJsonIncludesExplicitFoxRunFlow()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingVelocity", "field", "UnityEngine.Vector3",
                    true, false, "", "/phase157/cmd_vel", 10f, "",
                    1, 0f, "UnitTest", 0, "",
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
                    encoding: (int)(FoxRunEncoding)0,
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
                0f);

            var ex = Assert.Throws<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[] { member }));

            Assert.Contains("Policy", ex.Message, StringComparison.Ordinal);
            Assert.Contains("FixedRate, Change, or Trigger", ex.Message, StringComparison.Ordinal);
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
                float.PositiveInfinity));

            Assert.Contains("\"hz\":0", hashInput, StringComparison.Ordinal);
            Assert.Contains("\"tolerance\":0", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"rateHz\"", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"changeEpsilon\"", hashInput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"forceIntervalSeconds\"", hashInput, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundValidationRejectsJsonArraysWithoutLegacyIgnoredOptionWarning()
        {
            var model = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "CommandInput", "_incomingSamples", "field", "System.Single[]",
                    false, true, "System.Single", "/phase157/samples", 10f, "",
                    1, 0.1f, "UnitTest", 0, "",
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
                    1, 0.1f, "UnitTest", 0, "",
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
        [FoxRun(""/phase175/optional-root"", Encoding = FoxRunEncoding.Protobuf)]
        public int? OptionalRoot;

        [FoxRun(""/phase175/optional-payload"", Encoding = FoxRunEncoding.Protobuf)]
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
                        0, 0f, "UnitTest", 0, "",
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
                    1, 0f, "UnitTest", 0, "",
                    mode: (int)mode)
            });
        }

        private static void AssertGeneratedBooleanMethod(
            IEnumerable<MethodDeclarationSyntax> methods,
            string methodName)
        {
            var method = Assert.Single(
                methods,
                candidate => candidate.Identifier.ValueText == methodName);

            Assert.Contains(method.Modifiers, modifier =>
                modifier.IsKind(SyntaxKind.PublicKeyword));
            Assert.Equal("bool", method.ReturnType.ToString());
            Assert.Empty(method.ParameterList.Parameters);
        }

        private static object ReadReflectionAttributeSnapshot(FieldInfo field)
        {
            Assert.NotNull(field);
            var reader = typeof(FoxrunCodeGenerator).GetMethod(
                "ReadFoxRunAttributeSnapshots",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(reader);
            var snapshots = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                reader.Invoke(null, new object[] { field }));
            return Assert.Single(snapshots.Cast<object>());
        }

        private static T ReadField<T>(object value, string name)
        {
            var field = value.GetType().GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return Assert.IsType<T>(field.GetValue(value));
        }

        private static long ReadInt64Field(object value, string name)
        {
            var field = value.GetType().GetField(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return Convert.ToInt64(field.GetValue(value));
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

        private sealed class ReflectionArgumentsFixture
        {
            private bool Enabled => true;

            [FoxRun("/phase184/reflection/omitted")]
            public float Omitted;

        [FoxRun(
                "/phase184/reflection/explicit",
                Hz = -1f,
                Tolerance = 0f,
                OnlyIf = "",
                Policy = (FoxRunPolicy)0,
                Mode = (FoxRunFlow)0,
                Encoding = (FoxRunEncoding)0,
                Source = (FoxRunEndpoint)0,
                Targets = (FoxRunEndpoint)0,
                Ros2Qos = (FoxRunRos2QosPreset)0)]
            public float ExplicitDefaults;

            [FoxRun(
                "/phase184/reflection/invalid-policy",
                Policy = (FoxRunPolicy)99)]
            public float InvalidPolicy;

            [FoxRun(
                "/phase184/reflection/whitespace-condition",
                OnlyIf = " Enabled ")]
            public float WhitespaceCondition;
        }
    }
}
