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

    }
}
