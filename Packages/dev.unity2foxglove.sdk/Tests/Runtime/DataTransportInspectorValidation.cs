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
    /// <remarks>Phase192 source sections: Contracts.cs, SourceParsing.cs, Workflow.cs</remarks>
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
