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
    public static partial class DataTransportInspectorValidation
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
    }
}
