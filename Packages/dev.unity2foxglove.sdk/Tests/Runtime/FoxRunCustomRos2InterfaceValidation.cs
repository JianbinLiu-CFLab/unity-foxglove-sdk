// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Phase181 custom ROS2 DTO interface boundary.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Locks the ROS-free custom DTO model, the selected typesupport boundary,
    /// and the closed-generic Phase181 native transport seams.
    /// </summary>
    internal static partial class FoxRunCustomRos2InterfaceValidation
    {
        private const string CustomShapePath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2CustomDtoShape.cs";
        private const string CustomIdentityPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2CustomIdentity.cs";
        private const string CustomNamingPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2CustomNamingPolicy.cs";
        private const string ReflectionBuilderPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunReflectionRos2CustomDtoShapeBuilder.cs";
        private const string RoslynBuilderPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunRoslynRos2CustomDtoShapeBuilder.cs";
        private const string ValidatorPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/Shared/FoxRunGenerationModelValidator.cs";
        private const string EmitterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs";
        private const string R2fuAnalyzerPipelinePath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunR2fuAnalyzerPipeline.cs";
        private const string ManifestBuilderPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestBuilder.cs";
        private const string ManifestModelPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestModel.cs";
        private const string ManifestJsonWriterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestJsonWriter.cs";
        private const string DescriptorWriterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs";
        private const string InterfaceIdentityPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/FoxRunRos2InterfaceIdentity.cs";
        private const string InterfaceRendererPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2InterfacePackageRenderer.cs";
        private const string InterfaceWriterPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2InterfacePackageWriter.cs";
        private const string InterfacePreflightPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2InterfacePackagePreflight.cs";
        private const string InterfaceCommandPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunRos2InterfacePackageCommand.cs";
        private const string SourceGeneratorProjectPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/FoxRunR2fuSourceGenerator.csproj";
        private const string StaticInterfacePackageJsonPath =
            "Packages/dev.unity2foxglove.foxrun.ros2.interfaces/package.json";
        private const string StaticInterfaceLockPath =
            "Packages/dev.unity2foxglove.foxrun.ros2.interfaces/RuntimeSupport/foxrun-ros2-interface-lock.json";
        private const string StaticInterfaceCmakePath =
            "Packages/dev.unity2foxglove.foxrun.ros2.interfaces/Ros2Package~/CMakeLists.txt";
        private const string DiagnosticsPath =
            "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs";
        private const string UnshippedLedgerPath =
            "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md";
        private const string ShippedLedgerPath =
            "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md";
        private const string R2fuDiagnosticsPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunR2fuDiagnostics.cs";
        private const string R2fuUnshippedLedgerPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md";
        private const string TypesupportSelectionPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs";
        private const string RuntimeSelectionPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs";
        private const string DefineInstallerPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs";
        private const string PlayModeGuardPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs";
        private const string TypesupportDiscoveryPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportDiscovery.cs";
        private const string TypesupportPreflightPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityCustomTypesupportPreflight.cs";
        private const string TypesupportInspectorPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/FoxRunRos2CustomTypesupportInspector.cs";
        private const string ManagerR2fuRuntimeInspectorPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunR2fuProviderDrawer.cs";
        private const string FoxRunCodeGeneratorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs";
        private const string CustomTransportHostPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomNativeTransportHost.cs";
        private const string CustomPublisherHubPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherHub.cs";
        private const string CustomPublisherBindingPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomPublisherBinding.cs";
        private const string SubscriptionBindingPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs";
        private const string CustomOutboundPolicyPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomOutboundMappingPolicy.cs";
        private const string CustomOutboundBudgetPolicyPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2CustomOutboundBudgetPolicy.cs";
        private const string CustomMapperEmitterPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2CustomDtoMapperEmitter.cs";
        private const string CustomPublishEmitterPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2CustomPublishEmitter.cs";
        private const string FoxgloveLogHubPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs";
        private const string R2fuTransportProviderPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs";
        private const string AcceptanceSamplePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun Custom ROS2 Interface/Phase181FoxRunCustomRos2Interface.cs";
        private const string ImportedAcceptanceSamplePath =
            "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/FoxRun Custom ROS2 Interface/Phase181FoxRunCustomRos2Interface.cs";
        private const string AcceptanceSampleReadmePath =
            "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun Custom ROS2 Interface/README.md";
        private const string R2fuPackageJsonPath =
            "Packages/dev.unity2foxglove.ros2forunity/package.json";
        private const string AcceptanceComponentPath =
            "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase181FoxRunCustomRos2InterfaceAcceptance.cs";
        private const string AcceptancePlayerBuilderPath =
            "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase181CustomRos2InterfacePlayerBuilder.cs";
        private const string AcceptanceBatchProbePath =
            "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase181BatchModeCustomRos2InteropProbe.cs";
        private const string TypesupportPluginImporterBuilderPath =
            "Unity2Foxglove/Assets/Editor/Phase181TypesupportPluginImporterBuilder.cs";
        private const string RuntimeBatchSelectionPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Phase181Ros2RuntimeBatchSelection.cs";
        private const string PeerProtocolPath =
            "Scripts/smoke/ros2/phase181_custom_ros2_peer_protocol.py";
        private const string PeerHelperPath =
            "Scripts/smoke/ros2/phase181_custom_ros2_peer.py";
        private const string LinuxPeerPath =
            "Scripts/smoke/ros2/phase181_custom_ros2_linux_peer.py";
        private const string MatrixProfilesPath =
            "Scripts/smoke/ros2/phase181_custom_ros2_matrix_profiles.py";
        private const string RunCiPath =
            "Scripts/release/run_ci.py";
        private const string DotnetWorkflowPath =
            ".github/workflows/dotnet-tests.yml";
        private const string PackageWorkflowPath =
            ".github/workflows/package-check.yml";
        private const string Ros2SmokeReadmePath =
            "Scripts/smoke/ros2/README.md";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Custom ROS2 Interface Boundary ---");
            _passed = 0;

            var customShape = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomShapePath);
            var customIdentity = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomIdentityPath);
            var customNaming = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomNamingPath);
            var reflectionBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(ReflectionBuilderPath);
            var roslynBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(RoslynBuilderPath);
            var validator = PhaseValidationSourceHelpers.ReadRequiredRepoText(ValidatorPath);
            var emitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(EmitterPath);
            var r2fuAnalyzerPipeline =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(R2fuAnalyzerPipelinePath);
            var manifestBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManifestBuilderPath);
            var manifestModel = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManifestModelPath);
            var manifestJsonWriter = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManifestJsonWriterPath);
            var descriptorWriter = PhaseValidationSourceHelpers.ReadRequiredRepoText(DescriptorWriterPath);
            var interfaceIdentity = PhaseValidationSourceHelpers.ReadRequiredRepoText(InterfaceIdentityPath);
            var interfaceRenderer = PhaseValidationSourceHelpers.ReadRequiredRepoText(InterfaceRendererPath);
            var interfaceWriter = PhaseValidationSourceHelpers.ReadRequiredRepoText(InterfaceWriterPath);
            var interfacePreflight = PhaseValidationSourceHelpers.ReadRequiredRepoText(InterfacePreflightPath);
            var interfaceCommand = PhaseValidationSourceHelpers.ReadRequiredRepoText(InterfaceCommandPath);
            var sourceGeneratorProject = PhaseValidationSourceHelpers.ReadRequiredRepoText(SourceGeneratorProjectPath);
            var staticPackageJson = PhaseValidationSourceHelpers.ReadRequiredRepoText(StaticInterfacePackageJsonPath);
            var staticLock = PhaseValidationSourceHelpers.ReadRequiredRepoText(StaticInterfaceLockPath);
            var staticCmake = PhaseValidationSourceHelpers.ReadRequiredRepoText(StaticInterfaceCmakePath);
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(DiagnosticsPath);
            var unshippedLedger = PhaseValidationSourceHelpers.ReadRequiredRepoText(UnshippedLedgerPath);
            var shippedLedger = PhaseValidationSourceHelpers.ReadRequiredRepoText(ShippedLedgerPath);
            var r2fuDiagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(R2fuDiagnosticsPath);
            var r2fuUnshippedLedger = PhaseValidationSourceHelpers.ReadRequiredRepoText(R2fuUnshippedLedgerPath);
            var typesupportSelection = PhaseValidationSourceHelpers.ReadRos2ForUnityCustomTypesupportSelectionTransactionSources();
            var runtimeSelection = PhaseValidationSourceHelpers.ReadRequiredRepoText(RuntimeSelectionPath);
            var defineInstaller = PhaseValidationSourceHelpers.ReadRequiredRepoText(DefineInstallerPath);
            var playModeGuard = PhaseValidationSourceHelpers.ReadRequiredRepoText(PlayModeGuardPath);
            var typesupportDiscovery = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportDiscoveryPath);
            var typesupportPreflight = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportPreflightPath);
            var typesupportInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportInspectorPath);
            var managerR2fuRuntimeInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManagerR2fuRuntimeInspectorPath);
            var foxRunCodeGenerator = PhaseValidationSourceHelpers.ReadRequiredRepoText(FoxRunCodeGeneratorPath);
            var customTransportHost = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomTransportHostPath);
            var customPublisherHub = PhaseValidationSourceHelpers.ReadFoxRunRos2CustomPublisherHubSources();
            var customPublisherBinding = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomPublisherBindingPath);
            var subscriptionBinding = PhaseValidationSourceHelpers.ReadFoxRunRos2SubscriptionBindingSources();
            var customOutboundPolicy = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomOutboundPolicyPath);
            var customOutboundBudgetPolicy =
                PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomOutboundBudgetPolicyPath);
            var customMapperEmitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomMapperEmitterPath);
            var customPublishEmitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomPublishEmitterPath);
            var foxgloveLogHub = PhaseValidationSourceHelpers.ReadRequiredRepoText(FoxgloveLogHubPath);
            var r2fuTransportProvider = PhaseValidationSourceHelpers.ReadRequiredRepoText(R2fuTransportProviderPath);
            var acceptanceSample = PhaseValidationSourceHelpers.ReadRequiredRepoText(AcceptanceSamplePath);
            var importedAcceptanceSample = PhaseValidationSourceHelpers.ReadRequiredRepoText(ImportedAcceptanceSamplePath);
            var acceptanceSampleReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(AcceptanceSampleReadmePath);
            var r2fuPackageJson = PhaseValidationSourceHelpers.ReadRequiredRepoText(R2fuPackageJsonPath);
            var acceptanceComponent = PhaseValidationSourceHelpers.ReadRequiredRepoText(AcceptanceComponentPath);
            var acceptancePlayerBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(AcceptancePlayerBuilderPath);
            var acceptanceBatchProbe = PhaseValidationSourceHelpers.ReadRequiredRepoText(AcceptanceBatchProbePath);
            var typesupportPluginImporterBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportPluginImporterBuilderPath);
            var runtimeBatchSelection = PhaseValidationSourceHelpers.ReadRequiredRepoText(RuntimeBatchSelectionPath);
            var peerProtocol = PhaseValidationSourceHelpers.ReadRequiredRepoText(PeerProtocolPath);
            var peerHelper = ReadSplitPythonSource(PeerHelperPath);
            var linuxPeer = PhaseValidationSourceHelpers.ReadRequiredRepoText(LinuxPeerPath);
            var matrixProfiles = PhaseValidationSourceHelpers.ReadRequiredRepoText(MatrixProfilesPath);
            var runCi = PhaseValidationSourceHelpers.ReadRequiredRepoText(RunCiPath);
            var dotnetWorkflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(DotnetWorkflowPath);
            var packageWorkflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(PackageWorkflowPath);
            var ros2SmokeReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(Ros2SmokeReadmePath);

            VerifyDistinctRosFreeDtoModel(customShape, reflectionBuilder, roslynBuilder);
            VerifyStableIdentityAndNaming(customIdentity, customNaming);
            VerifyDirectionalProviderPolicy();
            VerifyNoImplicitWebSocketInputFallback(
                validator,
                emitter,
                r2fuAnalyzerPipeline,
                manifestBuilder);
            VerifyDiagnosticRanges(
                diagnostics,
                unshippedLedger,
                shippedLedger,
                r2fuDiagnostics,
                r2fuUnshippedLedger);
            VerifyStaticSourcePackageBoundary(
                interfaceIdentity,
                interfaceRenderer,
                interfaceWriter,
                interfacePreflight,
                interfaceCommand,
                sourceGeneratorProject,
                staticPackageJson,
                staticLock,
                staticCmake,
                manifestBuilder,
                manifestModel,
                manifestJsonWriter,
                descriptorWriter);
            VerifyTypesupportAddOnActivation(
                typesupportSelection,
                runtimeSelection,
                defineInstaller,
                playModeGuard);
            VerifyTypesupportPreflightPresentation(
                typesupportDiscovery,
                typesupportPreflight,
                typesupportInspector,
                managerR2fuRuntimeInspector,
                interfaceCommand);
            VerifyEditModeCustomContractSnapshot(
                foxRunCodeGenerator,
                managerR2fuRuntimeInspector);
            VerifyTypedNativeTransport(
                customTransportHost,
                customPublisherHub,
                customPublisherBinding,
                subscriptionBinding,
                customOutboundPolicy,
                customOutboundBudgetPolicy,
                customMapperEmitter,
                customPublishEmitter,
                foxgloveLogHub,
                r2fuTransportProvider);
            VerifyAcceptanceSurface(
                acceptanceSample,
                importedAcceptanceSample,
                acceptanceSampleReadme,
                r2fuPackageJson,
                acceptanceComponent,
                acceptancePlayerBuilder,
                acceptanceBatchProbe,
                typesupportPluginImporterBuilder);
            VerifyRuntimeBatchSelection(runtimeBatchSelection);
            VerifyInteropAutomationReleaseGate(
                peerProtocol,
                peerHelper,
                linuxPeer,
                matrixProfiles,
                runCi,
                dotnetWorkflow,
                packageWorkflow);
            VerifyPublicOperationalDocumentation(
                acceptanceSampleReadme,
                ros2SmokeReadme,
                r2fuPackageJson);
            VerifyRegistryEntry();

            Console.WriteLine("FoxRun custom ROS2 interface boundary: " + _passed + " checks passed.\n");
        }

        private static void VerifyDistinctRosFreeDtoModel(
            string customShape,
            string reflectionBuilder,
            string roslynBuilder)
        {
            Check(customShape.Contains("PackagedRos2Message = 1", StringComparison.Ordinal)
                  && customShape.Contains("CustomDto = 2", StringComparison.Ordinal)
                  && customShape.Contains("CanonicalIdentity", StringComparison.Ordinal)
                  && customShape.Contains("PayloadIdentity", StringComparison.Ordinal)
                  && customShape.Contains("PresenceFieldName", StringComparison.Ordinal)
                  && !customShape.Contains("public readonly bool ImplementsRos2Message", StringComparison.Ordinal),
                "181A-1: custom DTO and packaged ROS2 message are explicit, separate contract kinds");

            var customSources = string.Concat(customShape, reflectionBuilder, roslynBuilder);
            Check(!customSources.Contains("using ROS2", StringComparison.Ordinal)
                  && !customSources.Contains("ROS2.", StringComparison.Ordinal)
                  && customShape.Contains("FOXR2F009", StringComparison.Ordinal)
                  && customShape.Contains("FOXR2F010", StringComparison.Ordinal)
                  && customShape.Contains("FOXR2F011", StringComparison.Ordinal),
                "181A-2: R2FU owns custom DTO schema builders without a ROS runtime type dependency");

            Check(reflectionBuilder.Contains("OrderBy(member => member.Name", StringComparison.Ordinal)
                  && roslynBuilder.Contains("OrderBy(member => member.Name", StringComparison.Ordinal)
                  && reflectionBuilder.Contains("FoxRunRos2CustomDtoShape", StringComparison.Ordinal)
                  && roslynBuilder.Contains("FoxRunRos2CustomDtoShape", StringComparison.Ordinal),
                "181A-3: reflection and Roslyn builders share deterministic custom DTO ordering and shape output");
        }

        private static void VerifyStableIdentityAndNaming(string customIdentity, string customNaming)
        {
            Check(customIdentity.Contains("AppendLengthFramed", StringComparison.Ordinal)
                  && customIdentity.Contains("ToUpperInvariant", StringComparison.Ordinal)
                  && customIdentity.Contains("Fnv1a64Hex", StringComparison.Ordinal)
                  && customIdentity.Contains("Substring(0, 12)", StringComparison.Ordinal),
                "181A-4: custom DTO canonical and payload identities are deterministic and case-insensitive");

            Check(customNaming.Contains("FrameworkPrefix = \"foxrun_\"", StringComparison.Ordinal)
                  && customNaming.Contains("PresenceFieldName", StringComparison.Ordinal)
                  && customNaming.Contains("FrameworkPrefix + \"has_\"", StringComparison.Ordinal)
                  && customNaming.Contains("IsReservedUserField", StringComparison.Ordinal),
                "181A-5: generated presence fields use the reserved foxrun_has_ namespace");
        }

        private static void VerifyDirectionalProviderPolicy()
        {
            var customBidirectional = FoxRunRos2RouteResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                FoxRunRos2RouteEndpoint.R2fu,
                hasExplicitSource: true,
                declaredTargets: FoxRunRos2RouteEndpoint.WebSocket,
                hasExplicitTargets: true,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.WebSocket,
                hasExplicitWebSocketEncoding: true);
            var inheritedCustomBidirectional = FoxRunRos2RouteResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: 0,
                hasExplicitTargets: false,
                defaultSource: FoxRunRos2RouteEndpoint.R2fu,
                defaultTargets: FoxRunRos2RouteEndpoint.R2fu);
            var nativeSubscribeJson = FoxRunRos2RouteResolver.Resolve(
                FoxRunFlow.Subscribe,
                FoxRunRos2RouteEndpoint.R2fu,
                hasExplicitSource: true,
                declaredTargets: 0,
                hasExplicitTargets: false,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.WebSocket,
                hasExplicitWebSocketEncoding: true);
            var publishWithSource = FoxRunRos2RouteResolver.Resolve(
                FoxRunFlow.Publish,
                FoxRunRos2RouteEndpoint.R2fu,
                hasExplicitSource: true,
                declaredTargets: FoxRunRos2RouteEndpoint.R2fu,
                hasExplicitTargets: true,
                defaultSource: FoxRunRos2RouteEndpoint.WebSocket,
                defaultTargets: FoxRunRos2RouteEndpoint.WebSocket);

            Check(customBidirectional.Success
                  && customBidirectional.Route.Source == FoxRunRos2RouteEndpoint.R2fu
                  && customBidirectional.Route.Targets == FoxRunRos2RouteEndpoint.WebSocket
                  && inheritedCustomBidirectional.Success
                  && inheritedCustomBidirectional.Route.Source == FoxRunRos2RouteEndpoint.R2fu
                  && inheritedCustomBidirectional.Route.Targets == FoxRunRos2RouteEndpoint.R2fu
                  && nativeSubscribeJson.DiagnosticCode
                     == FoxRunRos2RouteDiagnosticCode.EncodingRequiresWebSocket
                  && publishWithSource.DiagnosticCode
                     == FoxRunRos2RouteDiagnosticCode.SourceNotAllowed,
                "181A-6: R2FU projects neutral Provider IDs into directional routes and rejects contradictory WebSocket encoding or source declarations");
        }
    }
}
