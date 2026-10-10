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

    }
}
