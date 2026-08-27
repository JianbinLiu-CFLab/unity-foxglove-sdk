// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Provider-neutral Data Transport Inspector.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Guards the one Manager-owned transport selection UI and lazy optional
    /// Provider companion contract.
    /// </summary>
    public static class DataTransportInspectorValidation
    {
        private const string ManagerEditorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";
        private const string DataTransportPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.DataTransport.cs";
        private const string PublishDataPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.PublishData.cs";
        private const string SubscribeDataPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs";
        private const string DrawerRegistryPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxRunTransportProviderDrawerRegistry.cs";
        private const string TransportIdPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/Transport/FoxRunTransportId.cs";
        private const string ManagerRuntimePath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
        private const string ManagerProvidersPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs";
        private const string ManagerCoordinateMigrationPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs";
        private const string McapRecorderPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Recording/McapRecorder.cs";
        private const string SessionClientPublishHandlerPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/SessionClientPublishHandler.cs";
        private const string R2fuDrawerPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs";
        private const string BridgeDrawerPath =
            "Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2BridgeProviderDrawer.cs";

        private static int _passed;

        /// <summary>
        /// Validates that optional transport packages extend one neutral Manager
        /// workflow without making their companions eager core state.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine(
                "=== Phase 180: Provider-neutral Data Transport Inspector ===");
            _passed = 0;

            var managerEditor =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    ManagerEditorPath);
            var managerEditorSources =
                PhaseValidationSourceHelpers
                    .ReadFoxgloveManagerEditorSources();
            var dataTransport =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    DataTransportPath);
            var publishData =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    PublishDataPath);
            var subscribeData =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    SubscribeDataPath);
            var drawerRegistry =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    DrawerRegistryPath);
            var transportId =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    TransportIdPath);
            var r2fuDrawer =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    R2fuDrawerPath);
            var bridgeDrawer =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    BridgeDrawerPath);

            VerifyTopLevelWorkflow(
                FindMethod(managerEditor, "OnInspectorGUI"));
            VerifyNeutralSubsections(
                FindMethod(dataTransport, "DrawDataTransportSection"),
                FindMethod(
                    dataTransport,
                    "DrawDataTransportSubsection"));
            VerifyPublishSelection(
                publishData,
                FindMethod(publishData, "DrawPublishDataSection"));
            VerifySubscribeSelection(
                FindMethod(
                    subscribeData,
                    "DrawSubscribeDataSection"));
            VerifyDrawerContract(
                drawerRegistry,
                transportId,
                r2fuDrawer,
                bridgeDrawer);
            VerifyLazyProviderCompanions(
                managerEditorSources,
                FindMethod(
                    publishData,
                    "DrawFoxRunTransportProviderExtensions"),
                FindMethod(
                    publishData,
                    "ShouldEnsureProvider"));
            VerifyFoldoutState(managerEditor);
            VerifyMultiObjectInspectorBoundary(
                FindMethod(managerEditor, "OnInspectorGUI"));
            VerifyPassiveInspectorMutationBoundary(managerEditor);
            VerifyNeutralSerialization();
            VerifyDirectionalCoordinateRuntimePolicy();
            VerifyValidationRegistryEntry();

            Console.WriteLine(
                "Phase 180: " + _passed + " checks passed.");
        }

        private static void VerifyTopLevelWorkflow(
            MethodDeclarationSyntax topLevel)
        {
            var calls = topLevel.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    IsInvocationNamed(invocation, "DrawSection")
                    || IsInvocationNamed(
                        invocation,
                        "DrawRecordingReplayWarning"))
                .OrderBy(invocation => invocation.SpanStart)
                .Select(invocation =>
                    IsInvocationNamed(
                        invocation,
                        "DrawRecordingReplayWarning")
                        ? "DrawRecordingReplayWarning"
                        : StringArgument(invocation, 0))
                .ToArray();
            var expected = new[]
            {
                "Connection & Security",
                "Data Transport",
                "DrawRecordingReplayWarning",
                "MCAP Record & Replay",
                "FoxServices",
                "Diagnostics",
            };

            Check(calls.SequenceEqual(expected),
                "180A-1: Manager keeps one Data Transport workflow between Connection and sibling MCAP");
            Check(!topLevel.ToFullString().Contains(
                      "DrawSection(\"Publish Data\"",
                      StringComparison.Ordinal)
                  && !topLevel.ToFullString().Contains(
                      "DrawSection(\"Subscribe Data\"",
                      StringComparison.Ordinal)
                  && !topLevel.ToFullString().Contains(
                      "DrawRos2BridgeSection",
                      StringComparison.Ordinal)
                  && !topLevel.ToFullString().Contains(
                      "DrawR2fuRuntimeSection",
                      StringComparison.Ordinal),
                "180A-2: Provider-specific and directional controls are not separate top-level Manager sections");
        }

        private static void VerifyNeutralSubsections(
            MethodDeclarationSyntax dataTransport,
            MethodDeclarationSyntax subsection)
        {
            var childSections = InvocationsNamed(
                    dataTransport,
                    "DrawDataTransportSubsection")
                .OrderBy(invocation => invocation.SpanStart)
                .ToArray();
            var providerExtensions = InvocationsNamed(
                    dataTransport,
                    "DrawFoxRunTransportProviderExtensions")
                .ToArray();

            Check(childSections.Length == 2
                  && StringArgument(childSections[0], 0)
                  == "Publish Data"
                  && StringArgument(childSections[0], 1)
                  == "DataTransportPublish"
                  && HasIdentifierArgument(
                      childSections[0],
                      "DrawPublishDataSection")
                  && StringArgument(childSections[1], 0)
                  == "Subscribe Data"
                  && StringArgument(childSections[1], 1)
                  == "DataTransportSubscribe"
                  && HasIdentifierArgument(
                      childSections[1],
                      "DrawSubscribeDataSection"),
                "180B-1: Data Transport contains exactly one Publish Data and one Subscribe Data subsection");
            Check(providerExtensions.Length == 1
                  && providerExtensions[0].SpanStart
                  > childSections[1].SpanStart
                  && !dataTransport.ToFullString().Contains(
                      "ROS 2",
                      StringComparison.Ordinal)
                  && !dataTransport.ToFullString().Contains(
                      "R2FU",
                      StringComparison.Ordinal),
                "180B-2: optional Provider drawers extend the neutral workflow after both selections without ROS-specific core UI");

            var subsectionText = subsection.ToFullString();
            Check(subsectionText.Contains(
                      "FoxgloveManagerInspectorLayout.WorkflowSubsection",
                      StringComparison.Ordinal)
                  && subsectionText.Contains(
                      "EditorStyles.foldoutHeader",
                      StringComparison.Ordinal)
                  && subsection.DescendantNodes()
                      .OfType<TryStatementSyntax>()
                      .Any(statement =>
                          statement.Finally != null)
                  && subsectionText.Contains(
                      "EditorGUI.indentLevel++",
                      StringComparison.Ordinal)
                  && subsectionText.Contains(
                      "EditorGUI.indentLevel--",
                      StringComparison.Ordinal),
                "180B-3: both neutral subsections retain persistent bold foldouts and exception-safe indentation");
        }

        private static void VerifyPublishSelection(
            string publishDataSource,
            MethodDeclarationSyntax publishData)
        {
            var destinationDraws =
                InvocationsNamed(
                        publishData,
                        "DrawPublishTransportSelection")
                    .ToArray();
            var headings =
                InvocationsNamed(publishData, "Subheader")
                    .Where(invocation =>
                        HasStringArgument(
                            invocation,
                            "Publish Destinations"))
                    .ToArray();
            var encoding =
                InvocationsNamed(
                        publishData,
                        "DrawFoxRunEncoding")
                    .ToArray();
            var encodingGuard = encoding
                .Select(invocation => invocation.Ancestors()
                    .OfType<IfStatementSyntax>()
                    .FirstOrDefault())
                .SingleOrDefault();
            var text = publishDataSource;

            Check(headings.Length == 1
                  && destinationDraws.Length == 1
                  && text.Contains(
                      "FindCachedProperty(\"_foxRunPublishTransportIds\")",
                      StringComparison.Ordinal)
                  && text.Contains(
                      "EditorGUILayout.ToggleLeft",
                      StringComparison.Ordinal)
                  && text.Contains(
                      "Unavailable Provider",
                      StringComparison.Ordinal),
                "180C-1: Publish exposes one authoritative selectable destination collection and retains unavailable IDs visibly");
            Check(encoding.Length == 1
                  && encodingGuard != null
                  && encodingGuard.Condition.ToFullString().Contains(
                      "SerializedStringArrayContains",
                      StringComparison.Ordinal)
                  && encodingGuard.Condition.ToFullString().Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal),
                "180C-2: FoxRunEncoding remains a WebSocket-only control guarded by the built-in transport ID");
            Check(!text.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && !text.Contains("_ros2BridgeEnabled", StringComparison.Ordinal)
                  && !text.Contains(
                      "ROS 2 Native",
                      StringComparison.Ordinal)
                  && !text.Contains(
                      "ROS 2 Bridge",
                      StringComparison.Ordinal),
                "180C-3: core Publish UI contains no retired ROS destination fields or Provider-specific labels");
        }

        private static void VerifySubscribeSelection(
            MethodDeclarationSyntax subscribeData)
        {
            var sourceDraws =
                InvocationsNamed(
                        subscribeData,
                        "DrawSubscribeTransportSelection")
                    .ToArray();
            var encoding =
                InvocationsNamed(
                        subscribeData,
                        "DrawFoxRunEncoding")
                    .ToArray();
            var encodingGuard = encoding
                .Select(invocation => invocation.Ancestors()
                    .OfType<IfStatementSyntax>()
                    .FirstOrDefault())
                .SingleOrDefault();
            var text = subscribeData.ToFullString();

            Check(sourceDraws.Length == 1
                  && InvocationsNamed(
                          subscribeData,
                          "FindCachedProperty")
                      .Count(invocation =>
                          HasStringArgument(
                              invocation,
                              "_foxRunSubscribeTransportId"))
                  == 1
                  && InvocationsNamed(subscribeData, "DrawProperty")
                      .Count(invocation =>
                          HasStringArgument(
                              invocation,
                              "_enableFoxRunInbound")) == 1
                  && text.Contains("\"Source\"", StringComparison.Ordinal)
                  && text.Contains(
                      "Configured Provider is unavailable",
                      StringComparison.Ordinal),
                "180D-1: Subscribe exposes one enabled-state control and exactly one fail-closed Source selector");
            Check(encoding.Length == 1
                  && encodingGuard != null
                  && encodingGuard.Condition.ToFullString().Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && text.Contains(
                      "Default Subscribe Rate Hz",
                      StringComparison.Ordinal)
                  && text.Contains(
                      "Maximum Subscribe Rate Hz (per Topic)",
                      StringComparison.Ordinal),
                "180D-2: WebSocket-only encoding/security is source-guarded while neutral rate bounds remain shared");
            Check(!text.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && !text.Contains("_ros2BridgeEnabled", StringComparison.Ordinal)
                  && !text.Contains(
                      "ROS 2 Native",
                      StringComparison.Ordinal)
                  && !text.Contains(
                      "ROS 2 Bridge",
                      StringComparison.Ordinal),
                "180D-3: core Subscribe UI contains no retired ROS source fields or Provider-specific labels");
        }

        private static void VerifyDrawerContract(
            string drawerRegistry,
            string transportId,
            string r2fuDrawer,
            string bridgeDrawer)
        {
            Check(drawerRegistry.Contains(
                      "string TransportId { get; }",
                      StringComparison.Ordinal)
                  && drawerRegistry.Contains(
                      "string DisplayName { get; }",
                      StringComparison.Ordinal)
                  && drawerRegistry.Contains(
                      "int Order { get; }",
                      StringComparison.Ordinal)
                  && drawerRegistry.Contains(
                      "FoxRunTransportCapabilities Capabilities { get; }",
                      StringComparison.Ordinal)
                  && drawerRegistry.Contains(
                      "FoxRunEditorDefinitionRegistry<",
                      StringComparison.Ordinal),
                "180E-1: Editor drawer definitions expose stable ID, display name, explicit order, capabilities, and conflict-aware deterministic capture");
            Check(transportId.Contains(
                      "public const string Id = \"foxglove.websocket\"",
                      StringComparison.Ordinal)
                  && r2fuDrawer.Contains(
                      "FoxRunRos2TransportProvider.IdValue",
                      StringComparison.Ordinal)
                  && r2fuDrawer.Contains(
                      "\"ROS 2 Native (R2FU)\"",
                      StringComparison.Ordinal)
                  && r2fuDrawer.Contains(
                      "FoxRunTransportCapabilities.Publish",
                      StringComparison.Ordinal)
                  && r2fuDrawer.Contains(
                      "FoxRunTransportCapabilities.Subscribe",
                      StringComparison.Ordinal)
                  && bridgeDrawer.Contains(
                      "Ros2BridgeTransportProvider.ProviderId",
                      StringComparison.Ordinal)
                  && bridgeDrawer.Contains(
                      "\"ROS 2 Bridge\"",
                      StringComparison.Ordinal)
                  && bridgeDrawer.Contains(
                      "FoxRunTransportCapabilities.Publish",
                      StringComparison.Ordinal)
                  && bridgeDrawer.Contains(
                      "FoxRunTransportCapabilities.Subscribe",
                      StringComparison.Ordinal),
                "180E-2: built-in, R2FU, and Bridge identities and directional capabilities stay with their owning definitions");
        }

        private static void VerifyLazyProviderCompanions(
            string managerEditor,
            MethodDeclarationSyntax extensions,
            MethodDeclarationSyntax shouldEnsureProvider)
        {
            var loops = extensions.DescendantNodes()
                .OfType<ForEachStatementSyntax>()
                .Where(loop =>
                    loop.Identifier.ValueText == "drawer"
                    && loop.Expression.ToFullString().Contains(
                        "FoxRunTransportProviderDrawerRegistry.Capture",
                        StringComparison.Ordinal))
                .ToArray();
            var loop = loops.Length == 1 ? loops[0] : null;
            var ensureCalls = loop == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : InvocationsNamed(loop, "EnsureProvider")
                    .ToArray();
            var drawCalls = loop == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : InvocationsNamed(loop, "Draw")
                    .ToArray();
            var ensureGuard = ensureCalls.Length == 1
                ? ensureCalls[0].Ancestors()
                    .TakeWhile(node => node != loop)
                    .OfType<IfStatementSyntax>()
                    .FirstOrDefault()
                : null;
            var drawIsUnconditional =
                drawCalls.Length == 1
                && !drawCalls[0].Ancestors()
                    .TakeWhile(node => node != loop)
                    .OfType<IfStatementSyntax>()
                    .Any();
            var guardInvocation = ensureGuard == null
                ? null
                : UnwrapParentheses(
                    ensureGuard.Condition)
                    as InvocationExpressionSyntax;
            var guardCallsHelper =
                guardInvocation != null
                && IsInvocationNamed(
                    guardInvocation,
                    "ShouldEnsureProvider")
                && guardInvocation.ArgumentList.Arguments
                    .Select(argument =>
                        argument.Expression)
                    .OfType<IdentifierNameSyntax>()
                    .Select(identifier =>
                        identifier.Identifier.ValueText)
                    .SequenceEqual(new[]
                    {
                        "drawer",
                        "publishTransportIds",
                        "subscribeTransportId",
                    });
            var helperReturns = shouldEnsureProvider
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Where(statement =>
                    statement.Expression != null)
                .ToArray();
            var selectionReturn = helperReturns
                .SingleOrDefault(statement =>
                    statement.Expression
                        .DescendantNodesAndSelf()
                        .OfType<BinaryExpressionSyntax>()
                        .Any(expression =>
                            expression.IsKind(
                                SyntaxKind.LogicalOrExpression)));
            var selectionContext = selectionReturn == null
                ? string.Empty
                : ExpandGuardContext(
                    shouldEnsureProvider,
                    selectionReturn.Expression);
            var multiObjectFalseGate =
                shouldEnsureProvider.DescendantNodes()
                    .OfType<IfStatementSyntax>()
                    .Any(statement =>
                        statement.Condition.ToFullString()
                            .Contains(
                                "serializedObject.isEditingMultipleObjects",
                                StringComparison.Ordinal)
                        && statement.Statement
                            .DescendantNodesAndSelf()
                            .OfType<ReturnStatementSyntax>()
                            .Any(returnStatement =>
                                returnStatement.Expression
                                    is LiteralExpressionSyntax literal
                                && literal.IsKind(
                                    SyntaxKind.FalseLiteralExpression)));
            var isMultiObjectEditor =
                PhaseValidationSourceHelpers.TypeHasAttribute(
                    managerEditor,
                    "FoxgloveManagerEditor",
                    "CanEditMultipleObjects");
            var extensionText = extensions.ToFullString();

            Check(loop != null
                  && ensureCalls.Length == 1
                  && ensureGuard != null
                  && guardCallsHelper
                  && extensionText.Contains(
                      "_foxRunPublishTransportIds",
                      StringComparison.Ordinal)
                  && extensionText.Contains(
                      "_foxRunSubscribeTransportId",
                      StringComparison.Ordinal)
                  && selectionReturn != null
                  && selectionContext.Contains(
                      "drawer.TransportId",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "FoxRunTransportCapabilities.Publish",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "FoxRunTransportCapabilities.Subscribe",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "publishTransportIds",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "subscribeTransportId",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "publishTransportIds != null",
                      StringComparison.Ordinal)
                  && selectionContext.Contains(
                      "subscribeTransportId != null",
                      StringComparison.Ordinal)
                  && Count(
                      selectionContext,
                      "hasMultipleDifferentValues") >= 2
                  && isMultiObjectEditor
                  && multiObjectFalseGate,
                "180F-1: EnsureProvider is AST-nested only under publish/subscribe capability and ID demand, while multi-object editing never creates companions implicitly");
            Check(drawCalls.Length == 1
                  && drawIsUnconditional,
                "180F-2: every captured Provider drawer is still offered exactly one unconditional Draw call");
        }

        private static void VerifyFoldoutState(string managerEditor)
        {
            Check(Count(
                      managerEditor,
                      "private bool _dataTransportExpanded;") == 1
                  && Count(
                      managerEditor,
                      "private bool _dataTransportPublishExpanded;") == 1
                  && Count(
                      managerEditor,
                      "private bool _dataTransportSubscribeExpanded;") == 1
                  && !managerEditor.Contains(
                      "_dataTransportNativeRuntimeExpanded",
                      StringComparison.Ordinal)
                  && !managerEditor.Contains(
                      "_dataTransportRos2BridgeExpanded",
                      StringComparison.Ordinal)
                  && managerEditor.Contains(
                      "InspectorFoldoutKey(\"DataTransport\")",
                      StringComparison.Ordinal)
                  && managerEditor.Contains(
                      "InspectorFoldoutKey(\"DataTransportPublish\")",
                      StringComparison.Ordinal)
                  && managerEditor.Contains(
                      "InspectorFoldoutKey(\"DataTransportSubscribe\")",
                      StringComparison.Ordinal),
                "180G-1: foldout persistence belongs only to Data Transport and its two neutral directional subsections");
        }

        private static void VerifyMultiObjectInspectorBoundary(
            MethodDeclarationSyntax topLevel)
        {
            var multiObjectGuard = topLevel.DescendantNodes()
                .OfType<IfStatementSyntax>()
                .FirstOrDefault(statement =>
                    statement.Condition.ToFullString().Contains(
                        "serializedObject.isEditingMultipleObjects",
                        StringComparison.Ordinal));
            var guardText = multiObjectGuard?.Statement.ToFullString()
                            ?? string.Empty;
            var guardCallsCustomUi = multiObjectGuard != null
                && multiObjectGuard.Statement.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation =>
                        IsInvocationNamed(invocation, "SyncSerializedManager")
                        || IsInvocationNamed(invocation, "DrawSection")
                        || IsInvocationNamed(invocation, "DrawRecordingReplayWarning"));
            var guardReturns = multiObjectGuard != null
                && multiObjectGuard.Statement.DescendantNodesAndSelf()
                    .OfType<ReturnStatementSyntax>()
                    .Any();
            var hasDefaultInspector = multiObjectGuard != null
                && multiObjectGuard.Statement.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation =>
                        IsInvocationNamed(invocation, "DrawDefaultInspector"));

            Check(multiObjectGuard != null
                  && guardText.Contains(
                      "Multi-object editing",
                      StringComparison.Ordinal)
                  && hasDefaultInspector
                  && guardReturns
                  && !guardCallsCustomUi,
                "180J-1: multi-object Manager inspection is mixed-safe and exposes no representative custom actions");
        }

        private static void VerifyPassiveInspectorMutationBoundary(
            string managerEditor)
        {
            var inspector = FindMethod(managerEditor, "OnInspectorGUI");
            var inspectorText = inspector.ToFullString();
            var beginChange = inspectorText.IndexOf(
                "EditorGUI.BeginChangeCheck()",
                StringComparison.Ordinal);
            var endChange = inspectorText.IndexOf(
                "EditorGUI.EndChangeCheck()",
                beginChange < 0 ? 0 : beginChange,
                StringComparison.Ordinal);
            var apply = inspectorText.IndexOf(
                "serializedObject.ApplyModifiedProperties()",
                endChange < 0 ? 0 : endChange,
                StringComparison.Ordinal);
            var discard = inspectorText.IndexOf(
                "serializedObject.Update()",
                endChange < 0 ? 0 : endChange,
                StringComparison.Ordinal);

            Check(beginChange >= 0
                  && endChange > beginChange
                  && apply > endChange
                  && discard > endChange
                  && inspectorText.Contains(
                      "if (EditorGUI.EndChangeCheck())",
                      StringComparison.Ordinal),
                "180J-2: passive Manager repaint discards staged enum normalization and applies only an intentional edit");

            var status = FindMethod(managerEditor, "DrawCompactStatus");
            var statusText = status.ToFullString();
            var unavailable = statusText.IndexOf(
                "unavailable",
                StringComparison.OrdinalIgnoreCase);
            var refresh = statusText.IndexOf(
                "RefreshWebUrlCache",
                StringComparison.Ordinal);
            Check(statusText.Contains(
                      "_foxgloveOutputEnabled",
                      StringComparison.Ordinal)
                  && unavailable >= 0
                  && statusText.Contains(
                      "return;",
                      StringComparison.Ordinal)
                  && refresh > unavailable,
                "180J-3: disabled or invalid transport status is explicit and never synthesizes active URL actions");

            var transport = FindMethod(managerEditor, "DrawTransportModeProperty");
            var transportText = transport.ToFullString();
            var disabled = transportText.IndexOf(
                "if (!GetBool(\"_foxgloveOutputEnabled\"))",
                StringComparison.Ordinal);
            var popup = transportText.IndexOf(
                "EditorGUILayout.Popup",
                StringComparison.Ordinal);
            var guardedAssignment = transportText.IndexOf(
                "if (EditorGUI.EndChangeCheck())",
                StringComparison.Ordinal);
            Check(disabled >= 0
                  && popup > disabled
                  && guardedAssignment > popup
                  && transportText.Contains(
                      "prop.intValue",
                      StringComparison.Ordinal)
                  && !transportText.Contains(
                      "prop.enumValueIndex = selected == 1",
                      StringComparison.Ordinal),
                "180J-4: disabled and malformed transport values remain byte-stable until an explicit popup selection");
        }

        private static void VerifyNeutralSerialization()
        {
            var source =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    ManagerProvidersPath);
            var root = Parse(source);
            var publishProperty = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Single(property =>
                    property.Identifier.ValueText
                    == "ConfiguredFoxRunPublishTransportIds")
                .ToFullString();
            var subscribeProperty = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .Single(property =>
                    property.Identifier.ValueText
                    == "ConfiguredFoxRunSubscribeTransportId")
                .ToFullString();
            Check(source.Contains(
                      "private string[] _foxRunPublishTransportIds",
                      StringComparison.Ordinal)
                  && source.Contains(
                      "private string _foxRunSubscribeTransportId",
                      StringComparison.Ordinal)
                  && publishProperty.Contains(
                      "FoxRunTransportSelection.TryCreate(",
                      StringComparison.Ordinal)
                  && publishProperty.Contains(
                      "Array.Empty<FoxRunTransportId>()",
                      StringComparison.Ordinal)
                  && subscribeProperty.Contains(
                      "FoxRunTransportId.TryCreate(",
                      StringComparison.Ordinal)
                  && subscribeProperty.Contains(
                      ": default;",
                      StringComparison.Ordinal)
                  && !publishProperty.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && !subscribeProperty.Contains(
                      "? FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && source.Contains(
                      "TryCreateCapturedTransportSelection",
                      StringComparison.Ordinal)
                  && source.Contains(
                      "Configured transport selection is invalid:",
                      StringComparison.Ordinal)
                  && !source.Contains(
                      "FormerlySerializedAs",
                      StringComparison.Ordinal)
                  && !source.Contains(
                      "_ros2Native",
                      StringComparison.Ordinal)
                  && !source.Contains(
                      "_ros2Bridge",
                      StringComparison.Ordinal),
                "180G-2: neutral publish/source IDs serialize directly and blank or unknown Source never falls back to WebSocket");
        }

        private static void VerifyDirectionalCoordinateRuntimePolicy()
        {
            var managerRuntime =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    ManagerRuntimePath);
            var managerCoordinateMigration =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    ManagerCoordinateMigrationPath);
            var mcapRecorder =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    McapRecorderPath);
            var sessionClientPublishHandler =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(
                    SessionClientPublishHandlerPath);
            var managerRoot = Parse(managerRuntime);
            var fields = managerRoot.DescendantNodes()
                .OfType<FieldDeclarationSyntax>()
                .SelectMany(field =>
                    field.Declaration.Variables)
                .Select(variable =>
                    variable.Identifier.ValueText)
                .ToArray();
            var outputPosition =
                FindMethod(managerRuntime, "UnityToFoxglovePosition");
            var outputRotation =
                FindMethod(managerRuntime, "UnityToFoxgloveRotation");
            var inputPosition =
                FindMethod(managerRuntime, "FoxgloveToUnityPosition");
            var inputRotation =
                FindMethod(managerRuntime, "FoxgloveToUnityRotation");
            var recorderWrite =
                sessionClientPublishHandler.IndexOf(
                    "recorder?.WriteClientMessage",
                    StringComparison.Ordinal);
            var callbackWrite =
                sessionClientPublishHandler.IndexOf(
                    "_messageCallback",
                    recorderWrite < 0 ? 0 : recorderWrite,
                    StringComparison.Ordinal);

            Check(fields.Count(field => field == "_coordinateMode") == 1
                  && fields.Count(
                      field => field == "_outputCoordinateMode") == 1
                  && fields.Count(
                      field => field == "_inputCoordinateMode") == 1
                  && References(
                      outputPosition,
                      "ActiveOutputCoordinateMode")
                  && References(
                      outputRotation,
                      "ActiveOutputCoordinateMode")
                  && References(
                      inputPosition,
                      "ActiveInputCoordinateMode")
                  && References(
                      inputRotation,
                      "ActiveInputCoordinateMode")
                  && managerCoordinateMigration.Contains(
                      "CoordinateTransportPolicy.Migrate",
                      StringComparison.Ordinal)
                  && mcapRecorder.Contains(
                      "DataDirectionMetadataKey",
                      StringComparison.Ordinal)
                  && mcapRecorder.Contains(
                      "McapChannelDirection.Output",
                      StringComparison.Ordinal)
                  && mcapRecorder.Contains(
                      "McapChannelDirection.Input",
                      StringComparison.Ordinal)
                  && recorderWrite >= 0
                  && callbackWrite > recorderWrite,
                "180H-1: neutral transport UI preserves directional coordinate migration, MCAP metadata, and raw inbound recording order");
        }

        private static void VerifyValidationRegistryEntry()
        {
            var entries = PhaseValidationRegistry.All
                .Where(item =>
                    string.Equals(
                        item.Flag,
                        "--phase180",
                        StringComparison.Ordinal))
                .ToArray();
            var defaults =
                PhaseValidationRegistry.DefaultValidations(
                        includeLocalEvidence: false)
                    .Where(item =>
                        string.Equals(
                            item.Flag,
                            "--phase180",
                            StringComparison.Ordinal))
                    .ToArray();

            Check(entries.Length == 1
                  && defaults.Length == 1
                  && ReferenceEquals(entries[0], defaults[0])
                  && entries[0].Run
                  == (Action)Validate
                  && entries[0].Category
                  == ValidationCategory.CiSafe
                  && entries[0].Evidence
                  == (ValidationEvidence.Behavior
                      | ValidationEvidence.Structural),
                "180I-1: Phase 180 remains one default CI-safe Behavior | Structural gate");
        }

        private static string ExpandGuardContext(
            MethodDeclarationSyntax method,
            ExpressionSyntax condition)
        {
            var declarations = method.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable =>
                    variable.Initializer != null)
                .GroupBy(variable =>
                    variable.Identifier.ValueText,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
            var pending = new Queue<string>(
                condition.DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
                    .Select(identifier =>
                        identifier.Identifier.ValueText));
            var visited =
                new HashSet<string>(StringComparer.Ordinal);
            var fragments =
                new List<string>
                {
                    condition.ToFullString()
                };

            while (pending.Count > 0)
            {
                var name = pending.Dequeue();
                if (!visited.Add(name)
                    || !declarations.TryGetValue(
                        name,
                        out var declaration))
                {
                    continue;
                }

                var initializer =
                    declaration.Initializer.Value;
                fragments.Add(initializer.ToFullString());
                foreach (var identifier in
                         initializer.DescendantNodesAndSelf()
                             .OfType<IdentifierNameSyntax>())
                {
                    pending.Enqueue(
                        identifier.Identifier.ValueText);
                }
            }

            return string.Join("\n", fragments);
        }

        private static SyntaxNode Parse(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var errors = tree.GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.Severity
                    == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    "Source contains syntax errors: "
                    + errors[0]);
            }

            return tree.GetRoot();
        }

        private static MethodDeclarationSyntax FindMethod(
            string source,
            string methodName)
        {
            var methods = Parse(source)
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    method.Identifier.ValueText == methodName)
                .ToArray();
            if (methods.Length != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one method named "
                    + methodName
                    + ", found "
                    + methods.Length
                    + ".");
            }

            return methods[0];
        }

        private static IEnumerable<InvocationExpressionSyntax>
            InvocationsNamed(
                SyntaxNode node,
                string methodName)
            => node.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    IsInvocationNamed(
                        invocation,
                        methodName));

        private static bool IsInvocationNamed(
            InvocationExpressionSyntax invocation,
            string methodName)
        {
            if (invocation.Expression
                is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.ValueText
                       == methodName;
            }

            return invocation.Expression
                       is MemberAccessExpressionSyntax access
                   && access.Name.Identifier.ValueText
                   == methodName;
        }

        private static string StringArgument(
            InvocationExpressionSyntax invocation,
            int index)
        {
            if (invocation.ArgumentList.Arguments.Count <= index
                || invocation.ArgumentList.Arguments[index]
                       .Expression
                   is not LiteralExpressionSyntax literal
                || !literal.IsKind(
                    SyntaxKind.StringLiteralExpression))
            {
                return string.Empty;
            }

            return literal.Token.ValueText;
        }

        private static ExpressionSyntax UnwrapParentheses(
            ExpressionSyntax expression)
        {
            while (expression
                   is ParenthesizedExpressionSyntax parentheses)
            {
                expression = parentheses.Expression;
            }

            return expression;
        }

        private static bool HasStringArgument(
            InvocationExpressionSyntax invocation,
            string expected)
            => invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<LiteralExpressionSyntax>()
                .Any(literal =>
                    literal.IsKind(
                        SyntaxKind.StringLiteralExpression)
                    && string.Equals(
                        literal.Token.ValueText,
                        expected,
                        StringComparison.Ordinal));

        private static bool HasIdentifierArgument(
            InvocationExpressionSyntax invocation,
            string expected)
            => invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<IdentifierNameSyntax>()
                .Any(identifier =>
                    identifier.Identifier.ValueText
                    == expected);

        private static bool References(
            MethodDeclarationSyntax method,
            string identifier)
            => method.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(name =>
                    name.Identifier.ValueText
                    == identifier);

        private static int Count(
            string source,
            string token)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(
                       token,
                       index,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static void Check(
            bool condition,
            string name)
        {
            if (!condition)
                throw new InvalidOperationException(
                    "[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
