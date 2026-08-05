// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class FoxRunDeclarationModelTests
    {
        [Fact]
        public void FoxRunAttributeDefaultsToPublishFlow()
        {
            var attr = new FoxRunAttribute("/phase157/default");

            Assert.Equal(FoxRunFlow.Publish, attr.Mode);
        }

        [Fact]
        public void GenerationMemberConstructorsInferOmittedAndExplicitJsonEncodingPresence()
        {
            var omitted = new[]
            {
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_first",
                    memberKind: "field",
                    rawTypeName: "System.Int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/first",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 0,
                    conditionalSymbols: ""),
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_second",
                    memberKind: "field",
                    rawObservedTypeName: "System.Int32",
                    emissionTypeName: "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/second",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 1,
                    conditionalSymbols: ""),
                new FoxRunGenerationMember(
                    ns: "Demo",
                    className: "Defaults",
                    memberName: "_third",
                    memberKind: "field",
                    rawObservedTypeName: "System.Int32",
                    emissionTypeName: "int",
                    canonicalType: "int32",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/phase184/defaults/third",
                    hz: -1f,
                    schemaName: "",
                    policy: 1,
                    tolerance: 0f,
                    hostKind: "UnitTest",
                    rawMemberOrder: 2,
                    conditionalSymbols: "")
            };
            var explicitJson = new FoxRunGenerationMember(
                ns: "Demo",
                className: "Defaults",
                memberName: "_json",
                memberKind: "field",
                rawTypeName: "System.Int32",
                isValueType: true,
                isArray: false,
                elementTypeName: "",
                topic: "/phase184/defaults/json",
                hz: -1f,
                schemaName: "",
                policy: 1,
                tolerance: 0f,
                hostKind: "UnitTest",
                rawMemberOrder: 3,
                conditionalSymbols: "",
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding);

            Assert.All(omitted, member =>
            {
                Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, member.Encoding);
                Assert.Equal(FoxRunNamedArgumentPresence.None, member.NamedArgumentPresence);
                Assert.False(member.HasNamedArgument(FoxRunNamedArgumentPresence.Encoding));
            });
            Assert.Equal(FoxRunGenerationDescriptorConstants.JsonEncoding, explicitJson.Encoding);
            Assert.True(explicitJson.HasNamedArgument(FoxRunNamedArgumentPresence.Encoding));
            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(
                    FoxRunGenerationModel.FromMembers(omitted.Append(explicitJson).ToArray())),
                diagnostic => diagnostic.Severity == "Error");
        }

        [Fact]
        public void SharedTopicMemberDefaultsAndBlankEncodingToInheritedContract()
        {
            var omitted = new FoxgloveSourceEmitter.TopicMember(
                "_omitted",
                "System.Int32",
                "/phase184/defaults/topic-member-omitted",
                10f,
                "");
            var blank = new FoxgloveSourceEmitter.TopicMember(
                "_blank",
                "System.Int32",
                "/phase184/defaults/topic-member-blank",
                10f,
                "",
                policy: (int)FoxRunPolicy.FixedRate,
                tolerance: 0f,
                encoding: " ");

            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, omitted.Encoding);
            Assert.Equal(FoxRunGenerationDescriptorConstants.InheritEncoding, blank.Encoding);
        }



        [Fact]
        public void GeneratedTopicMetadataDistinguishesInheritedAndExplicitPublishRates()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class PublishRates
    {
        [FoxRun(""/phase184/rate/inherited"")]
        public int Inherited;

        [FoxRun(""/phase184/rate/explicit"", Hz = 7f)]
        public int Explicit;

        [FoxRun(""/phase184/rate/mixed"")]
        public int MixedInherited;

        [FoxRun(""/phase184/rate/mixed"", Hz = 7f)]
        public int MixedExplicit;
    }
}");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class PublishRates", StringComparison.Ordinal));
            var topicLines = generated
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("new FoxgloveLogTopicInfo", StringComparison.Ordinal))
                .ToArray();
            var inherited = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/inherited\"", StringComparison.Ordinal));
            var explicitRate = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/explicit\"", StringComparison.Ordinal));
            var mixedRate = topicLines.Single(line =>
                line.Contains("\"/phase184/rate/mixed\"", StringComparison.Ordinal));

            Assert.Contains("hasExplicitHz: false", inherited, StringComparison.Ordinal);
            Assert.DoesNotContain("hasExplicitHz: false", explicitRate, StringComparison.Ordinal);
            Assert.Contains(", 7f,", mixedRate, StringComparison.Ordinal);
            Assert.DoesNotContain("hasExplicitHz: false", mixedRate, StringComparison.Ordinal);
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
                    FoxRunEncoding.JSON,
                    (FoxRunEncoding)3
                },
                values);
            Assert.Equal(0, (int)(FoxRunEncoding)0);
            Assert.Equal(1, (int)FoxRunEncoding.Protobuf);
            Assert.Equal(2, (int)FoxRunEncoding.JSON);
            Assert.Equal(3, (int)(FoxRunEncoding)3);
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
            var extracted = ExtractRoslynMemberData(
                source,
                "_incomingVelocity");
            var topic = Assert.Single(extracted.Topics);
            Assert.Null(topic.SubscribeTransportId);
            Assert.True(
                topic.GeneratesWebSocketCodec(topic.Mode));

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
            Assert.Contains("__FoxRunMarkRemoteApplied_0();", generated, StringComparison.Ordinal);
            Assert.Contains("if (!__foxRunRemoteOwned_0) return true;", generated, StringComparison.Ordinal);
            Assert.Contains("if (__remoteUnchanged) return false;", generated, StringComparison.Ordinal);
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
        public void RoslynGeneratorCompilesEveryAccessibleInheritedOnlyIfShape()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public class ConditionalBase
    {
        public bool PublicField;
        protected bool ProtectedProperty => true;
        protected internal bool ProtectedInternalMethod() => true;
        internal bool InternalField;
        private protected bool PrivateProtectedProperty => true;
    }

    public partial class ConditionalState : ConditionalBase
    {
        private bool CurrentPrivateMethod() => true;

        [FoxRun(""/phase184/conditional/public-field"", OnlyIf = ""PublicField"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _publicField;

        [FoxRun(""/phase184/conditional/protected-property"", OnlyIf = ""ProtectedProperty"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _protectedProperty;

        [FoxRun(""/phase184/conditional/protected-internal-method"", OnlyIf = ""ProtectedInternalMethod"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _protectedInternalMethod;

        [FoxRun(""/phase184/conditional/internal-field"", OnlyIf = ""InternalField"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _internalField;

        [FoxRun(""/phase184/conditional/private-protected-property"", OnlyIf = ""PrivateProtectedProperty"",
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _privateProtectedProperty;

        [FoxRun(""/phase184/conditional/current-private-method"", OnlyIf = nameof(CurrentPrivateMethod),
            Mode = FoxRunFlow.Subscribe, Encoding = FoxRunEncoding.JSON)]
        private int _currentPrivateMethod;
    }
}";
            var result = RunGenerator(source);

            Assert.DoesNotContain(
                result.Diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN015" || diagnostic.Id == "FOXRUN016");
            var generated = result.GeneratedTrees
                .Select(tree => tree.GetText().ToString())
                .Single(text => text.Contains("partial class ConditionalState", StringComparison.Ordinal));
            Assert.Contains("ProtectedProperty", generated, StringComparison.Ordinal);
            Assert.Contains("ProtectedInternalMethod()", generated, StringComparison.Ordinal);
            Assert.Contains("PrivateProtectedProperty", generated, StringComparison.Ordinal);
            Assert.Contains("CurrentPrivateMethod()", generated, StringComparison.Ordinal);
            Assert.DoesNotContain(
                RunGeneratorAndUpdateCompilation(source).GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        [Fact]
        public void RoslynAndReflectionRejectPrivateBaseOnlyIf()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public class ConditionalBase
    {
        private bool Hidden => true;
    }

    public partial class ConditionalState : ConditionalBase
    {
        [FoxRun(""/phase184/conditional/private-base"", OnlyIf = ""Hidden"")]
        private int _value;
    }
}");
            var reflection = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(
                FoxRunConditionMemberKind.Missing,
                reflection[nameof(ReflectionInheritedConditionFixture.PrivateBase)]);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void GeneratedBarePublisherObserverSideChannelCompilesWithoutCaptureSequenceState()
        {
            var output = RunGeneratorAndUpdateCompilation(@"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class BarePublisher
    {
        [FoxRun(""/phase184/bare-observer"")]
        private float _value;
    }
}");

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
#endif

        [Fact]
        public void ReflectionScannerMatchesAccessibleInheritedOnlyIfShapes()
        {
            var members = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Equal(
                FoxRunConditionMemberKind.Field,
                members[nameof(ReflectionInheritedConditionFixture.PublicFieldProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Property,
                members[nameof(ReflectionInheritedConditionFixture.ProtectedPropertyProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                members[nameof(ReflectionInheritedConditionFixture.ProtectedInternalMethodProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Field,
                members[nameof(ReflectionInheritedConditionFixture.InternalFieldProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Property,
                members[nameof(ReflectionInheritedConditionFixture.PrivateProtectedPropertyProbe)]);
            Assert.Equal(
                FoxRunConditionMemberKind.Method,
                members[nameof(ReflectionInheritedConditionFixture.CurrentPrivateMethodProbe)]);
        }

        [Fact]
        public void RoslynAndReflectionDoNotBypassInvalidInheritedOnlyIfShadow()
        {
            var roslyn = RunGenerator(@"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public class ConditionalGrandBase
    {
        protected bool Gate => true;
    }

    public class ConditionalBase : ConditionalGrandBase
    {
        public new int Gate;
    }

    public partial class ConditionalState : ConditionalBase
    {
        [FoxRun(""/phase184/conditional/invalid-shadow"", OnlyIf = ""Gate"")]
        private int _value;
    }
}");
            var reflection = ScanReflectionConditionKinds(typeof(ReflectionInheritedConditionFixture));

            Assert.Contains(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN016");
            Assert.DoesNotContain(roslyn.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN015");
            Assert.Equal(
                FoxRunConditionMemberKind.Invalid,
                reflection[nameof(ReflectionInheritedConditionFixture.InvalidShadowProbe)]);
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

        [Fact]
        [Trait("Phase", "186-A")]
        public void TransportProviderSelectionFailsClosedForInvalidDirection()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class InvalidProviderDirection
    {
        [FoxRun(""/phase186/publish"",
            SubscribeTransportId = ""foxglove.websocket"")]
        private int _publish;

        [FoxRun(""/phase186/subscribe"",
            Mode = FoxRunFlow.Subscribe,
            PublishTransportIds = new[] { ""foxglove.websocket"" })]
        private int _subscribe;
    }
}";
            var diagnostics = RunGenerator(source).Diagnostics
                .Where(diagnostic => diagnostic.Id == "FOXRUN621")
                .ToArray();

            Assert.Equal(2, diagnostics.Length);
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void TransportNeutralSystemDefaultDeliveryAxesAreAccepted()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class SystemDefaultDelivery
    {
        [FoxRun(""/phase186/system-default"",
            Reliability = FoxRunDeliveryReliability.SystemDefault,
            Durability = FoxRunDeliveryDurability.SystemDefault,
            History = FoxRunDeliveryHistory.SystemDefault)]
        private int _value;
    }
}";

            Assert.DoesNotContain(
                RunGenerator(source).Diagnostics,
                diagnostic => diagnostic.Severity
                              == DiagnosticSeverity.Error);
        }

        [Fact]
        [Trait("Phase", "186-A")]
        public void InvalidTransportNeutralDeliveryAxisUsesPublicDiagnostic()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class InvalidDelivery
    {
        [FoxRun(""/phase186/invalid-delivery"",
            Reliability = (FoxRunDeliveryReliability)99)]
        private int _value;
    }
}";

            var diagnostic = Assert.Single(
                RunGenerator(source).Diagnostics,
                value => value.Severity
                         == DiagnosticSeverity.Error);
            Assert.Equal("FOXRUN622", diagnostic.Id);
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
        [Trait("Phase", "185-A")]
        public void DescriptorComparerTreatsValueTypeClassificationAsSemanticState()
        {
            FoxRunGenerationModel Build(bool isValueType)
                => FoxRunGenerationModel.FromMembers(new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "ValueTypeProbe",
                        "_value",
                        "field",
                        "System.Int32",
                        isValueType,
                        false,
                        string.Empty,
                        "/phase185/value-type",
                        10f,
                        "Demo.Value",
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        "UnitTest",
                        0,
                        string.Empty)
                });

            var comparison = FoxRunGenerationDescriptorComparer.Compare(
                Build(isValueType: true),
                Build(isValueType: false));

            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains(
                    "isValueType",
                    StringComparison.Ordinal));
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void DescriptorWriterAndComparerPreserveStreamSemantics()
        {
            var streamMember = new FoxRunGenerationMember(
                "Demo", "StreamInput", "_samples", "field", "System.Int32",
                true, false, "", "/phase184/stream", 0f, "",
                1, 0f, "UnitTest", 0, "",
                mode: (int)FoxRunFlow.Subscribe,
                isStream: true);
            var stream = FoxRunGenerationModel.FromMembers(new[] { streamMember });
            var ordinary = FoxRunGenerationModel.FromMembers(new[]
            {
                new FoxRunGenerationMember(
                    "Demo", "StreamInput", "_samples", "field", "System.Int32",
                    true, false, "", "/phase184/stream", 0f, "",
                    1, 0f, "UnitTest", 0, "",
                    mode: (int)FoxRunFlow.Subscribe)
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(stream);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(stream, ordinary);
            using var descriptor = JsonDocument.Parse(json);
            var serializedMember = descriptor.RootElement
                .GetProperty("types")[0]
                .GetProperty("members")[0];

            Assert.Contains("\"isStream\":true", json, StringComparison.Ordinal);
            Assert.True(serializedMember.GetProperty("isStream").GetBoolean());
            Assert.False(comparison.IsSemanticEqual);
            Assert.Contains(
                comparison.SemanticDifferences,
                difference => difference.Contains("isStream", StringComparison.Ordinal));
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
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var subscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.Subscribe)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);
            var publishAndSubscribe = FoxRunManifestBuilder.Build(new[]
            {
                ManifestMember(FoxRunFlow.PublishAndSubscribe)
            }, manifestVersion: FoxrunManifestWriter.CurrentManifestVersion);

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

            Assert.Equal(
                new[] { "json", "msgpack", "protobuf" },
                contracts.Select(contract => contract.Encoding).OrderBy(encoding => encoding));
            Assert.Null(contracts.Single(contract => contract.Encoding == "json").Fields.Single().ProtobufMetadata);
            Assert.Null(contracts.Single(contract => contract.Encoding == "msgpack").Fields.Single().ProtobufMetadata);
            Assert.Equal(
                17,
                contracts.Single(contract => contract.Encoding == "protobuf")
                    .Fields.Single()
                    .ProtobufMetadata.FieldNumber);
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
                    typeShape: FoxRunTypeShape.Canonical("float32"))
            });

            var diagnostics = FoxRunGenerationModelValidator.Validate(model);

            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN200" && diagnostic.Severity == "Error");
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FOXRUN201");
        }

        [Fact]
        public void RoslynGeneratorDoesNotEmitNullableProtobufWriterSyntaxOrConversionErrors()
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
                diagnostic => diagnostic.Id == "CS1001" || diagnostic.Id == "CS1503");
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

            Assert.Contains("TopicMember has empty MemberName", ex.Message, StringComparison.Ordinal);
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

        private static object ReadReflectionMessageAttributeSnapshot(Type type)
        {
            var reader = typeof(FoxrunCodeGenerator).GetMethod(
                "ReadFoxRunMessageAttributeSnapshot",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(reader);
            var snapshot = reader.Invoke(null, new object[] { type });
            Assert.NotNull(snapshot);
            return snapshot;
        }

        private static IReadOnlyDictionary<string, FoxRunConditionMemberKind> ScanReflectionConditionKinds(
            Type type)
        {
            const BindingFlags flags = BindingFlags.Public
                                       | BindingFlags.NonPublic
                                       | BindingFlags.Instance
                                       | BindingFlags.DeclaredOnly;
            return type.GetFields(flags)
                .Where(field => field.GetCustomAttribute<FoxRunAttribute>() != null)
                .ToDictionary(
                    field => field.Name,
                    field =>
                    {
                        var snapshot = ReadReflectionAttributeSnapshot(field);
                        return FoxRunReflectionConditionMemberResolver.Resolve(
                            type,
                            ReadField<string>(snapshot, "OnlyIf"),
                            (FoxRunNamedArgumentPresence)ReadInt64Field(
                                snapshot,
                                "NamedArgumentPresence"));
                    },
                    StringComparer.Ordinal);
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



        [Fact]
        [Trait("Phase", "184-E")]
        public void RoslynCompilesTwoInitializedSubscribeStreamsInOneType()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}
namespace Demo
{
    public partial class Streams
    {
        [FoxRun(""/imu"", Mode = FoxRunFlow.Subscribe)]
        private FoxRunStream<int> _imu = new FoxRunStream<int>();

        [FoxRun(""/lidar"", Mode = FoxRunFlow.Subscribe)]
        private FoxRunStream<float> _lidar = new FoxRunStream<float>();
    }
}";

            var output = RunGeneratorAndUpdateCompilation(source);

            Assert.DoesNotContain(
                output.GetDiagnostics(),
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }



        [Fact]
        [Trait("Phase", "184-H")]
        public void Phase184ManualContextDiagnosticsPreserveReasonAndConstrainPlayAuthorization()
        {
            var acceptanceSource = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");
            var diagnostic = new Phase184ContextDiagnosticProbe(acceptanceSource);

            foreach (var reason in new[]
                     {
                         "No valid Phase184 manual-active pointer is present.",
                         "The Phase184 helper process is no longer alive.",
                     })
            {
                var formatted = diagnostic.Format(reason, isManual: true);
                Assert.Contains(reason, formatted, StringComparison.Ordinal);
                Assert.Contains("Start a fresh Phase184 helper", formatted, StringComparison.Ordinal);
                Assert.Contains("exactly one Play", formatted, StringComparison.Ordinal);
            }

            const string batchReason =
                "The Phase184 run config is missing, empty, or oversized.";
            Assert.Equal(batchReason, diagnostic.Format(batchReason, isManual: false));
            const string blankBatchReason =
                "The Batch Phase184 run config argument is missing or blank.";
            Assert.Equal(
                blankBatchReason,
                diagnostic.Format(blankBatchReason, isManual: false));

            Assert.Contains(
                "Phase184ContextDiagnostic.Format(",
                acceptanceSource,
                StringComparison.Ordinal);
            Assert.Contains("isManual: !isBatchContext", acceptanceSource, StringComparison.Ordinal);
            Assert.Contains("Application.isBatchMode", acceptanceSource, StringComparison.Ordinal);
            Assert.Contains("TryReadCommandLineValue", acceptanceSource, StringComparison.Ordinal);
            Assert.Contains(
                "The Batch Phase184 run config argument is missing or blank.",
                acceptanceSource,
                StringComparison.Ordinal);
            Assert.Contains("isManual: false", acceptanceSource, StringComparison.Ordinal);
            var builderSource = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptanceBuilder.cs");
            foreach (var contract in new[]
                     {
                         "CreateFreshRouteSet",
                         "NormalizeHelperOwnedRoute",
                         "ValidateFreshRouteSetInMemory",
                         "RequireHelperOwnedRoute",
                         "HideFlags.NotEditable",
                     })
            {
                Assert.Contains(contract, builderSource, StringComparison.Ordinal);
            }
            var builderRoot = CSharpSyntaxTree.ParseText(
                builderSource,
                new CSharpParseOptions(preprocessorSymbols: new[] { "UNITY_EDITOR" }))
                .GetRoot();
            var previewValidation = builderRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "ValidateFreshRouteSetInMemory")
                .ToFullString();
            var newObject = builderRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "NewObject")
                .ToFullString();
            Assert.Contains("try", newObject, StringComparison.Ordinal);
            Assert.Contains(
                "SceneManager.MoveGameObjectToScene(value, scene)",
                newObject,
                StringComparison.Ordinal);
            Assert.Contains(
                "UnityEngine.Object.DestroyImmediate(value)",
                newObject,
                StringComparison.Ordinal);
            Assert.True(
                newObject.IndexOf("UnityEngine.Object.DestroyImmediate(value)", StringComparison.Ordinal)
                > newObject.IndexOf("SceneManager.MoveGameObjectToScene(value, scene)", StringComparison.Ordinal));
            Assert.Contains("EditorSceneManager.NewPreviewScene()", previewValidation, StringComparison.Ordinal);
            Assert.Contains("ClosePreviewSceneWithFallback(scene)", previewValidation, StringComparison.Ordinal);
            Assert.Contains("AggregateException", previewValidation, StringComparison.Ordinal);
            Assert.Contains("ExceptionDispatchInfo.Capture", previewValidation, StringComparison.Ordinal);
            Assert.Contains("catch (Exception", previewValidation, StringComparison.Ordinal);
            Assert.DoesNotContain("NewScene(", previewValidation, StringComparison.Ordinal);
            var previewCleanup = builderRoot.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == "ClosePreviewSceneWithFallback")
                .Select(method => method.ToFullString())
                .SingleOrDefault() ?? string.Empty;
            Assert.Contains("scene.GetRootGameObjects()", previewCleanup, StringComparison.Ordinal);
            Assert.Contains(
                "UnityEngine.Object.DestroyImmediate(root)",
                previewCleanup,
                StringComparison.Ordinal);
            Assert.Contains("AggregateException", previewCleanup, StringComparison.Ordinal);
            Assert.True(
                previewCleanup.Split(
                    "EditorSceneManager.ClosePreviewScene(scene)",
                    StringSplitOptions.None).Length >= 3,
                "Preview cleanup must make one initial close attempt and one bounded retry.");
            var existingStart = builderSource.IndexOf("if (sceneExists)", StringComparison.Ordinal);
            var existingEnd = builderSource.IndexOf("            else", existingStart, StringComparison.Ordinal);
            Assert.True(existingStart >= 0 && existingEnd > existingStart);
            var existingBranch = builderSource.Substring(existingStart, existingEnd - existingStart);
            Assert.DoesNotContain("requireInactive: true", existingBranch, StringComparison.Ordinal);
            Assert.True(
                builderSource.IndexOf("NormalizeHelperOwnedRoute(profile", StringComparison.Ordinal)
                > existingEnd);
        }

        [Fact]
        [Trait("Phase", "187")]
        public void Phase184NativePreflightPrecedesManagerMutation()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptanceBuilder.cs");
            var root = CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(preprocessorSymbols: new[] { "UNITY_EDITOR" }))
                .GetRoot();
            var configure = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "ConfigureManager");
            var nativeGuard = configure.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .Single(statement =>
                    statement.Condition.ToString() == "native"
                    && statement.Statement.ToFullString().Contains(
                        "require an active ROS2 For Unity runtime package",
                        StringComparison.Ordinal));
            var firstManagerMutation = configure.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .First(expression => expression.Type.ToString() == "SerializedObject");

            Assert.True(
                nativeGuard.SpanStart < firstManagerMutation.SpanStart,
                "Unavailable native cases must fail before serialized Manager state is changed.");
        }

        [Fact]
        [Trait("Phase", "187")]
        public void Phase184ContextFailureStopsRemainingUpdateWork()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var acceptance = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type =>
                    type.Identifier.ValueText == "Phase184FoxRunProfileAcceptance");
            var update = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Update");
            var statements = update.Body!.Statements;
            var profileIndex = statements.IndexOf(
                statements.Single(statement => statement.ToString().Contains(
                    "CaptureRuntimeProfileEvidence()",
                    StringComparison.Ordinal)));
            var transportIndex = statements.IndexOf(
                statements.Single(statement => statement.ToString().Contains(
                    "CaptureTransportClientEvidence()",
                    StringComparison.Ordinal)));

            Assert.Contains(
                statements.Skip(profileIndex + 1).Take(transportIndex - profileIndex - 1),
                statement => statement is IfStatementSyntax guard
                    && guard.Condition.ToString().Contains(
                        "!_contextValidated",
                        StringComparison.Ordinal)
                    && guard.Statement.DescendantNodesAndSelf()
                        .OfType<ReturnStatementSyntax>()
                        .Any());
        }

        [Fact]
        [Trait("Phase", "187")]
        public void Phase179PlayerCompletionRequiresImuEvidence()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase179FoxRunRos2NativeSubscribeAcceptance.cs");
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            var evaluate = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "EvaluatePlayerAutoQuit");
            var success = evaluate.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .Single(statement => statement.Statement.ToFullString().Contains(
                    "CompletePlayer(0, \"success\")",
                    StringComparison.Ordinal));

            Assert.Contains(
                "_playerImuMatched",
                success.Condition.ToString(),
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184RuntimeAcceptanceRoutesUseBoundedNonRacingEvidenceWindows()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");
            var syntaxRoot = CSharpSyntaxTree.ParseText(source).GetRoot();

            string RouteSource(string routeName)
            {
                return syntaxRoot.DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Single(type => type.Identifier.ValueText == routeName)
                    .ToFullString();
            }

            var foxgloveRoute = RouteSource("Phase184FoxgloveProfileRoute");
            var foxgloveUpdate = CSharpSyntaxTree.ParseText(foxgloveRoute)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Update")
                .ToFullString();
            Assert.Contains(
                "ProfileResponseTimeoutSeconds",
                foxgloveRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                "_profileResponseDeadline",
                foxgloveUpdate,
                StringComparison.Ordinal);
            Assert.Contains(
                "Foxglove profile response was not observed.",
                foxgloveUpdate,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "_nextBootstrapPulseAt",
                foxgloveUpdate,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "MaximumBootstrapPulses",
                foxgloveRoute,
                StringComparison.Ordinal);

            var multiTargetRoute = RouteSource("Phase184MultiTargetRoute");
            Assert.Contains(
                "WarmupTimeoutSeconds",
                multiTargetRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                "_warmupDeadline",
                multiTargetRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                "Multi-target readiness was not observed.",
                multiTargetRoute,
                StringComparison.Ordinal);

            var streamRoute = RouteSource("Phase184StreamRoute");
            Assert.Contains(
                "private const float StreamTransportSettleSeconds = 0.5f;",
                streamRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                "_received > _inputStream.Options.Capacity",
                streamRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                "Mathf.Max(_producerCompletionObservedAt, _lastStreamActivityAt)",
                streamRoute,
                StringComparison.Ordinal);
            Assert.Contains(
                ">= StreamTransportSettleSeconds",
                streamRoute,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "MinimumStreamSamples",
                streamRoute,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184RuntimeAcceptanceEmitsObservedProfileAndTargetEvidence()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");

            Assert.Contains(
                "field.GetCustomAttributes(",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "typeof(FoxRunAttribute)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_manager.ConfiguredFoxRunPublishTransportIds",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_manager.ConfiguredFoxRunSubscribeTransportId",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "manager?.ActiveFoxRunTransportSession",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_manager.ActiveFoxRunPublishEncoding",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "_manager.ActiveFoxRunSubscriptionEncoding",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "PHASE184G_PROFILE_EVIDENCE",
                source,
                StringComparison.Ordinal);
            foreach (var marker in new[]
                     {
                         "PHASE184G_FOXGLOVE_TARGET_STATUS",
                         "PHASE184G_MULTI_TARGET_STATUS",
                         "PHASE184G_QOS_TARGET_STATUS",
                         "PHASE184G_STREAM_SUBSCRIPTION_STATUS",
                     })
            {
                Assert.Contains(marker, source, StringComparison.Ordinal);
            }
            Assert.Contains(
                "Phase184AcceptanceText.FormatTransportIds(",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "bridgeRuntimeFailures=",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "copyFailed=",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "staleCallbacks=",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "rejectedAfterStop=",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-H")]
        public void Phase184AcceptanceThrottlesStableTransportClientEvidence()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");

            Assert.Contains(
                "_manager.GetTransportStatsSnapshot()",
                source,
                StringComparison.Ordinal);
            Assert.Contains("stats.ActiveClientCount", source, StringComparison.Ordinal);
            Assert.Contains("stats.TotalAcceptedClients", source, StringComparison.Ordinal);
            Assert.Equal(
                1,
                source.Split(
                    new[] { "\"PHASE184H_TRANSPORT_CLIENTS\"" },
                    StringSplitOptions.None).Length - 1);
            Assert.Equal(
                1,
                source.Split(
                    new[] { "\"PHASE184H_TRANSPORT_CLIENTS_OVERFLOW\"" },
                    StringSplitOptions.None).Length - 1);

            var syntaxRoot = CSharpSyntaxTree.ParseText(source).GetRoot();
            var acceptance = syntaxRoot.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type =>
                    type.Identifier.ValueText == "Phase184FoxRunProfileAcceptance");
            var routeBase = syntaxRoot.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(type =>
                    type.Identifier.ValueText == "Phase184AcceptanceRoute");

            var transportMaximum = acceptance.Members
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(
                    variable =>
                        variable.Identifier.ValueText
                        == "MaximumTransportClientMarkerCount"));
            Assert.Contains(
                transportMaximum.Modifiers,
                modifier => modifier.IsKind(SyntaxKind.ConstKeyword));
            Assert.Equal(
                "8",
                transportMaximum.Declaration.Variables.Single().Initializer?.Value.ToString());

            var sampleInterval = acceptance.Members
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(
                    variable =>
                        variable.Identifier.ValueText
                        == "TransportClientSampleIntervalSeconds"));
            Assert.Contains(
                sampleInterval.Modifiers,
                modifier => modifier.IsKind(SyntaxKind.ConstKeyword));
            var sampleIntervalLiteral = Assert.IsType<LiteralExpressionSyntax>(
                sampleInterval.Declaration.Variables.Single().Initializer?.Value);
            Assert.InRange(
                Assert.IsType<float>(sampleIntervalLiteral.Token.Value),
                0.05f,
                0.1f);

            var routeMaximum = routeBase.Members
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(
                    variable => variable.Identifier.ValueText == "MaximumMarkerCount"));
            Assert.Equal(
                "64",
                routeMaximum.Declaration.Variables.Single().Initializer?.Value.ToString());

            var runTokenField = acceptance.Members
                .OfType<FieldDeclarationSyntax>()
                .Single(field => field.Declaration.Variables.Any(
                    variable => variable.Identifier.ValueText == "_runToken"));
            Assert.Contains(
                runTokenField.Modifiers,
                modifier => modifier.IsKind(SyntaxKind.PrivateKeyword));
            Assert.Contains(
                runTokenField.AttributeLists.SelectMany(list => list.Attributes),
                attribute => attribute.Name.ToString() == "NonSerialized");
            Assert.DoesNotContain(
                runTokenField.AttributeLists.SelectMany(list => list.Attributes),
                attribute => attribute.Name.ToString() == "SerializeField");

            var update = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Update")
                .ToFullString();
            var validationGuardIndex = update.IndexOf(
                "if (!_contextValidated || _manager == null)",
                StringComparison.Ordinal);
            var sampleIndex = update.IndexOf(
                "CaptureTransportClientEvidence();",
                StringComparison.Ordinal);
            Assert.True(
                validationGuardIndex >= 0 && sampleIndex > validationGuardIndex,
                "Transport evidence must be sampled from Update only after context validation.");

            var awake = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "Awake")
                .ToFullString();
            var contextValidatedIndex = awake.IndexOf(
                "_contextValidated = true;",
                StringComparison.Ordinal);
            var resetIndex = awake.IndexOf(
                "ResetTransportClientEvidence(context.Token);",
                StringComparison.Ordinal);
            var armIndex = awake.IndexOf(
                "route.Arm(context);",
                StringComparison.Ordinal);
            var activateIndex = awake.IndexOf(
                "route.gameObject.SetActive(true);",
                StringComparison.Ordinal);
            Assert.True(
                contextValidatedIndex >= 0
                && resetIndex > contextValidatedIndex
                && armIndex > resetIndex
                && activateIndex > armIndex,
                "Validated transport evidence must reset before unchanged route activation.");

            var reset = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method =>
                    method.Identifier.ValueText == "ResetTransportClientEvidence")
                .ToFullString();
            Assert.Contains("_runToken = runToken;", reset, StringComparison.Ordinal);
            Assert.Contains(
                "_transportClientMarkerState.Reset();",
                reset,
                StringComparison.Ordinal);
            Assert.Contains(
                "_nextTransportClientSampleAt = 0f;",
                reset,
                StringComparison.Ordinal);

            var capture = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method =>
                    method.Identifier.ValueText == "CaptureTransportClientEvidence")
                .ToFullString();
            Assert.Contains(
                "if (_transportClientMarkerState.IsOverflowed)",
                capture,
                StringComparison.Ordinal);
            Assert.Contains("var now = Time.unscaledTime;", capture, StringComparison.Ordinal);
            Assert.Contains(
                "if (now < _nextTransportClientSampleAt)",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "_nextTransportClientSampleAt =",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "now + TransportClientSampleIntervalSeconds;",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "if (stats == null || !stats.Supported)",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "_transportClientMarkerState.ResetPending();",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "_transportClientMarkerState.Observe(active, accepted)",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "decision.ActiveClientCount",
                capture,
                StringComparison.Ordinal);
            Assert.Contains(
                "decision.TotalAcceptedClients",
                capture,
                StringComparison.Ordinal);
            Assert.True(
                capture.IndexOf(
                    "if (now < _nextTransportClientSampleAt)",
                    StringComparison.Ordinal)
                < capture.IndexOf(
                    "_manager.GetTransportStatsSnapshot()",
                    StringComparison.Ordinal),
                "Sampling must be throttled before allocating a transport snapshot.");
            Assert.DoesNotContain("Pass(", capture, StringComparison.Ordinal);
            Assert.DoesNotContain("Fail(", capture, StringComparison.Ordinal);

            var emit = acceptance.Members
                .OfType<MethodDeclarationSyntax>()
                .Single(method =>
                    method.Identifier.ValueText == "EmitTransportClientEvidence")
                .ToFullString();
            Assert.Contains("\"case=\" + _selectedCase", emit, StringComparison.Ordinal);
            Assert.Contains(
                "\" token=\" + Phase184AcceptanceText.SafeMarker(_runToken)",
                emit,
                StringComparison.Ordinal);
            Assert.Contains("\" active=\" + active", emit, StringComparison.Ordinal);
            Assert.Contains("\" accepted=\" + accepted", emit, StringComparison.Ordinal);
            Assert.Contains(
                "PHASE184G_CONTEXT_READY",
                acceptance.Members
                    .OfType<MethodDeclarationSyntax>()
                    .Single(method => method.Identifier.ValueText == "Awake")
                    .ToFullString(),
                StringComparison.Ordinal);

            var decision = syntaxRoot.DescendantNodes()
                .OfType<StructDeclarationSyntax>()
                .Single(type =>
                    type.Identifier.ValueText
                    == "Phase184TransportClientMarkerDecision");
            Assert.Contains(
                decision.Modifiers,
                modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword));
            Assert.Contains(
                acceptance.Members.OfType<FieldDeclarationSyntax>(),
                field => field.Declaration.Variables.Any(
                             variable =>
                                 variable.Identifier.ValueText
                                 == "_transportClientMarkerState")
                         && field.Modifiers.Any(
                             modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword)));
            Assert.DoesNotContain(
                "_transportClientActiveCounts",
                acceptance.ToFullString(),
                StringComparison.Ordinal);

            foreach (var serializedRouteAnchor in new[]
                     {
                         "[SerializeField] private Phase184FoxgloveProfileRoute _foxgloveProfile;",
                         "[SerializeField] private Phase184MultiTargetRoute _multiTarget;",
                         "[SerializeField] private Phase184DegradedTargetRoute _degradedTarget;",
                         "[SerializeField] private Phase184QosContractRoute _qosContract;",
                         "[SerializeField] private Phase184StreamRoute _stream;",
                     })
            {
                Assert.Contains(serializedRouteAnchor, source, StringComparison.Ordinal);
            }
        }

        [Fact]
        [Trait("Phase", "184-H")]
        public void Phase184TransportClientMarkerStateRejectsTornAndUnstablePairs()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase184FoxRunProfileAcceptance.cs");
            var probe = new Phase184TransportClientMarkerStateProbe(source, 8);

            Assert.True(probe.KindType.IsEnum);
            Assert.True(probe.DecisionType.IsValueType);
            Assert.All(
                probe.DecisionType.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                property => Assert.False(property.CanWrite));

            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));

            probe.Reset();
            Assert.Equal("None", probe.Observe(-1, 0).Kind);
            Assert.Equal("None", probe.Observe(0, -1).Kind);
            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));

            probe.Reset();
            Assert.Equal("None", probe.Observe(1, 0).Kind);
            Assert.Equal("None", probe.Observe(0, 1).Kind);
            Assert.Equal("None", probe.Observe(1, 1).Kind);
            Assert.Equal(("Normal", 1, 1L), probe.Observe(1, 1));

            probe.Reset();
            Assert.Equal("None", probe.Observe(2, 1).Kind);
            Assert.Equal("None", probe.Observe(2, 2).Kind);
            Assert.Equal(("Normal", 2, 2L), probe.Observe(2, 2));

            probe.Reset();
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal("None", probe.Observe(4, 4).Kind);
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal(("Normal", 3, 3L), probe.Observe(3, 3));
            Assert.Equal("None", probe.Observe(3, 3).Kind);
            Assert.Equal("None", probe.Observe(3, 3).Kind);

            probe.Reset();
            Assert.Equal("None", probe.Observe(5, 5).Kind);
            probe.ResetPending();
            Assert.Equal("None", probe.Observe(5, 5).Kind);
            Assert.Equal(("Normal", 5, 5L), probe.Observe(5, 5));

            probe.Reset();
            for (var pair = 0; pair < 8; pair++)
            {
                Assert.Equal("None", probe.Observe(pair, pair).Kind);
                Assert.Equal(
                    ("Normal", pair, (long)pair),
                    probe.Observe(pair, pair));
            }

            Assert.Equal("None", probe.Observe(8, 8).Kind);
            Assert.Equal(("Overflow", 8, 8L), probe.Observe(8, 8));
            Assert.True(probe.IsOverflowed);
            Assert.Equal("None", probe.Observe(9, 9).Kind);
            Assert.Equal("None", probe.Observe(9, 9).Kind);

            probe.Reset();
            Assert.False(probe.IsOverflowed);
            Assert.Equal("None", probe.Observe(0, 0).Kind);
            Assert.Equal(("Normal", 0, 0L), probe.Observe(0, 0));
        }



        [Fact]
        [Trait("Phase", "181-F")]
        public void Phase181OriginProbeBindsNullablePayloadToCurrentRunToken()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/"
                + "Phase181FoxRunCustomRos2InterfaceAcceptance.cs");

            Assert.Contains(
                "CreateState(\n"
                + "                \"unity-bidirectional\",\n"
                + "                RunTokenProbeCount(_runToken),\n"
                + "                true)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "state.Count == RunTokenProbeCount(_runToken)",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "private static int RunTokenProbeCount(string token)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchExitQuiescesFoxRunSourcesBeforePlayModeShutdown()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var scheduleExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void SchedulePlayModeExit()");

            Assert.Contains(
                "PHASE184G_BATCH_SOURCES_QUIESCED",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "route.isActiveAndEnabled",
                source,
                StringComparison.Ordinal);
            var exitScheduleIndex = scheduleExit.IndexOf(
                "EditorApplication.delayCall += ExitPlayModeNow;",
                StringComparison.Ordinal);
            var quiesceIndex = scheduleExit.IndexOf(
                "QuiesceAcceptanceSources();",
                StringComparison.Ordinal);
            Assert.True(exitScheduleIndex >= 0);
            Assert.True(quiesceIndex >= 0);
            Assert.True(exitScheduleIndex < quiesceIndex);
            Assert.Contains(
                "catch (Exception exception)",
                scheduleExit,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void Phase184BatchProbeRestoresDeadlinesAndKeepsExitIdempotentAcrossReload()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                + "Phase184BatchModeProfileProbe.cs");
            var attach =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void AttachHandlers()");
            var open =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void OpenSceneAndEnterPlayMode()");
            var retry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RetryCanceledPlayEntry()");
            var requestEditorExit =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void RequestEditorExit(int exitCode, string outcome)");
            var queueRetry =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static void QueuePlayEntryRetry(string reason)");
            var workerResults =
                Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    source,
                    "private static bool AllRequiredWorkerResultsReady()");

            Assert.Contains("RestoreRunState();", attach, StringComparison.Ordinal);
            Assert.Contains(
                "PersistTime(\"started-at\", value);",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.SetBool(SessionKey(\"terminal-pass-observed\")",
                source,
                StringComparison.Ordinal);
            Assert.Contains(
                "SessionState.GetBool(SessionKey(\"exit-requested\"), false)",
                open,
                StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", open, StringComparison.Ordinal);
            Assert.Contains("StartupDeadlineExpired()", retry, StringComparison.Ordinal);
            Assert.Contains("_editorExitQueued", requestEditorExit, StringComparison.Ordinal);
            Assert.Contains(
                "SessionKey(\"exit-code\")",
                requestEditorExit,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "SessionState.SetBool(SessionKey(\"play-entry-retry-queued\"), true);",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "SchedulePlayEntryAttempt();",
                queueRetry,
                StringComparison.Ordinal);
            Assert.Contains(
                "_requiredWorkerResultPaths.Length == 0",
                workerResults,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void BatchNativeAcceptanceRetriesPlayCanceledBeforeEditModeTransition()
        {
            foreach (var path in new[]
                     {
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase181BatchModeCustomRos2InteropProbe.cs",
                         "Unity2Foxglove/Assets/Editor/ManualAcceptance/"
                         + "Phase184BatchModeProfileProbe.cs",
                     })
            {
                var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(path);
                Assert.Contains(
                    "SessionState.SetBool(SessionKey(\"play-entry-pending\"), true);",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "RetryCanceledPlayEntry",
                    source,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "SessionState.GetBool(SessionKey(\"play-entry-pending\"), false)",
                    source,
                    StringComparison.Ordinal);
            }
        }



        [Theory]
        [Trait("Phase", "184-E")]
        [InlineData(1, "field", false, 0L)]
        [InlineData(3, "field", false, 0L)]
        [InlineData(2, "property", false, 0L)]
        [InlineData(2, "field", true, 0L)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.PublishTransportIds)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Policy)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Hz)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.Tolerance)]
        [InlineData(2, "field", false, (long)FoxRunNamedArgumentPresence.OnlyIf)]
        public void ReflectionLowererRejectsIllegalStreamDeclarationShapes(
            int mode,
            string memberKind,
            bool isAggregateMember,
            long namedArgumentPresence)
        {
            var model = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Streams",
                    "_stream",
                    memberKind,
                    "System.Int32",
                    "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/stream",
                    schemaName: "",
                    hz: -1f,
                    policy: 0,
                    tolerance: 0f,
                    rawMemberOrder: 0,
                    conditionalSymbols: "",
                    isAggregateMember: isAggregateMember,
                    mode: mode,
                    namedArgumentPresence:
                        (FoxRunNamedArgumentPresence)namedArgumentPresence,
                    isStream: true)
            });

            Assert.Contains(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN215");
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void ReflectionLowererAcceptsValidSubscribeStreamFieldShape()
        {
            var model = FoxRunReflectionGenerationModelLowerer.Lower(new[]
            {
                new FoxRunReflectionGenerationMember(
                    "Demo",
                    "Streams",
                    "_stream",
                    "field",
                    "System.Int32",
                    "int",
                    isValueType: true,
                    isArray: false,
                    elementTypeName: "",
                    topic: "/stream",
                    schemaName: "",
                    hz: -1f,
                    policy: 0,
                    tolerance: 0f,
                    rawMemberOrder: 0,
                    conditionalSymbols: "",
                    mode: (int)FoxRunFlow.Subscribe,
                    isStream: true)
            });

            Assert.DoesNotContain(
                FoxRunGenerationModelValidator.Validate(model),
                diagnostic => diagnostic.Id == "FOXRUN215");
        }

        [Fact]
        [Trait("Phase", "184-F")]
        public void ControlledTestLogPhysicalFallbackMatchesRoslynEmitter()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.cs");
            var messagePackSource = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/FullDemoVisualization/TestLog.MessagePack.cs");
            var result = RunGenerator(source, messagePackSource);
            var core = result.Results
                .Single()
                .GeneratedSources
                .Single(generated => generated.HintName == "TestLog_FoxRun.g.cs")
                .SourceText
                .ToString();
            var expected = string.Join(
                    "\n",
                    "// <auto-generated/>",
                    "// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.",
                    "// SPDX-License-Identifier: Apache-2.0",
                    "// " + FoxRunGeneratedSourceReconciler.GeneratedSourceSentinel,
                    "// In the Unity Editor, the Roslyn analyzer already generates this partial type in memory.",
                    "#if !UNITY_EDITOR")
                + "\n"
                + NormalizeGeneratedSource(core).TrimEnd()
                + "\n#endif";
            var actual = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");

            Assert.Equal(expected, NormalizeGeneratedSource(actual).TrimEnd());
        }

        [Theory]
        [Trait("Phase", "184-E")]
        [InlineData("private FoxRunStream<int> _stream;")]
        [InlineData("private FoxRunStream<int> _stream = null;")]
        [InlineData("private FoxRunStream<int> _stream = default;")]
        public void RoslynRejectsStreamWithoutNonNullFieldInitializer(string declaration)
        {
            var result = RunGenerator($@"
using Unity.FoxgloveSDK.Components;
namespace Demo
{{
    public partial class Streams
    {{
        [FoxRun(""/stream"", Mode = FoxRunFlow.Subscribe)]
        {declaration}
    }}
}}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN216");
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void RoslynRejectsMultipleFoxRunAttributesOnOneStream()
        {
            var result = RunGenerator(@"
using Unity.FoxgloveSDK.Components;
namespace Demo
{
    public partial class Streams
    {
        [FoxRun(""/one"", Mode = FoxRunFlow.Subscribe)]
        [FoxRun(""/two"", Mode = FoxRunFlow.Subscribe)]
        private FoxRunStream<int> _stream = new FoxRunStream<int>();
    }
}");

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "FOXRUN215");
        }

        private sealed class Phase184TransportClientMarkerStateProbe
        {
            private const BindingFlags InstanceMembers =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            private readonly object _state;
            private readonly MethodInfo _observe;
            private readonly MethodInfo _resetPending;
            private readonly MethodInfo _reset;
            private readonly PropertyInfo _isOverflowed;
            private readonly PropertyInfo _kind;
            private readonly PropertyInfo _active;
            private readonly PropertyInfo _accepted;

            internal Phase184TransportClientMarkerStateProbe(
                string acceptanceSource,
                int maximumMarkerCount)
            {
                var syntaxRoot = CSharpSyntaxTree.ParseText(acceptanceSource).GetRoot();
                var declarations = new[]
                    {
                        "Phase184TransportClientMarkerKind",
                        "Phase184TransportClientMarkerDecision",
                        "Phase184TransportClientMarkerState",
                    }
                    .Select(name => syntaxRoot.DescendantNodes()
                        .OfType<BaseTypeDeclarationSyntax>()
                        .Single(type => type.Identifier.ValueText == name)
                        .NormalizeWhitespace()
                        .ToFullString());
                var isolatedSource =
                    "using System;\n"
                    + "namespace Unity2Foxglove.ManualAcceptance\n{\n"
                    + string.Join(Environment.NewLine, declarations)
                    + "\n}";
                var trustedAssemblies =
                    AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                    ?? string.Empty;
                var references = trustedAssemblies
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => MetadataReference.CreateFromFile(path));
                var compilation = CSharpCompilation.Create(
                    "Phase184TransportMarkerProbe_" + Guid.NewGuid().ToString("N"),
                    new[] { CSharpSyntaxTree.ParseText(isolatedSource) },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var image = new MemoryStream();
                var emit = compilation.Emit(image);
                Assert.True(
                    emit.Success,
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
                var assembly = Assembly.Load(image.ToArray());
                const string typePrefix =
                    "Unity2Foxglove.ManualAcceptance.";
                KindType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerKind",
                    throwOnError: true);
                DecisionType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerDecision",
                    throwOnError: true);
                var stateType = assembly.GetType(
                    typePrefix + "Phase184TransportClientMarkerState",
                    throwOnError: true);
                var constructor = stateType.GetConstructor(
                    InstanceMembers,
                    binder: null,
                    new[] { typeof(int) },
                    modifiers: null);
                Assert.NotNull(constructor);
                _state = constructor.Invoke(new object[] { maximumMarkerCount });
                _observe = RequiredMethod(stateType, "Observe");
                _resetPending = RequiredMethod(stateType, "ResetPending");
                _reset = RequiredMethod(stateType, "Reset");
                _isOverflowed = RequiredProperty(stateType, "IsOverflowed");
                _kind = RequiredProperty(DecisionType, "Kind");
                _active = RequiredProperty(DecisionType, "ActiveClientCount");
                _accepted = RequiredProperty(DecisionType, "TotalAcceptedClients");
            }

            internal Type KindType { get; }
            internal Type DecisionType { get; }

            internal bool IsOverflowed =>
                Assert.IsType<bool>(_isOverflowed.GetValue(_state));

            internal (string Kind, int Active, long Accepted) Observe(
                int active,
                long accepted)
            {
                var decision = _observe.Invoke(_state, new object[] { active, accepted });
                Assert.NotNull(decision);
                var kind = _kind.GetValue(decision);
                Assert.NotNull(kind);
                return (
                    kind.ToString(),
                    Assert.IsType<int>(_active.GetValue(decision)),
                    Assert.IsType<long>(_accepted.GetValue(decision)));
            }

            internal void ResetPending()
                => _resetPending.Invoke(_state, Array.Empty<object>());

            internal void Reset()
                => _reset.Invoke(_state, Array.Empty<object>());

            private static MethodInfo RequiredMethod(Type type, string name)
            {
                var method = type.GetMethod(name, InstanceMembers);
                Assert.NotNull(method);
                return method;
            }

            private static PropertyInfo RequiredProperty(Type type, string name)
            {
                var property = type.GetProperty(name, InstanceMembers);
                Assert.NotNull(property);
                return property;
            }
        }

        private static GeneratorDriverRunResult RunGenerator(params string[] sources)
        {
            var compilation = CreateCompilation(sources);

            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static GeneratorDriverRunResult RunGeneratorWithR2fu(
            params string[] sources)
        {
            var compilation = CreateCompilation(sources);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.CoreAndR2fu());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static string GeneratedDescriptor(GeneratorDriverRunResult result)
            => result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

        private static string GeneratedDescriptorJson(GeneratorDriverRunResult result)
        {
            var descriptorSource = CSharpSyntaxTree.ParseText(GeneratedDescriptor(result));
            var descriptorVariable = descriptorSource
                .GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "DescriptorJson");
            var literal = Assert.IsType<LiteralExpressionSyntax>(
                descriptorVariable.Initializer?.Value);
            return literal.Token.ValueText;
        }

        private static Unity.FoxgloveSDK.SourceGenerators.MemberData ExtractRoslynMemberData(
            string source,
            string memberName = null)
        {
            var compilation = CreateCompilation(source);
            var fields = compilation.SyntaxTrees
                .SelectMany(tree => tree.GetRoot().DescendantNodes())
                .OfType<FieldDeclarationSyntax>()
                .ToArray();
            var field = string.IsNullOrEmpty(memberName)
                ? fields.Single()
                : fields.Single(candidate =>
                    candidate.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == memberName));
            var constructor = typeof(GeneratorSyntaxContext).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(SyntaxNode), typeof(SemanticModel) },
                modifiers: null);
            Assert.NotNull(constructor);
            var context = (GeneratorSyntaxContext)constructor.Invoke(
                new object[] { field, compilation.GetSemanticModel(field.SyntaxTree) });
            var extract = typeof(FoxgloveLogSourceGenerator).GetMethod(
                "ExtractMember",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(extract);
            return Assert.IsType<Unity.FoxgloveSDK.SourceGenerators.MemberData>(
                extract.Invoke(
                    null,
                    new object[] { context, System.Threading.CancellationToken.None }));
        }

        private static Compilation RunGeneratorAndUpdateCompilation(string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            return outputCompilation;
        }

        private static Compilation RunGeneratorAndUpdateCompilationWithR2fu(
            string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.CoreAndR2fu());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out _);
            return outputCompilation;
        }

        private static CSharpCompilation CreateCompilation(params string[] sources)
        {
            return CSharpCompilation.Create(
                "Phase157GeneratorProbe",
                sources.Select(source => CSharpSyntaxTree.ParseText(source)),
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string NormalizeGeneratedSource(string source)
            => (source ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

        private sealed class ReflectionArgumentsFixture
        {
            private bool Enabled => true;

            [FoxRun("/phase184/reflection/omitted")]
            public float Omitted;

            [FoxRun(
                "/phase184/reflection/invalid-policy",
                Policy = (FoxRunPolicy)99)]
            public float InvalidPolicy;

            [FoxRun(
                "/phase184/reflection/whitespace-condition",
                OnlyIf = " Enabled ")]
            public float WhitespaceCondition;

            [FoxRun(
                "/phase186/reflection/providers",
                Mode = FoxRunFlow.PublishAndSubscribe,
                PublishTransportIds = new[]
                {
                    "unity2foxglove.zeta",
                    "foxglove.websocket"
                },
                SubscribeTransportId = "unity2foxglove.alpha")]
            public float ProviderSelection;
        }

        [FoxRunMessage(
            "/phase186/reflection/aggregate",
            PublishTransportIds = new[]
            {
                "unity2foxglove.zeta",
                "foxglove.websocket"
            })]
        private sealed class ReflectionAggregateFixture
        {
            [FoxRunField("value")]
            public int Value;
        }

        private class ReflectionInheritedConditionGrandBase
        {
            protected bool ShadowedCondition => true;
        }

        private class ReflectionInheritedConditionBase : ReflectionInheritedConditionGrandBase
        {
            public bool PublicField;
            protected bool ProtectedProperty => true;
            protected internal bool ProtectedInternalMethod() => true;
            internal bool InternalField;
            private protected bool PrivateProtectedProperty => true;
            private bool PrivateBaseCondition => true;
            public new int ShadowedCondition;
        }

        private sealed class ReflectionInheritedConditionFixture : ReflectionInheritedConditionBase
        {
            private bool CurrentPrivateCondition() => true;

            [FoxRun("/phase184/reflection/public-field", OnlyIf = "PublicField")]
            public float PublicFieldProbe;

            [FoxRun("/phase184/reflection/protected-property", OnlyIf = "ProtectedProperty")]
            public float ProtectedPropertyProbe;

            [FoxRun("/phase184/reflection/protected-internal-method", OnlyIf = "ProtectedInternalMethod")]
            public float ProtectedInternalMethodProbe;

            [FoxRun("/phase184/reflection/internal-field", OnlyIf = "InternalField")]
            public float InternalFieldProbe;

            [FoxRun("/phase184/reflection/private-protected-property", OnlyIf = "PrivateProtectedProperty")]
            public float PrivateProtectedPropertyProbe;

            [FoxRun("/phase184/reflection/current-private-method", OnlyIf = nameof(CurrentPrivateCondition))]
            public float CurrentPrivateMethodProbe;

            [FoxRun("/phase184/reflection/private-base", OnlyIf = "PrivateBaseCondition")]
            public float PrivateBase;

            [FoxRun("/phase184/reflection/invalid-shadow", OnlyIf = "ShadowedCondition")]
            public float InvalidShadowProbe;
        }

        private sealed class Phase184ContextDiagnosticProbe
        {
            private readonly MethodInfo _format;

            internal Phase184ContextDiagnosticProbe(string acceptanceSource)
            {
                var declaration = CSharpSyntaxTree.ParseText(acceptanceSource)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Single(type => type.Identifier.ValueText == "Phase184ContextDiagnostic")
                    .NormalizeWhitespace()
                    .ToFullString();
                var isolatedSource =
                    "using System;\n"
                    + "namespace Unity2Foxglove.ManualAcceptance\n{\n"
                    + declaration
                    + "\n}";
                var trustedAssemblies =
                    AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                    ?? string.Empty;
                var references = trustedAssemblies
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => MetadataReference.CreateFromFile(path));
                var compilation = CSharpCompilation.Create(
                    "Phase184ContextDiagnosticProbe_" + Guid.NewGuid().ToString("N"),
                    new[] { CSharpSyntaxTree.ParseText(isolatedSource) },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var image = new MemoryStream();
                var emit = compilation.Emit(image);
                Assert.True(
                    emit.Success,
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
                var type = Assembly.Load(image.ToArray()).GetType(
                    "Unity2Foxglove.ManualAcceptance.Phase184ContextDiagnostic",
                    throwOnError: true);
                _format = type.GetMethod(
                    "Format",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.NotNull(_format);
            }

            internal string Format(string reason, bool isManual)
                => Assert.IsType<string>(_format.Invoke(null, new object[] { reason, isManual }));
        }
    }
}
