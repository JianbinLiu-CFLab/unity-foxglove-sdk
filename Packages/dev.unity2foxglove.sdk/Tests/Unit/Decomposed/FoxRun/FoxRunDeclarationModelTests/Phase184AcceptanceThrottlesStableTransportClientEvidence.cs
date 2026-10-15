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

    }
}
