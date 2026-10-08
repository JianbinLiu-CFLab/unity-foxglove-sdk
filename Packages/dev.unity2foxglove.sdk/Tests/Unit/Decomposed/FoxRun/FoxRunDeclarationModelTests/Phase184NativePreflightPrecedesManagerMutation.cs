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

    }
}
