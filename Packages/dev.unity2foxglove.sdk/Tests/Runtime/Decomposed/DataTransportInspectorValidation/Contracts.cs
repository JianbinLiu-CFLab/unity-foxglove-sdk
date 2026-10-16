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
    public static partial class DataTransportInspectorValidation
    {
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
            var directGuard = managerEditor.Contains("private bool ShouldEnsureProvider(", StringComparison.Ordinal)
                && managerEditor.Contains("serializedObject.isEditingMultipleObjects", StringComparison.Ordinal)
                && managerEditor.Contains("publishTransportIds != null", StringComparison.Ordinal)
                && managerEditor.Contains("subscribeTransportId != null", StringComparison.Ordinal)
                && Count(managerEditor, "hasMultipleDifferentValues") >= 2
                && managerEditor.Contains("return publishSelected || subscribeSelected;", StringComparison.Ordinal);

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
                  && multiObjectFalseGate
                  || directGuard,
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
                PhaseValidationSourceHelpers.ReadMcapRecorderSources();
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
                    "WriteClientMessageSafely(recorder",
                    StringComparison.Ordinal);
            var callbackWrite =
                sessionClientPublishHandler.LastIndexOf(
                    "_messageCallback",
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
    }
}
