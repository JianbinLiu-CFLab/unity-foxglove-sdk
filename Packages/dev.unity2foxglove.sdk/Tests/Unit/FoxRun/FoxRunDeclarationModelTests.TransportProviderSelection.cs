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
        public void SameTopicMembersMustShareProviderAndDeliveryAuthority()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;

namespace Demo
{
    public partial class ConflictingTopicAuthority
    {
        [FoxRun(""/phase192/authority"",
            PublishTransportIds = new[] { ""unity2foxglove.alpha"" },
            Reliability = FoxRunDeliveryReliability.Reliable)]
        private int _first;

        [FoxRun(""/phase192/authority"",
            PublishTransportIds = new[] { ""unity2foxglove.beta"" },
            Reliability = FoxRunDeliveryReliability.BestEffort)]
        private int _second;
    }
}";

            var diagnostics = RunGenerator(source).Diagnostics;
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN621");
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Id == "FOXRUN622");
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

    }
}
