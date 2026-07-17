// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the public Data Transport Inspector hierarchy.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Guards the public FoxgloveManager Inspector hierarchy planned for Phase 180.
    /// </summary>
    public static class DataTransportInspectorValidation
    {
        private const string ManagerEditorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";
        private static readonly string[] LegacyTransportFoldoutKeys =
        {
            "PublishData",
            "SubscribeData",
            "R2fuRuntime",
            "Ros2Bridge",
        };
        private static readonly string[] DataTransportFoldoutKeys =
        {
            "DataTransport",
            "DataTransportPublish",
            "DataTransportSubscribe",
            "DataTransportNativeRuntime",
            "DataTransportRos2Bridge",
        };

        private static readonly Dictionary<string, string> DataTransportFoldoutFields =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "DataTransport", "_dataTransportExpanded" },
                { "DataTransportPublish", "_dataTransportPublishExpanded" },
                { "DataTransportSubscribe", "_dataTransportSubscribeExpanded" },
                { "DataTransportNativeRuntime", "_dataTransportNativeRuntimeExpanded" },
                { "DataTransportRos2Bridge", "_dataTransportRos2BridgeExpanded" },
            };

        private static int _passed;

        /// <summary>
        /// Validates the public Data Transport grouping without changing Inspector behavior.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 180: Data Transport Inspector Structure ===");
            _passed = 0;

            var mainInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManagerEditorPath);
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var topLevel = FindMethod(mainInspector, "OnInspectorGUI");
            var section = FindMethod(mainInspector, "DrawSection");
            var foldoutState = FindMethod(mainInspector, "LoadInspectorFoldoutState");
            var dataTransport = FindMethod(editorSources, "DrawDataTransportSection");
            var publishData = FindMethod(editorSources, "DrawPublishDataSection");
            var subscribeData = FindMethod(editorSources, "DrawSubscribeDataSection");
            var nativeQos = FindMethod(editorSources, "DrawRos2NativeSubscriptionQos");
            var nativeBudget = FindMethod(editorSources, "DrawRos2NativeCopyBudget");
            var nativeBudgetUnit = FindMethod(editorSources, "GetNativeCopyBudgetDisplayUnit");
            var subsection = FindMethod(editorSources, "DrawDataTransportSubsection");

            VerifyTopLevelWorkflow(topLevel);
            VerifyNestedTransportWorkflow(dataTransport, publishData);
            VerifyPublishPresentation(publishData);
            VerifySubscribePresentation(subscribeData, nativeQos, nativeBudget, nativeBudgetUnit);
            VerifyFoldoutStateModel(mainInspector, foldoutState);
            VerifyParentSectionPresentationHelper(section);
            VerifySubsectionPresentationHelper(subsection);

            Console.WriteLine("Phase 180: " + _passed + " checks passed.");
        }

        private static void VerifyTopLevelWorkflow(MethodDeclarationSyntax topLevel)
        {
            var directSections = DirectInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection"))
                .ToArray();
            var executableSections = ExecutableInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection"))
                .ToArray();
            var allInvocations = AllInvocations(topLevel);
            var directWorkflowCalls = DirectInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection")
                                     || IsInvocationNamed(invocation, "DrawRecordingReplayWarning"))
                .ToArray();
            var expectedWorkflowOrder = new[]
            {
                "Connection & Security",
                "Data Transport",
                "DrawRecordingReplayWarning",
                "MCAP Record & Replay",
                "FoxServices",
                "Diagnostics",
            };
            var actualWorkflowOrder = directWorkflowCalls
                .Select(DescribeTopLevelWorkflowCall)
                .ToArray();
            var dataTransport = directSections.Where(invocation => HasStringHeading(invocation, "Data Transport")).ToArray();
            var mcap = directSections.Where(invocation => HasStringHeading(invocation, "MCAP Record & Replay")).ToArray();
            var allDataTransport = executableSections.Where(invocation => HasStringHeading(invocation, "Data Transport")).ToArray();
            var allMcap = executableSections.Where(invocation => HasStringHeading(invocation, "MCAP Record & Replay")).ToArray();
            var bridgeSectionCallbacks = allInvocations
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection")
                                     && HasMethodGroupArgument(invocation, "DrawRos2BridgeSection"))
                .ToArray();

            Check(actualWorkflowOrder.SequenceEqual(expectedWorkflowOrder)
                  && allDataTransport.Length == 1
                  && dataTransport.Length == 1
                  && ReferenceEquals(allDataTransport[0], dataTransport[0])
                  && HasMethodGroupArgument(dataTransport[0], "DrawDataTransportSection")
                  && allMcap.Length == 1
                  && mcap.Length == 1
                  && ReferenceEquals(allMcap[0], mcap[0])
                  && HasMethodGroupArgument(mcap[0], "DrawMcapSection")
                  && Array.IndexOf(directSections, dataTransport[0]) < Array.IndexOf(directSections, mcap[0]),
                "180A-1: Manager Inspector keeps Connection, Data Transport, warning, sibling MCAP, services, and diagnostics in the exact workflow order");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "Publish Data")),
                "180A-2: Publish Data is no longer a top-level workflow section");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "Subscribe Data")),
                "180A-3: Subscribe Data is no longer a top-level workflow section");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "ROS2 Runtime (R2FU)")
                                                        || HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)")),
                "180A-4: ROS 2 Native Runtime (R2FU) is no longer a top-level workflow section");
            Check(bridgeSectionCallbacks.Length == 0
                  && !allInvocations.Any(invocation => IsInvocationNamed(invocation, "DrawRos2BridgeSection")),
                "180A-5: ROS2 Bridge has neither a top-level callback section nor a direct top-level draw");
        }

        private static void VerifyNestedTransportWorkflow(
            MethodDeclarationSyntax dataTransport,
            MethodDeclarationSyntax publishData)
        {
            var subsections = DirectInvocations(dataTransport)
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection"))
                .ToArray();
            var allInvocations = AllInvocations(dataTransport);
            var nativeSubsections = allInvocations
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)"))
                .ToArray();
            var nativeDemandBranches = DirectIfStatements(dataTransport)
                .Where(HasNativeDemandCondition)
                .ToArray();
            var branchNativeSubsections = nativeDemandBranches
                .SelectMany(DirectThenStatements)
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null
                                     && IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)"))
                .ToArray();
            var bridgeSubsections = AllInvocations(publishData)
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Bridge Output"))
                .ToArray();
            var bridgeEnabledBranches = DirectIfStatements(publishData)
                .Where(statement => HasSerializedBooleanCondition(statement, "_ros2BridgeEnabled"))
                .ToArray();
            var branchBridgeSubsections = bridgeEnabledBranches
                .SelectMany(DirectThenStatements)
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null
                                     && IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Bridge Output"))
                .ToArray();

            Check(!ContainsStringLiteral(dataTransport, "MCAP Record & Replay")
                  && !ContainsIdentifier(dataTransport, "DrawMcapSection")
                  && !ContainsIdentifier(dataTransport, "DrawRos2BridgeSection"),
                "180B-1: Data Transport contains no MCAP Record & Replay child workflow");
            Check(subsections.Length == 2
                  && HasExactlyOneSubsection(
                      subsections,
                      "Publish",
                      "DataTransportPublish",
                      "_dataTransportPublishExpanded",
                      "DrawPublishDataSection")
                  && HasExactlyOneSubsection(
                      subsections,
                      "Subscribe",
                      "DataTransportSubscribe",
                      "_dataTransportSubscribeExpanded",
                      "DrawSubscribeDataSection")
                  && HasStringHeading(subsections[0], "Publish")
                  && HasStringHeading(subsections[1], "Subscribe"),
                "180B-2: Data Transport nests the public Publish workflow");
            Check(nativeSubsections.Length == 1
                  && HasSubsectionArguments(
                      nativeSubsections[0],
                      "ROS 2 Native Runtime (R2FU)",
                      "DataTransportNativeRuntime",
                      "_dataTransportNativeRuntimeExpanded",
                      "DrawR2fuRuntimeSection")
                  && nativeDemandBranches.Length == 1
                  && branchNativeSubsections.Length == 1
                  && ReferenceEquals(nativeSubsections[0], branchNativeSubsections[0]),
                "180B-3: Data Transport nests ROS 2 Native Runtime (R2FU) only under native demand");
            Check(bridgeSubsections.Length == 1
                  && HasSubsectionArguments(
                      bridgeSubsections[0],
                      "ROS 2 Bridge Output",
                      "DataTransportRos2Bridge",
                      "_dataTransportRos2BridgeExpanded",
                      "DrawRos2BridgeSection")
                  && bridgeEnabledBranches.Length == 1
                  && branchBridgeSubsections.Length == 1
                  && ReferenceEquals(bridgeSubsections[0], branchBridgeSubsections[0]),
                "180B-4: Publish nests persisted ROS 2 Bridge Output only when its destination is enabled");
        }

        private static void VerifyPublishPresentation(MethodDeclarationSyntax publishData)
        {
            var directInvocations = DirectInvocations(publishData).ToArray();
            var allInvocations = AllInvocations(publishData);
            var nativeOutputBranches = DirectIfStatements(publishData)
                .Where(statement => HasSerializedBooleanCondition(statement, "_ros2NativeEnabled"))
                .ToArray();
            var nativeQosHelp = nativeOutputBranches
                .SelectMany(DirectThenStatements)
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null
                                     && IsInvocationNamed(invocation, "HelpBox")
                                     && HasStringArgument(
                                         invocation,
                                         0,
                                         "This Manager has no global ROS2 Native publish QoS override; configure QoS on individual R2FU publishers.")
                                     && HasMessageTypeInfoArgument(invocation, 1))
                .ToArray();

            Check(allInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                 && HasStringHeading(invocation, "Publish Destinations")) == 1
                  && HasExactlyOneLabeledProperty(directInvocations, "_foxgloveOutputEnabled", "Foxglove WebSocket")
                  && HasExactlyOneLabeledProperty(directInvocations, "_ros2NativeEnabled", "ROS 2 Native (R2FU)")
                  && HasExactlyOneLabeledProperty(directInvocations, "_ros2BridgeEnabled", "ROS 2 Bridge")
                  && !ContainsStringLiteral(publishData, "Output Mode"),
                "180E-1: Publish renders the three independent approved destinations without the obsolete output-mode heading");
            Check(allInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                 && HasStringHeading(invocation, "Publisher Encoding")) == 1
                  && HasExactlyOneLabeledInvocation(
                      allInvocations,
                      "DrawGlobalEncodingProperty",
                      "_defaultPublisherEncoding",
                      "Component Publisher Encoding")
                  && HasExactlyOneLabeledInvocation(
                      allInvocations,
                      "DrawProperty",
                      "_allowPublisherOverride",
                      "Allow Component Publisher Override")
                  && allInvocations.Count(invocation => IsInvocationNamed(invocation, "DrawFoxRunWireEncoding")
                                                 && HasStringArgument(invocation, 1, "FoxRun Contract Encoding")) == 1
                  && ContainsStringLiteral(
                      publishData,
                      "Component publishers and generated FoxRun contracts use independent default encodings.")
                  && !ContainsStringLiteral(publishData, "Default FoxRun Publish Encoding"),
                "180E-2: Publish labels the independent component and FoxRun contract encoding defaults by source");
            Check(nativeOutputBranches.Length == 1
                  && nativeQosHelp.Length == 1,
                "180E-3: selected ROS 2 Native output explains that Manager has no global publish QoS override");
        }

        private static void VerifySubscribePresentation(
            MethodDeclarationSyntax subscribeData,
            MethodDeclarationSyntax nativeQos,
            MethodDeclarationSyntax nativeBudget,
            MethodDeclarationSyntax nativeBudgetUnit)
        {
            var allInvocations = AllInvocations(subscribeData);
            var directInvocations = DirectInvocations(subscribeData).ToArray();
            var webSocketBranches = subscribeData.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .Where(statement => HasIdentifierCondition(statement, "showWebSocket"))
                .ToArray();
            var nativeBranches = subscribeData.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .Where(statement => HasIdentifierCondition(statement, "showRos2Native"))
                .ToArray();
            var webSocketBranchInvocations = webSocketBranches
                .SelectMany(DirectThenStatements)
                .SelectMany(AllInvocations)
                .ToArray();
            var nativeBranchInvocations = nativeBranches
                .SelectMany(DirectThenStatements)
                .SelectMany(AllInvocations)
                .ToArray();

            Check(allInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                && HasStringHeading(invocation, "Input Transport")) == 1
                  && allInvocations.Count(invocation => IsInvocationNamed(invocation, "Draw")
                                                && HasStringArgument(invocation, 2, "Default Input Transport")) == 1
                  && !ContainsStringLiteral(subscribeData, "Subscription Protocol")
                  && !ContainsStringLiteral(subscribeData, "Default Subscription Protocol"),
                "180F-1: Subscribe names its provider and encoding selector Input Transport without the obsolete protocol terminology");
            Check(HasExactlyOneLabeledProperty(directInvocations, "_enableFoxRunInbound", "Enable FoxRun Subscriptions")
                  && allInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                && HasStringHeading(invocation, "Subscription Delivery")) == 1
                  && HasExactlyOneLabeledProperty(
                      allInvocations,
                      "_foxRunInboundMaxMessagesPerSecondPerTopic",
                      "Subscription Rate Limit Hz (per Topic)")
                  && ContainsStringLiteralFragment(
                      subscribeData,
                      "captured provider, WebSocket encoding, QoS, copy budget, and rate."),
                "180F-2: Subscribe keeps its enable gate, delivery rate, and complete frozen-session policy boundary");
            Check(webSocketBranches.Length == 1
                  && HasProviderVisibilityRule(
                      subscribeData,
                      "showWebSocket",
                      "FoxgloveWebSocket")
                  && webSocketBranchInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                         && HasStringHeading(invocation, "Foxglove WebSocket Input")) == 1
                  && HasExactlyOneLabeledProperty(
                      webSocketBranchInvocations,
                      "_allowRemoteFoxRunInboundWithSharedToken",
                      "Allow Remote FoxRun Subscriptions With Shared Token")
                  && HasExactlyOneLabeledProperty(
                      webSocketBranchInvocations,
                      "_foxRunInboundMaxPayloadBytes",
                      "Subscription Max Payload Bytes"),
                "180F-3: WebSocket input remains visible for a selected or explicit generated WebSocket contract");
            Check(nativeBranches.Length == 1
                  && HasProviderVisibilityRule(
                      subscribeData,
                      "showRos2Native",
                      "Ros2Native")
                  && nativeBranchInvocations.Count(invocation => IsInvocationNamed(invocation, "Subheader")
                                                      && HasStringHeading(invocation, "ROS 2 Native Input")) == 1
                  && nativeBranchInvocations.Count(invocation => IsInvocationNamed(invocation, "DrawRos2NativeSubscriptionQos")) == 1
                  && nativeBranchInvocations.Count(invocation => IsInvocationNamed(invocation, "DrawRos2NativeCopyBudget")) == 1
                  && !ContainsStringLiteral(subscribeData, "Native Copied-Data Budget Bytes"),
                "180F-4: native input remains visible for a selected or explicit generated native contract and uses dedicated QoS and budget controls");
            Check(nativeQos != null
                  && AllInvocations(nativeQos).Count(invocation => IsInvocationNamed(invocation, "NormalizeSerializedManagerDefault")) == 1
                  && AllInvocations(nativeQos).Count(invocation => IsInvocationNamed(invocation, "Popup")) == 1
                  && AllInvocations(nativeQos).Count(invocation => IsInvocationNamed(invocation, "HelpBox")) == 1
                  && !ContainsStringLiteral(nativeQos, "Inherit"),
                "180F-5: native QoS normalizes malformed serialized defaults and displays only the concrete portable choices");
            Check(nativeBudget != null
                  && nativeBudgetUnit != null
                  && AllInvocations(nativeBudgetUnit).Count(invocation => IsInvocationNamed(invocation, "GetInt")) == 1
                  && AllInvocations(nativeBudget).Count(invocation => IsInvocationNamed(invocation, "SetInt")) == 1,
                "180F-6: native copied-message budget keeps its display unit in SessionState instead of Manager serialization");
            Check(nativeBudget != null
                  && AllInvocations(nativeBudget).Any(invocation => IsInvocationNamed(invocation, "ToDisplayValue"))
                  && AllInvocations(nativeBudget).Count(invocation => IsInvocationNamed(invocation, "ToClampedBytes")) == 1
                  && AllInvocations(nativeBudget).Count(invocation => IsInvocationNamed(invocation, "NormalizeSerializedBytes")) == 1
                  && ContainsStringLiteralFragment(nativeBudget, "Native Copied-Message Budget")
                  && ContainsStringLiteralFragment(nativeBudget, "bytes"),
                "180F-7: native copied-message budget converts KiB or MiB deterministically and renders its exact stored-byte equivalent");
        }

        private static void VerifyFoldoutStateModel(string mainInspector, MethodDeclarationSyntax foldoutState)
        {
            var expectedFields = new[]
            {
                "_dataTransportExpanded",
                "_dataTransportPublishExpanded",
                "_dataTransportSubscribeExpanded",
                "_dataTransportNativeRuntimeExpanded",
                "_dataTransportRos2BridgeExpanded",
            };
            var obsoleteFields = new[]
            {
                "_publishDataExpanded",
                "_subscribeDataExpanded",
                "_r2fuRuntimeExpanded",
                "_ros2BridgeExpanded",
            };
            var migration = DirectIfStatements(foldoutState)
                .Where(HasDataTransportMigrationCondition)
                .ToArray();
            var migrationStatements = migration.Length == 1
                ? DirectThenStatements(migration[0]).ToArray()
                : Array.Empty<StatementSyntax>();
            var migrationInvocations = migrationStatements
                .SelectMany(AllInvocations)
                .ToArray();
            var legacyLocalNames = FindLegacyFoldoutLocalNames(migrationStatements);
            var markerWrites = migrationInvocations
                .Where(invocation => IsSessionStateInvocationNamed(invocation, "SetInt")
                                     && HasInspectorFoldoutKeyArgument(invocation, 0, "DataTransportFoldoutMigrationVersion")
                                     && HasIntegerLiteralArgument(invocation, 1, 1))
                .ToArray();
            var newLoads = AllInvocations(foldoutState)
                .Where(invocation => IsSessionStateInvocationNamed(invocation, "GetBool")
                                     && DataTransportFoldoutKeys.Any(key => HasInspectorFoldoutKeyArgument(invocation, 0, key)))
                .ToArray();
            var hasUnsupportedSessionStateProbe = AllInvocations(foldoutState)
                .Any(IsSessionStateHasInvocation);

            Check(expectedFields.All(field => CountBoolFieldDeclarations(mainInspector, field) == 1)
                  && obsoleteFields.All(field => CountBoolFieldDeclarations(mainInspector, field) == 0),
                "180C-1: Data Transport owns exactly its five persisted foldout fields and retires the four legacy top-level fields");
            Check(migration.Length == 1
                  && LegacyTransportFoldoutKeys.All(key => legacyLocalNames.ContainsKey(key))
                  && DataTransportFoldoutKeys.All(key => newLoads.Count(invocation => HasInspectorFoldoutKeyArgument(invocation, 0, key)) == 1)
                  && DataTransportFoldoutFields.Count == DataTransportFoldoutKeys.Length
                  && DataTransportFoldoutKeys.All(key => DataTransportFoldoutFields.ContainsKey(key)
                                                     && HasMatchingFoldoutLoad(foldoutState, key, DataTransportFoldoutFields[key]))
                  && HasMigratedChildSeed(migrationInvocations, "DataTransportPublish", legacyLocalNames, "PublishData")
                  && HasMigratedChildSeed(migrationInvocations, "DataTransportSubscribe", legacyLocalNames, "SubscribeData")
                  && HasMigratedChildSeed(migrationInvocations, "DataTransportNativeRuntime", legacyLocalNames, "R2fuRuntime")
                  && HasMigratedChildSeed(migrationInvocations, "DataTransportRos2Bridge", legacyLocalNames, "Ros2Bridge")
                  && HasMigratedParentSeed(migrationInvocations, legacyLocalNames)
                  && markerWrites.Length == 1
                  && newLoads.All(invocation => markerWrites[0].SpanStart < invocation.SpanStart)
                  && !hasUnsupportedSessionStateProbe,
                "180C-2: one-time SessionState migration assigns each persisted foldout to its matching field, seeds the parent from the four legacy children, and writes its marker before loading new state");
        }

        private static void VerifyParentSectionPresentationHelper(MethodDeclarationSyntax section)
        {
            var workflowSections = AllInvocations(section)
                .Where(invocation => IsInvocationNamed(invocation, "WorkflowSection"))
                .ToArray();
            var closedSectionReturns = DirectIfStatements(section)
                .Where(statement => statement.Condition is PrefixUnaryExpressionSyntax prefix
                                    && prefix.IsKind(SyntaxKind.LogicalNotExpression)
                                    && prefix.Operand is InvocationExpressionSyntax invocation
                                    && IsInvocationNamed(invocation, "WorkflowSection")
                                    && DirectThenStatements(statement).OfType<ReturnStatementSyntax>().Any())
                .ToArray();

            Check(workflowSections.Length == 1
                  && HasInspectorFoldoutKeyIdentifierArgument(workflowSections[0], 1, "sessionStateName")
                  && HasRefIdentifierArgument(workflowSections[0], 2, "expanded")
                  && closedSectionReturns.Length == 1
                  && HasExceptionSafeIndentedContents(section),
                "180D-1: parent Inspector sections preserve collapsed behavior and restore indentation with try/finally");
        }

        private static void VerifySubsectionPresentationHelper(MethodDeclarationSyntax subsection)
        {
            var workflowSubsections = AllInvocations(subsection)
                .Where(invocation => IsInvocationNamed(invocation, "WorkflowSubsection"))
                .ToArray();
            var closedSubsectionReturns = DirectIfStatements(subsection)
                .Where(statement => statement.Condition is PrefixUnaryExpressionSyntax prefix
                                    && prefix.IsKind(SyntaxKind.LogicalNotExpression)
                                    && prefix.Operand is InvocationExpressionSyntax invocation
                                    && IsInvocationNamed(invocation, "WorkflowSubsection")
                                    && DirectThenStatements(statement).OfType<ReturnStatementSyntax>().Any())
                .ToArray();
            Check(workflowSubsections.Length == 1
                  && HasInspectorFoldoutKeyIdentifierArgument(workflowSubsections[0], 1, "sessionStateName")
                  && HasRefIdentifierArgument(workflowSubsections[0], 2, "expanded")
                  && closedSubsectionReturns.Length == 1
                  && HasExceptionSafeIndentedContents(subsection),
                "180D-2: Data Transport subsection presentation persists the layout foldout and confines indentation to expanded contents with try/finally");
        }

        private static bool HasExceptionSafeIndentedContents(MethodDeclarationSyntax section)
        {
            var indentationMutations = section == null
                ? Array.Empty<SyntaxNode>()
                : section.DescendantNodes()
                    .Where(IsEditorGuiIndentMutation)
                    .ToArray();
            var increments = indentationMutations
                .Where(IsEditorGuiIndentIncrement)
                .ToArray();
            var decrements = indentationMutations
                .Where(IsEditorGuiIndentDecrement)
                .ToArray();
            var tryStatements = section?.Body?.Statements.OfType<TryStatementSyntax>().ToArray()
                                ?? Array.Empty<TryStatementSyntax>();
            var tryStatement = tryStatements.Length == 1 ? tryStatements[0] : null;
            var drawsContents = tryStatement != null
                                && tryStatement.Block.DescendantNodes()
                                    .OfType<InvocationExpressionSyntax>()
                                    .Any(invocation => IsInvocationNamed(invocation, "drawContents"));
            var decrementsInFinally = tryStatement?.Finally != null
                                      && tryStatement.Finally.Block.DescendantNodes()
                                          .Any(IsEditorGuiIndentDecrement);

            return increments.Length == 1
                   && decrements.Length == 1
                   && tryStatement != null
                   && increments[0].SpanStart < tryStatement.SpanStart
                   && drawsContents
                   && decrementsInFinally;
        }

        private static MethodDeclarationSyntax FindMethod(string source, string methodName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == methodName && method.Body != null)
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : DirectInvocations(method.Body.Statements);
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectInvocations(IEnumerable<StatementSyntax> statements)
        {
            foreach (var statement in statements.OfType<ExpressionStatementSyntax>())
            {
                if (statement.Expression is InvocationExpressionSyntax invocation)
                    yield return invocation;
            }
        }

        private static IEnumerable<IfStatementSyntax> DirectIfStatements(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<IfStatementSyntax>()
                : method.Body.Statements.OfType<IfStatementSyntax>();
        }

        private static IEnumerable<InvocationExpressionSyntax> ExecutableInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : method.Body.Statements.SelectMany(ExecutableInvocations);
        }

        private static IEnumerable<InvocationExpressionSyntax> ExecutableInvocations(StatementSyntax statement)
        {
            return statement is LocalFunctionStatementSyntax
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : statement.DescendantNodes(ShouldDescendIntoExecutableNode).OfType<InvocationExpressionSyntax>();
        }

        private static bool ShouldDescendIntoExecutableNode(SyntaxNode node)
        {
            return !(node is AnonymousFunctionExpressionSyntax)
                   && !(node is LocalFunctionStatementSyntax);
        }

        private static IEnumerable<StatementSyntax> DirectThenStatements(IfStatementSyntax statement)
        {
            return DirectBranchStatements(statement.Statement);
        }

        private static IEnumerable<StatementSyntax> DirectBranchStatements(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements;

            return new[] { statement };
        }

        private static InvocationExpressionSyntax[] AllInvocations(MethodDeclarationSyntax method)
        {
            return method == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        }

        private static IEnumerable<InvocationExpressionSyntax> AllInvocations(StatementSyntax statement)
        {
            return statement == null
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>();
        }

        private static bool HasExactlyOneSubsection(
            IEnumerable<InvocationExpressionSyntax> subsections,
            string heading,
            string sessionStateName,
            string expandedField,
            string callback)
        {
            var matches = subsections.Where(invocation => HasStringHeading(invocation, heading)).ToArray();
            return matches.Length == 1
                   && HasSubsectionArguments(matches[0], heading, sessionStateName, expandedField, callback);
        }

        private static bool HasSubsectionArguments(
            InvocationExpressionSyntax invocation,
            string heading,
            string sessionStateName,
            string expandedField,
            string callback)
        {
            return HasStringHeading(invocation, heading)
                   && HasStringArgument(invocation, 1, sessionStateName)
                   && HasRefIdentifierArgument(invocation, 2, expandedField)
                   && HasMethodGroupArgument(invocation, callback);
        }

        private static bool HasNativeDemandCondition(IfStatementSyntax statement)
        {
            return statement.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "HasR2fuNativeRuntimeDemand")
                   && invocation.ArgumentList.Arguments.Count == 0;
        }

        private static bool HasSerializedBooleanCondition(IfStatementSyntax statement, string propertyName)
        {
            return statement?.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "GetBool")
                   && HasStringArgument(invocation, 0, propertyName);
        }

        private static bool HasIdentifierCondition(IfStatementSyntax statement, string identifier)
        {
            return statement?.Condition is IdentifierNameSyntax condition
                   && condition.Identifier.ValueText == identifier;
        }

        private static bool HasProviderVisibilityRule(
            MethodDeclarationSyntax method,
            string localName,
            string providerMemberName)
        {
            return method?.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Any(variable => variable.Identifier.ValueText == localName
                                 && variable.Initializer?.Value.DescendantNodesAndSelf()
                                     .OfType<InvocationExpressionSyntax>()
                                     .Any(invocation => IsInvocationNamed(invocation, "HasExplicitSubscriptionProvider")
                                                        && HasProviderMemberArgument(invocation, providerMemberName)) == true
                                 && variable.Initializer.Value.DescendantNodesAndSelf()
                                     .OfType<BinaryExpressionSyntax>()
                                     .Any(comparison => comparison.IsKind(SyntaxKind.EqualsExpression)
                                                        && HasSelectedProviderComparison(
                                                            comparison,
                                                            providerMemberName))) == true;
        }

        private static bool HasSelectedProviderComparison(
            BinaryExpressionSyntax comparison,
            string providerMemberName)
        {
            return (IsIdentifierNamed(comparison.Left, "selectedProvider")
                    && IsProviderMember(comparison.Right, providerMemberName))
                   || (IsProviderMember(comparison.Left, providerMemberName)
                       && IsIdentifierNamed(comparison.Right, "selectedProvider"));
        }

        private static bool IsIdentifierNamed(ExpressionSyntax expression, string identifier)
        {
            return expression is IdentifierNameSyntax name
                   && name.Identifier.ValueText == identifier;
        }

        private static bool IsProviderMember(ExpressionSyntax expression, string memberName)
        {
            return expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "FoxRunSubscriptionProvider"
                   && memberAccess.Name.Identifier.ValueText == memberName;
        }

        private static bool HasProviderMemberArgument(
            InvocationExpressionSyntax invocation,
            string memberName)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count == 1
                   && invocation.ArgumentList.Arguments[0].Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "FoxRunSubscriptionProvider"
                   && memberAccess.Name.Identifier.ValueText == memberName;
        }

        private static bool IsInvocationNamed(InvocationExpressionSyntax invocation, string name)
        {
            if (invocation == null)
                return false;

            if (invocation.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == name;

            return invocation.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == name;
        }

        private static bool HasStringHeading(InvocationExpressionSyntax invocation, string heading)
        {
            return HasStringArgument(invocation, 0, heading);
        }

        private static bool HasStringArgument(InvocationExpressionSyntax invocation, int argumentIndex, string value)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is LiteralExpressionSyntax literal
                   && literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                   && literal.Token.ValueText == value;
        }

        private static bool HasMethodGroupArgument(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation != null && invocation.ArgumentList.Arguments.Any(argument =>
                IsMethodGroupNamed(argument.Expression, methodName));
        }

        private static bool IsMethodGroupNamed(ExpressionSyntax expression, string methodName)
        {
            if (expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == methodName;

            return expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == methodName;
        }

        private static bool HasExactlyOneLabeledProperty(
            IEnumerable<InvocationExpressionSyntax> invocations,
            string propertyName,
            string label)
        {
            return HasExactlyOneLabeledInvocation(invocations, "DrawProperty", propertyName, label);
        }

        private static bool HasExactlyOneLabeledInvocation(
            IEnumerable<InvocationExpressionSyntax> invocations,
            string invocationName,
            string propertyName,
            string label)
        {
            return invocations.Count(invocation => IsInvocationNamed(invocation, invocationName)
                                                && HasStringArgument(invocation, 0, propertyName)
                                                && HasStringArgument(invocation, 1, label)) == 1;
        }

        private static bool HasMessageTypeInfoArgument(InvocationExpressionSyntax invocation, int argumentIndex)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "MessageType"
                   && memberAccess.Name.Identifier.ValueText == "Info";
        }

        private static string DescribeTopLevelWorkflowCall(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawRecordingReplayWarning")
                ? "DrawRecordingReplayWarning"
                : invocation?.ArgumentList.Arguments.FirstOrDefault().Expression is LiteralExpressionSyntax literal
                    ? literal.Token.ValueText
                    : string.Empty;
        }

        private static bool HasDataTransportMigrationCondition(IfStatementSyntax statement)
        {
            return statement?.Condition is BinaryExpressionSyntax comparison
                   && comparison.IsKind(SyntaxKind.LessThanExpression)
                   && comparison.Left is InvocationExpressionSyntax version
                   && IsSessionStateInvocationNamed(version, "GetInt")
                   && HasInspectorFoldoutKeyArgument(version, 0, "DataTransportFoldoutMigrationVersion")
                   && HasIntegerLiteralArgument(version, 1, 0)
                   && comparison.Right is LiteralExpressionSyntax literal
                   && literal.IsKind(SyntaxKind.NumericLiteralExpression)
                   && literal.Token.Value is int value
                   && value == 1;
        }

        private static bool IsSessionStateInvocationNamed(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "SessionState"
                   && memberAccess.Name.Identifier.ValueText == methodName;
        }

        private static bool IsSessionStateHasInvocation(InvocationExpressionSyntax invocation)
        {
            return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "SessionState"
                   && memberAccess.Name.Identifier.ValueText.StartsWith("Has", StringComparison.Ordinal);
        }

        private static bool HasInspectorFoldoutKeyArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string key)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is InvocationExpressionSyntax keyInvocation
                   && IsInvocationNamed(keyInvocation, "InspectorFoldoutKey")
                   && HasStringArgument(keyInvocation, 0, key);
        }

        private static bool HasInspectorFoldoutKeyIdentifierArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is InvocationExpressionSyntax keyInvocation
                   && IsInvocationNamed(keyInvocation, "InspectorFoldoutKey")
                   && keyInvocation.ArgumentList.Arguments.Count == 1
                   && keyInvocation.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static bool HasIntegerLiteralArgument(InvocationExpressionSyntax invocation, int argumentIndex, int value)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is LiteralExpressionSyntax literal
                   && literal.IsKind(SyntaxKind.NumericLiteralExpression)
                   && literal.Token.Value is int intValue
                   && intValue == value;
        }

        private static bool HasRefIdentifierArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static Dictionary<string, string> FindLegacyFoldoutLocalNames(IEnumerable<StatementSyntax> statements)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var declaration in statements.OfType<LocalDeclarationStatementSyntax>())
            {
                foreach (var variable in declaration.Declaration.Variables)
                {
                    if (!(variable.Initializer?.Value is InvocationExpressionSyntax invocation)
                        || !IsSessionStateInvocationNamed(invocation, "GetBool"))
                        continue;

                    foreach (var key in LegacyTransportFoldoutKeys)
                    {
                        if (HasInspectorFoldoutKeyArgument(invocation, 0, key)
                            && HasIntegerLiteralArgument(invocation, 1, 0) == false
                            && invocation.ArgumentList.Arguments.Count > 1
                            && invocation.ArgumentList.Arguments[1].Expression.IsKind(SyntaxKind.FalseLiteralExpression))
                        {
                            result[key] = variable.Identifier.ValueText;
                        }
                    }
                }
            }

            return result;
        }

        private static bool HasMigratedChildSeed(
            IEnumerable<InvocationExpressionSyntax> invocations,
            string newKey,
            IReadOnlyDictionary<string, string> legacyLocalNames,
            string oldKey)
        {
            return legacyLocalNames.TryGetValue(oldKey, out var oldLocal)
                   && invocations.Count(invocation => IsSessionStateInvocationNamed(invocation, "SetBool")
                                                && HasInspectorFoldoutKeyArgument(invocation, 0, newKey)
                                                && HasIdentifierArgument(invocation, 1, oldLocal)) == 1;
        }

        private static bool HasMatchingFoldoutLoad(
            MethodDeclarationSyntax foldoutState,
            string key,
            string field)
        {
            var matchingLoads = foldoutState?.Body?.Statements
                                    .OfType<ExpressionStatementSyntax>()
                                    .Select(statement => statement.Expression as AssignmentExpressionSyntax)
                                    .Where(assignment => assignment != null
                                                         && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                                                         && assignment.Left is IdentifierNameSyntax target
                                                         && target.Identifier.ValueText == field
                                                         && assignment.Right is InvocationExpressionSyntax read
                                                         && IsSessionStateInvocationNamed(read, "GetBool")
                                                         && HasInspectorFoldoutKeyArgument(read, 0, key)
                                                         && read.ArgumentList.Arguments.Count == 2
                                                         && read.ArgumentList.Arguments[1].Expression.IsKind(SyntaxKind.FalseLiteralExpression))
                                    .ToArray()
                                ?? Array.Empty<AssignmentExpressionSyntax>();
            return matchingLoads.Length == 1;
        }

        private static bool HasMigratedParentSeed(
            IEnumerable<InvocationExpressionSyntax> invocations,
            IReadOnlyDictionary<string, string> legacyLocalNames)
        {
            var parentSeeds = invocations
                .Where(invocation => IsSessionStateInvocationNamed(invocation, "SetBool")
                                     && HasInspectorFoldoutKeyArgument(invocation, 0, "DataTransport"))
                .ToArray();
            if (parentSeeds.Length != 1 || parentSeeds[0].ArgumentList.Arguments.Count < 2)
                return false;

            var parentExpression = parentSeeds[0].ArgumentList.Arguments[1].Expression;
            var legacyOperands = new List<string>();
            return CollectLogicalOrIdentifierOperands(parentExpression, legacyOperands)
                   && legacyOperands.Count == LegacyTransportFoldoutKeys.Length
                   && LegacyTransportFoldoutKeys.All(key => legacyLocalNames.TryGetValue(key, out var local)
                                                       && legacyOperands.Count(operand => operand == local) == 1);
        }

        private static bool CollectLogicalOrIdentifierOperands(ExpressionSyntax expression, ICollection<string> operands)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
                return CollectLogicalOrIdentifierOperands(parenthesized.Expression, operands);

            if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.LogicalOrExpression))
            {
                return CollectLogicalOrIdentifierOperands(binary.Left, operands)
                       && CollectLogicalOrIdentifierOperands(binary.Right, operands);
            }

            if (expression is IdentifierNameSyntax identifier)
            {
                operands.Add(identifier.Identifier.ValueText);
                return true;
            }

            return false;
        }

        private static bool HasIdentifierArgument(InvocationExpressionSyntax invocation, int argumentIndex, string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static int CountBoolFieldDeclarations(string source, string fieldName)
        {
            return CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .Where(field => field.Declaration.Type is PredefinedTypeSyntax type
                                && type.Keyword.IsKind(SyntaxKind.BoolKeyword))
                .SelectMany(field => field.Declaration.Variables)
                .Count(variable => variable.Identifier.ValueText == fieldName);
        }

        private static bool IsEditorGuiIndentMutation(SyntaxNode node)
        {
            return node is AssignmentExpressionSyntax assignment
                   && IsEditorGuiIndentTarget(assignment.Left)
                   || node is PostfixUnaryExpressionSyntax unary
                   && IsEditorGuiIndentTarget(unary.Operand)
                   && (unary.IsKind(SyntaxKind.PostIncrementExpression)
                       || unary.IsKind(SyntaxKind.PostDecrementExpression));
        }

        private static bool IsEditorGuiIndentIncrement(SyntaxNode node)
        {
            return node is AssignmentExpressionSyntax assignment
                   && assignment.IsKind(SyntaxKind.AddAssignmentExpression)
                   && IsEditorGuiIndentTarget(assignment.Left)
                   || node is PostfixUnaryExpressionSyntax unary
                   && unary.IsKind(SyntaxKind.PostIncrementExpression)
                   && IsEditorGuiIndentTarget(unary.Operand);
        }

        private static bool IsEditorGuiIndentDecrement(SyntaxNode node)
        {
            return node is AssignmentExpressionSyntax assignment
                   && assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)
                   && IsEditorGuiIndentTarget(assignment.Left)
                   || node is PostfixUnaryExpressionSyntax unary
                   && unary.IsKind(SyntaxKind.PostDecrementExpression)
                   && IsEditorGuiIndentTarget(unary.Operand);
        }

        private static bool IsEditorGuiIndentTarget(ExpressionSyntax expression)
        {
            return expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Expression is IdentifierNameSyntax receiver
                   && receiver.Identifier.ValueText == "EditorGUI"
                   && memberAccess.Name.Identifier.ValueText == "indentLevel";
        }

        private static bool ContainsStringLiteral(MethodDeclarationSyntax method, string value)
        {
            return method != null
                   && method.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(literal =>
                       literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                       && literal.Token.ValueText == value);
        }

        private static bool ContainsStringLiteralFragment(MethodDeclarationSyntax method, string value)
        {
            return method != null
                   && method.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(literal =>
                       literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                       && literal.Token.ValueText.Contains(value, StringComparison.Ordinal));
        }

        private static bool ContainsIdentifier(MethodDeclarationSyntax method, string identifier)
        {
            return method != null
                   && method.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name =>
                       name.Identifier.ValueText == identifier);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
