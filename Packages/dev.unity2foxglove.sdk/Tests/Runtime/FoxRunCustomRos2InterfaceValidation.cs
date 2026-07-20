// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Phase181 custom ROS2 DTO interface boundary.

using System;
using System.Linq;
using System.Text.Json;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Locks the ROS-free custom DTO model, the selected typesupport boundary,
    /// and the closed-generic Phase181 native transport seams.
    /// </summary>
    internal static class FoxRunCustomRos2InterfaceValidation
    {
        private const string CustomShapePath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunRos2CustomDtoShape.cs";
        private const string CustomIdentityPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunRos2CustomIdentity.cs";
        private const string CustomNamingPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunRos2CustomNamingPolicy.cs";
        private const string ReflectionBuilderPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunReflectionRos2CustomDtoShapeBuilder.cs";
        private const string RoslynBuilderPath =
            "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunRoslynRos2CustomDtoShapeBuilder.cs";
        private const string ValidatorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs";
        private const string EmitterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs";
        private const string ManifestBuilderPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestBuilder.cs";
        private const string ManifestModelPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestModel.cs";
        private const string ManifestJsonWriterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestJsonWriter.cs";
        private const string DescriptorWriterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorJsonWriter.cs";
        private const string InterfaceIdentityPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunRos2InterfaceIdentity.cs";
        private const string InterfaceRendererPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunRos2InterfacePackageRenderer.cs";
        private const string InterfaceWriterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunRos2InterfacePackageWriter.cs";
        private const string InterfacePreflightPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunRos2InterfacePackagePreflight.cs";
        private const string InterfaceCommandPath =
            "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunRos2InterfacePackageCommand.cs";
        private const string SourceGeneratorProjectPath =
            "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj";
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
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.R2fuRuntime.cs";
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
        private const string CustomMapperEmitterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/Ros2CustomDtoMapperEmitter.cs";
        private const string CustomPublishEmitterPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/Ros2CustomPublishEmitter.cs";
        private const string FoxgloveLogHubPath =
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs";
        private const string LegacyR2fuTopicSinkPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2R2FUTopicSink.cs";
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
            var typesupportSelection = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportSelectionPath);
            var runtimeSelection = PhaseValidationSourceHelpers.ReadRequiredRepoText(RuntimeSelectionPath);
            var defineInstaller = PhaseValidationSourceHelpers.ReadRequiredRepoText(DefineInstallerPath);
            var playModeGuard = PhaseValidationSourceHelpers.ReadRequiredRepoText(PlayModeGuardPath);
            var typesupportDiscovery = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportDiscoveryPath);
            var typesupportPreflight = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportPreflightPath);
            var typesupportInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(TypesupportInspectorPath);
            var managerR2fuRuntimeInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManagerR2fuRuntimeInspectorPath);
            var foxRunCodeGenerator = PhaseValidationSourceHelpers.ReadRequiredRepoText(FoxRunCodeGeneratorPath);
            var customTransportHost = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomTransportHostPath);
            var customPublisherHub = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomPublisherHubPath);
            var customPublisherBinding = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomPublisherBindingPath);
            var subscriptionBinding = PhaseValidationSourceHelpers.ReadRequiredRepoText(SubscriptionBindingPath);
            var customOutboundPolicy = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomOutboundPolicyPath);
            var customMapperEmitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomMapperEmitterPath);
            var customPublishEmitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(CustomPublishEmitterPath);
            var foxgloveLogHub = PhaseValidationSourceHelpers.ReadRequiredRepoText(FoxgloveLogHubPath);
            var legacyR2fuTopicSink = PhaseValidationSourceHelpers.ReadRequiredRepoText(LegacyR2fuTopicSinkPath);
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
            var peerHelper = PhaseValidationSourceHelpers.ReadRequiredRepoText(PeerHelperPath);
            var linuxPeer = PhaseValidationSourceHelpers.ReadRequiredRepoText(LinuxPeerPath);
            var matrixProfiles = PhaseValidationSourceHelpers.ReadRequiredRepoText(MatrixProfilesPath);
            var runCi = PhaseValidationSourceHelpers.ReadRequiredRepoText(RunCiPath);
            var dotnetWorkflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(DotnetWorkflowPath);
            var packageWorkflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(PackageWorkflowPath);
            var ros2SmokeReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(Ros2SmokeReadmePath);

            VerifyDistinctRosFreeDtoModel(customShape, reflectionBuilder, roslynBuilder);
            VerifyStableIdentityAndNaming(customIdentity, customNaming);
            VerifyDirectionalProviderPolicy();
            VerifyNoImplicitWebSocketInputFallback(validator, emitter, manifestBuilder);
            VerifyDiagnosticRanges(diagnostics, unshippedLedger, shippedLedger);
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
                customMapperEmitter,
                customPublishEmitter,
                foxgloveLogHub,
                legacyR2fuTopicSink);
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
                  && !customSources.Contains("Ros2ForUnity", StringComparison.Ordinal)
                  && !customSources.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal),
                "181A-2: custom DTO schema builders remain ROS-free core code");

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
            var customBidirectional = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunMode.PublishAndSubscribe,
                FoxRunWireEncoding.Json,
                supportsWebSocket: true,
                supportsRos2Native: true,
                allowsNativePublishAndSubscribe: true);
            var inheritedCustomBidirectional = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunMode.PublishAndSubscribe,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true,
                allowsNativePublishAndSubscribe: true);
            var packagedBidirectional = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunMode.PublishAndSubscribe,
                FoxRunWireEncoding.Json,
                supportsWebSocket: true,
                supportsRos2Native: true,
                allowsNativePublishAndSubscribe: false);
            var nativeSubscribeJson = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunMode.SubscribeOnly,
                FoxRunWireEncoding.Json,
                supportsWebSocket: true,
                supportsRos2Native: true);
            var nativePublishOnly = FoxRunSubscriptionProviderResolver.Resolve(
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunSubscriptionProvider.Ros2Native,
                FoxRunMode.PublishOnly,
                FoxRunWireEncoding.Inherit,
                supportsWebSocket: true,
                supportsRos2Native: true);

            Check(customBidirectional.Success
                  && customBidirectional.Provider == FoxRunSubscriptionProvider.Ros2Native
                  && inheritedCustomBidirectional.Success
                  && inheritedCustomBidirectional.Provider == FoxRunSubscriptionProvider.Ros2Native
                  && packagedBidirectional.DiagnosticCode == FoxRunSubscriptionProviderDiagnosticCode.NativeEncodingConflict
                  && nativeSubscribeJson.DiagnosticCode == FoxRunSubscriptionProviderDiagnosticCode.NativeEncodingConflict
                  && nativePublishOnly.DiagnosticCode == FoxRunSubscriptionProviderDiagnosticCode.NativeRequiresSubscribeOnly,
                "181A-6: JSON/Protobuf is allowed only as custom P&S WebSocket output; native remains the input provider");
        }

        private static void VerifyNoImplicitWebSocketInputFallback(
            string validator,
            string emitter,
            string manifestBuilder)
        {
            Check(validator.Contains("CustomNativeBidirectionalContractDiagnosticId", StringComparison.Ordinal)
                  && validator.Contains("never falls back to WebSocket input", StringComparison.Ordinal)
                  && validator.Contains("!IsNativeCustomBidirectionalOutputContract(member)", StringComparison.Ordinal)
                  && validator.Contains("NativeSubscribeOnlyDiagnosticId", StringComparison.Ordinal),
                "181A-7: validator preserves legacy packaged native rejection and gives custom P&S a no-fallback diagnostic path");

            Check(emitter.Contains("webSocketInputMembers", StringComparison.Ordinal)
                  && emitter.Contains("!string.Equals(", StringComparison.Ordinal)
                  && emitter.Contains("Ros2NativeSubscriptionProvider", StringComparison.Ordinal)
                  && manifestBuilder.Contains("SubscribeOnly native contracts remain absent", StringComparison.Ordinal)
                  && manifestBuilder.Contains("ResolvePackagedCanonicalRosType", StringComparison.Ordinal)
                  && manifestBuilder.Contains("ResolvePackagedCopyShapeIdentity", StringComparison.Ordinal)
                  && manifestBuilder.Contains("member.FlowMode == 2", StringComparison.Ordinal),
                "181A-8: generated WebSocket input excludes native contracts while custom P&S retains only output metadata");
        }

        private static void VerifyDiagnosticRanges(
            string diagnostics,
            string unshippedLedger,
            string shippedLedger)
        {
            var expectedIds = new[] { "FOXRUN214", "FOXRUN402", "FOXRUN606", "FOXRUN607", "FOXRUN608" };
            Check(expectedIds.All(id => diagnostics.Contains("\"" + id + "\"", StringComparison.Ordinal))
                  && expectedIds.All(id => unshippedLedger.Contains(id + " | FoxRun |", StringComparison.Ordinal))
                  && !diagnostics.Contains("\"FOXRUN036\"", StringComparison.Ordinal)
                  && shippedLedger.Contains("FOXRUN036 | FoxRun |", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN036 | FoxRun | Error | Retired;", StringComparison.Ordinal),
                "181A-9: new diagnostics use subscribe, bidirectional, and system ranges while retired FOXRUN036 remains reserved");
        }

        private static void VerifyStaticSourcePackageBoundary(
            string interfaceIdentity,
            string interfaceRenderer,
            string interfaceWriter,
            string interfacePreflight,
            string interfaceCommand,
            string sourceGeneratorProject,
            string staticPackageJson,
            string staticLock,
            string staticCmake,
            string manifestBuilder,
            string manifestModel,
            string manifestJsonWriter,
            string descriptorWriter)
        {
            Check(interfaceIdentity.Contains("UnityPackageId = \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && interfaceIdentity.Contains("BuildRosPackageName(string currentPackageName", StringComparison.Ordinal)
                  && interfaceIdentity.Contains("TryParseRosPackageRevision", StringComparison.Ordinal)
                  && !interfaceIdentity.Contains("ROS2.", StringComparison.Ordinal),
                "181B-1: one ROS-free static interface identity freezes a chosen package stem across explicit revisions");

            Check(interfaceRenderer.Contains("rosidl_generate_interfaces", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("ament_cmake", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("rosidl_default_generators", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("builtin_interfaces/Time foxrun_stamp", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("foxrun_origin_id", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("foxrun_sequence", StringComparison.Ordinal)
                  && !interfaceRenderer.Contains("Typesupport", StringComparison.Ordinal),
                "181B-2: renderer emits deterministic source-only ROS interfaces with envelope origin sequence and stamp fields");

            Check(interfaceWriter.Contains("build", StringComparison.Ordinal)
                  && interfaceWriter.Contains("phase181", StringComparison.Ordinal)
                  && interfaceWriter.Contains("interface-generation", StringComparison.Ordinal)
                  && interfaceWriter.Contains("File.Replace", StringComparison.Ordinal)
                  && interfaceWriter.Contains("RestoreBackup", StringComparison.Ordinal)
                  && interfaceWriter.Contains("RevisionRequired", StringComparison.Ordinal)
                  && !interfaceWriter.Contains("ROS2.", StringComparison.Ordinal),
                "181B-3: writer stages outside Packages and preserves the prior source package on cancellation or failure");

            Check(interfacePreflight.Contains("NotRequired", StringComparison.Ordinal)
                  && interfacePreflight.Contains("ReadyForBuild", StringComparison.Ordinal)
                  && interfacePreflight.Contains("MissingSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("StaleSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("InvalidSource", StringComparison.Ordinal)
                  && interfacePreflight.Contains("RevisionRequired", StringComparison.Ordinal)
                  && interfacePreflight.Contains("intentionally does not share", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("using ROS2", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("Ros2ForUnity", StringComparison.Ordinal),
                "181B-4: source preflight has independent typed states and performs no RMW or native loading");

            Check(interfaceCommand.Contains("--check", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--generate", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--next-revision", StringComparison.Ordinal)
                  && interfaceCommand.Contains("ExecuteFromCommandLine", StringComparison.Ordinal)
                  && interfaceCommand.Contains("CollectReflectionGenerationModelForRos2InterfacePackage", StringComparison.Ordinal)
                  && interfaceCommand.Contains("EditorApplication.Exit", StringComparison.Ordinal),
                "181B-5: explicit Editor and batch command never lets Play Mode or source generation mutate the package");

            var staticRosPackageName = ReadRequiredJsonString(staticLock, "rosPackageName");
            Check(staticPackageJson.Contains("\"name\": \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && staticLock.Contains("\"lockSchemaVersion\":1", StringComparison.Ordinal)
                  && staticLock.Contains("\"interfaceDigest\":\"", StringComparison.Ordinal)
                  && staticCmake.Contains("project(" + staticRosPackageName + ")", StringComparison.Ordinal)
                  && staticCmake.Contains("rosidl_generate_interfaces", StringComparison.Ordinal),
                "181B-6: tracked static UPM package CMake identity follows the lock-selected portable ROS package revision");

            Check(manifestBuilder.Contains("ResolveCustomEnvelopeIdentity", StringComparison.Ordinal)
                  && manifestModel.Contains("CustomEnvelopeIdentity", StringComparison.Ordinal)
                  && manifestJsonWriter.Contains("customEnvelopeIdentity", StringComparison.Ordinal)
                  && descriptorWriter.Contains("ros2CustomEnvelopeMessageName", StringComparison.Ordinal),
                "181B-7: manifest and descriptor evidence carry custom DTO capability plus the expected envelope identity");

            Check(sourceGeneratorProject.Contains("FoxRunRos2InterfaceDigest.cs", StringComparison.Ordinal)
                  && sourceGeneratorProject.Contains("FoxRunRos2InterfaceLock.cs", StringComparison.Ordinal)
                  && sourceGeneratorProject.Contains("FoxRunRos2InterfaceJsonWriter.cs", StringComparison.Ordinal)
                  && !descriptorWriter.Contains("using Unity.FoxgloveSDK.Components;", StringComparison.Ordinal),
                "181B-8: static-package editor helpers stay out of the analyzer compile surface");
        }

        private static void VerifyRegistryEntry()
        {
            var entries = PhaseValidationRegistry.All
                .Where(entry => string.Equals(entry.Flag, "--phase181", StringComparison.Ordinal))
                .ToArray();
            var handlerEntries = PhaseValidationRegistry.All
                .Where(entry => entry.Run == (Action)Validate)
                .ToArray();
            var defaultEntries = PhaseValidationRegistry.DefaultValidations(includeLocalEvidence: false)
                .Where(entry => string.Equals(entry.Flag, "--phase181", StringComparison.Ordinal))
                .ToArray();

            Check(entries.Length == 1
                  && handlerEntries.Length == 1
                  && ReferenceEquals(entries[0], handlerEntries[0])
                  && entries[0].Category == ValidationCategory.CiSafe
                  && entries[0].IncludeInDefault
                  && entries[0].Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Structural)
                  && defaultEntries.Length == 1
                  && string.Equals(entries[0].Name, "FoxRun custom ROS2 interface interop release gate", StringComparison.Ordinal),
                "181F-9: Phase181 updates its one registry entry to the default behavior and structural release gate");
        }

        private static void VerifyTypesupportAddOnActivation(
            string transaction,
            string runtimeSelection,
            string defineInstaller,
            string playModeGuard)
        {
            Check(transaction.Contains("EvaluateActive", StringComparison.Ordinal)
                  && transaction.Contains("WriteAtomically", StringComparison.Ordinal)
                  && transaction.Contains("File.Replace", StringComparison.Ordinal)
                  && transaction.Contains("Client.Resolve", StringComparison.Ordinal) == false
                  && transaction.Contains("property.Name == StaticInterfacePackageId", StringComparison.Ordinal)
                  // The transaction documents Unity ownership of the lock;
                  // reject an actual packages-lock.json write path instead of
                  // treating that explanatory text as a violation.
                  && transaction.Contains("packages-lock.json", StringComparison.Ordinal) == false
                  && transaction.Contains("NativePluginRelativeDirectory", StringComparison.Ordinal),
                "181C-1: add-on transaction validates manifest-owned candidates and never writes Unity lock state");

            Check(runtimeSelection.Contains("CustomTypesupportPackagePrefix", StringComparison.Ordinal)
                  && runtimeSelection.Contains("SwitchActiveCustomTypesupportPackage", StringComparison.Ordinal)
                  && runtimeSelection.Contains("BuildCleanRestartPath", StringComparison.Ordinal)
                  && runtimeSelection.Contains("GetCustomTypesupportRequiringEditorRestart", StringComparison.Ordinal)
                  && runtimeSelection.Contains("SessionCustomTypesupportIdentityKey", StringComparison.Ordinal),
                "181C-2: runtime selection treats the add-on identity and plugin path as restart-sensitive native state");

            Check(defineInstaller.Contains("CustomTypesupportCompileSymbol", StringComparison.Ordinal)
                  && defineInstaller.Contains("GetActiveCustomTypesupportSelection", StringComparison.Ordinal)
                  && defineInstaller.Contains("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.CustomTypesupportCompileSymbol)", StringComparison.Ordinal),
                "181C-3: custom compile symbol is enabled only for one validated active add-on");

            Check(playModeGuard.Contains("GetActiveCustomTypesupportSelection", StringComparison.Ordinal)
                  && playModeGuard.Contains("GetCustomTypesupportRequiringEditorRestart", StringComparison.Ordinal)
                  && playModeGuard.Contains("custom ROS2 typesupport is not ready", StringComparison.Ordinal),
                "181C-4: Play Mode rechecks custom add-on readiness before native initialization");
        }

        private static void VerifyTypedNativeTransport(
            string customTransportHost,
            string customPublisherHub,
            string customPublisherBinding,
            string subscriptionBinding,
            string customOutboundPolicy,
            string customMapperEmitter,
            string customPublishEmitter,
            string foxgloveLogHub,
            string legacyR2fuTopicSink)
        {
            Check(customTransportHost.Contains("TryAcquireSubscriptionBackend", StringComparison.Ordinal)
                  && customTransportHost.Contains("TryAcquirePublisherBackend", StringComparison.Ordinal)
                  && customTransportHost.Contains("unity2foxglove_foxrun_custom", StringComparison.Ordinal)
                  && customTransportHost.Contains("ReleaseLease", StringComparison.Ordinal),
                "181D-1: custom typed input and output retain one demand-created node lease host");

            Check(customPublisherHub.Contains("Ros2NativeOutputPolicy.Enabled", StringComparison.Ordinal)
                  && customPublisherHub.Contains("IFoxRunRos2CustomPublisherSource", StringComparison.Ordinal)
                  && customPublisherHub.Contains("TryAcquirePublisherBackend", StringComparison.Ordinal)
                  && customPublisherHub.Contains("FoxRunRos2CustomOriginRegistry.BeginPublisher", StringComparison.Ordinal)
                  && customPublisherHub.Contains("!readiness.IsReady", StringComparison.Ordinal),
                "181D-2: custom output demand is independent of subscription sessions and fails closed before endpoint creation");

            var stopStart = customPublisherBinding.IndexOf("internal void Stop()", StringComparison.Ordinal);
            var stopEnd = customPublisherBinding.IndexOf("private void OnBusEnvelope", StringComparison.Ordinal);
            var stopBody = stopStart >= 0 && stopEnd > stopStart
                ? customPublisherBinding.Substring(stopStart, stopEnd - stopStart)
                : string.Empty;
            var unsubscribe = stopBody.IndexOf("_bus.Unsubscribe", StringComparison.Ordinal);
            var detachToken = stopBody.IndexOf("Interlocked.Exchange(ref _token, null)", StringComparison.Ordinal);
            var removePublisher = stopBody.IndexOf("TryRemovePublisher(token)", StringComparison.Ordinal);
            var releaseNode = stopBody.IndexOf("_backend.ReleaseNodeOwnership", StringComparison.Ordinal);
            var removeHelper = customPublisherBinding.IndexOf(
                "private void TryRemovePublisher", StringComparison.Ordinal);
            var backendRemove = removeHelper >= 0
                ? customPublisherBinding.IndexOf("_backend.RemovePublisher(token)", removeHelper, StringComparison.Ordinal)
                : -1;
            Check(customPublisherBinding.Contains("FoxTopicBus", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("_bus.Subscribe", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("FoxRunRos2CustomSequenceSource", StringComparison.Ordinal)
                  && customPublisherBinding.Contains("FoxRunRos2CustomOutboundMappingPolicy.CreateContext", StringComparison.Ordinal)
                  && unsubscribe >= 0
                  && detachToken > unsubscribe
                  && removePublisher > detachToken
                  && releaseNode > removePublisher
                  && backendRemove > removeHelper
                  && customPublisherBinding.IndexOf("catch (Exception)", removeHelper, StringComparison.Ordinal) > backendRemove,
                "181D-3: typed publisher unsubscribes, detaches its endpoint token, and protects native teardown before node release");

            Check(subscriptionBinding.Contains("dropBeforeApply", StringComparison.Ordinal)
                  && subscriptionBinding.Contains("SameOriginDropCount", StringComparison.Ordinal)
                  && subscriptionBinding.Contains("_slot.TryApplyLatest(_tryApplyOwned", StringComparison.Ordinal),
                "181D-4: custom P&S drops its own origin only after bounded callback copying and before DTO construction");

            Check(customOutboundPolicy.Contains("MaximumBytes = 4L * 1024L * 1024L", StringComparison.Ordinal)
                  && !customOutboundPolicy.Contains("foxRunRos2NativeCopyBudget", StringComparison.Ordinal)
                  && customMapperEmitter.Contains("FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds", StringComparison.Ordinal)
                  && customMapperEmitter.Contains("__FoxRunRos2CustomDisposeEnvelope", StringComparison.Ordinal)
                  && customPublishEmitter.Contains("__FoxRunRos2CustomMapDtoToEnvelope", StringComparison.Ordinal),
                "181D-5: outbound mapping uses its fixed 4 MiB cap, timestamp bounds, and exact-once generated disposal");

            Check(foxgloveLogHub.Contains("TryResolvePublishRoutes", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains("publishLive || publishBus", StringComparison.Ordinal)
                  && foxgloveLogHub.Contains("busSource.FoxgloveLog_PublishToBus", StringComparison.Ordinal)
                  && !legacyR2fuTopicSink.Contains("FoxRunRos2Custom", StringComparison.Ordinal),
                "181D-6: WebSocket and typed-bus routes fan out independently while the legacy byte/CDR sink remains separate");
        }

        private static void VerifyTypesupportPreflightPresentation(
            string discovery,
            string preflight,
            string inspector,
            string managerR2fuRuntimeInspector,
            string interfaceCommand)
        {
            var requiredStates = new[]
            {
                "NotRequired", "MissingSource", "StaleSource", "MissingAddOn", "MultipleAddOns",
                "DistributionMismatch", "DigestMismatch", "InvalidManifest", "InvalidInventory",
                "MissingManagedType", "MissingCatalog", "DuplicateCatalog", "UnsupportedRmw", "Settling", "Ready",
            };
            Check(requiredStates.All(state => preflight.Contains(state, StringComparison.Ordinal))
                  && discovery.Contains("InvalidateCache", StringComparison.Ordinal)
                  && !discovery.Contains("Client.Resolve", StringComparison.Ordinal)
                  && !discovery.Contains("Assembly.Load", StringComparison.Ordinal)
                  && !discovery.Contains("NativeLibrary.Load", StringComparison.Ordinal)
                  && !discovery.Contains("Ros2cs.Init", StringComparison.Ordinal)
                  && !discovery.Contains("CreateNode", StringComparison.Ordinal)
                  && !discovery.Contains("using ROS2", StringComparison.Ordinal),
                "181E-1: metadata-only preflight has distinct bounded readiness states and no resolver or native load path");

            Check(inspector.Contains("Custom FoxRun ROS 2 Interface", StringComparison.Ordinal)
                  && inspector.Contains("Install and Select Matching Typesupport Add-On", StringComparison.Ordinal)
                  && inspector.Contains("FilterCandidatesForRuntime", StringComparison.Ordinal)
                  && inspector.Contains("runtime.RosDistro", StringComparison.Ordinal)
                  && inspector.Contains("runtime.Platform", StringComparison.Ordinal)
                  && !inspector.Contains("PendingAddOnByProject", StringComparison.Ordinal)
                  && !inspector.Contains("EditorGUILayout.Popup(\"Matching Add-On\"", StringComparison.Ordinal)
                  && inspector.Contains("var selectionChangeBlocked = EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && inspector.Contains("|| EditorApplication.isCompiling", StringComparison.Ordinal)
                  && inspector.Contains("|| EditorApplication.isUpdating", StringComparison.Ordinal)
                  && inspector.Contains("catch (InvalidOperationException exception)", StringComparison.Ordinal)
                  && inspector.Contains("+ exception.Message", StringComparison.Ordinal)
                  && inspector.Contains("Generate ROS2 Interface Source Package", StringComparison.Ordinal)
                  && inspector.Contains("Validate ROS2 Interface Source Package", StringComparison.Ordinal)
                  && inspector.Contains("Open ROS2 Interface Source Package", StringComparison.Ordinal)
                  && !inspector.Contains("Client.Resolve", StringComparison.Ordinal)
                  && !inspector.Contains("packages-lock.json", StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "Unity2Foxglove.Ros2ForUnity.Editor.FoxRunRos2CustomTypesupportInspector, Unity2Foxglove.Ros2ForUnity.Editor",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains("DrawOptionalR2fuCustomTypesupportInspector", StringComparison.Ordinal)
                  && interfaceCommand.Contains("ValidateFromMenu", StringComparison.Ordinal)
                   && interfaceCommand.Contains("OpenSourcePackageFromMenu", StringComparison.Ordinal),
                "181E-2: Data Transport reaches one optional custom-interface inspector through an auditable reflection seam");

            var runtimeDemandStart = managerR2fuRuntimeInspector.IndexOf(
                "private bool HasR2fuNativeRuntimeDemand()",
                StringComparison.Ordinal);
            var runtimeDemandEnd = managerR2fuRuntimeInspector.IndexOf(
                "private bool HasR2fuNativeSubscriptionDemand()",
                StringComparison.Ordinal);
            var runtimeDemandBody = runtimeDemandStart >= 0 && runtimeDemandEnd > runtimeDemandStart
                ? managerR2fuRuntimeInspector.Substring(runtimeDemandStart, runtimeDemandEnd - runtimeDemandStart)
                : string.Empty;
            Check(runtimeDemandBody.Contains("HasCustomNativeSubscriptionDemand()", StringComparison.Ordinal),
                "181E-3: custom native input independently creates shared R2FU Runtime demand");

            var runtimeSectionStart = managerR2fuRuntimeInspector.IndexOf(
                "private void DrawR2fuRuntimeSection()",
                StringComparison.Ordinal);
            var runtimeSectionEnd = managerR2fuRuntimeInspector.IndexOf(
                "private void DrawOptionalR2fuRuntimeSelector()",
                StringComparison.Ordinal);
            var runtimeSectionBody = runtimeSectionStart >= 0 && runtimeSectionEnd > runtimeSectionStart
                ? managerR2fuRuntimeInspector.Substring(runtimeSectionStart, runtimeSectionEnd - runtimeSectionStart)
                : string.Empty;
            Check(runtimeSectionBody.Contains(
                      "HasR2fuNativeSubscriptionDemand() || HasCustomNativeSubscriptionDemand()",
                      StringComparison.Ordinal)
                  && runtimeSectionBody.Contains(
                      "if (HasCustomNativeContractDemand())",
                      StringComparison.Ordinal)
                  && runtimeSectionBody.Contains(
                      "DrawOptionalR2fuCustomTypesupportInspector();",
                      StringComparison.Ordinal),
                "181E-4: shared Runtime direction text includes custom native input demand and conditionally renders the custom preflight");
        }

        private static void VerifyEditModeCustomContractSnapshot(
            string foxRunCodeGenerator,
            string managerR2fuRuntimeInspector)
        {
            var snapshotStart = foxRunCodeGenerator.IndexOf(
                "internal static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> CollectCustomNativeContractsForInspector()",
                StringComparison.Ordinal);
            var snapshotEnd = snapshotStart >= 0
                ? foxRunCodeGenerator.IndexOf("        public static FoxRunSchemaInfoVerification VerifyGeneratedSchemaInfoFiles()", snapshotStart, StringComparison.Ordinal)
                : -1;
            var snapshotBody = snapshotStart >= 0 && snapshotEnd > snapshotStart
                ? foxRunCodeGenerator.Substring(snapshotStart, snapshotEnd - snapshotStart)
                : string.Empty;
            Check(snapshotBody.Contains("ScanFoxRunMembers(ignoreReflectionTypeLoadExceptions: true)", StringComparison.Ordinal)
                  && snapshotBody.Contains("FoxRunManifestBuilder.Build", StringComparison.Ordinal)
                  && snapshotBody.Contains("manifest.CustomNativeContracts", StringComparison.Ordinal)
                  && snapshotBody.Contains(".Select(ToSchemaCustomNativeContractInfo)", StringComparison.Ordinal)
                  && snapshotBody.Contains("private static FoxRunSchemaCustomNativeContractInfo ToSchemaCustomNativeContractInfo(", StringComparison.Ordinal)
                  && !snapshotBody.Contains("WriteManifestFiles", StringComparison.Ordinal)
                  && !snapshotBody.Contains("WriteSchemaInfoFiles", StringComparison.Ordinal),
                "181F-17: Edit Mode preflight derives custom contracts from the current reflection snapshot without rewriting generated schema evidence");

            var sourcePackageModelStart = foxRunCodeGenerator.IndexOf(
                "internal static FoxRunGenerationModel CollectReflectionGenerationModelForRos2InterfacePackage()",
                StringComparison.Ordinal);
            var sourcePackageModelEnd = sourcePackageModelStart >= 0
                ? foxRunCodeGenerator.IndexOf(
                    "internal static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> CollectCustomNativeContractsForInspector()",
                    sourcePackageModelStart,
                    StringComparison.Ordinal)
                : -1;
            var sourcePackageModelBody = sourcePackageModelStart >= 0 && sourcePackageModelEnd > sourcePackageModelStart
                ? foxRunCodeGenerator.Substring(sourcePackageModelStart, sourcePackageModelEnd - sourcePackageModelStart)
                : string.Empty;
            Check(sourcePackageModelBody.Contains("ValidateGenerationModel(model, logWarnings: false);", StringComparison.Ordinal)
                  && foxRunCodeGenerator.Contains(
                      "private static void ValidateGenerationModel(FoxRunGenerationModel model, bool logWarnings = true)",
                      StringComparison.Ordinal),
                "181F-19: source-package validation preserves blocking diagnostics without replaying suppressible advisory warnings into the Unity Console");

            Check(managerR2fuRuntimeInspector.Contains(
                      "private static IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> GetCurrentCustomNativeContractsForInspector()",
                      StringComparison.Ordinal)
                  && managerR2fuRuntimeInspector.Contains(
                      "FoxrunCodeGenerator.CollectCustomNativeContractsForInspector()",
                      StringComparison.Ordinal)
                  && !managerR2fuRuntimeInspector.Contains(
                      "GetGeneratedCustomNativeContracts()",
                      StringComparison.Ordinal),
                "181F-18: custom preflight reads the current contract snapshot instead of caching an empty stale generated-schema list");
        }

        private static void VerifyAcceptanceSurface(
            string sample,
            string importedSample,
            string sampleReadme,
            string r2fuPackageJson,
            string acceptanceComponent,
            string playerBuilder,
            string batchProbe,
            string typesupportPluginImporterBuilder)
        {
            Check(sample.Contains("FoxRunMode.PublishOnly", StringComparison.Ordinal)
                  && sample.Contains("FoxRunMode.SubscribeOnly", StringComparison.Ordinal)
                  && sample.Contains("FoxRunMode.PublishAndSubscribe", StringComparison.Ordinal)
                  && sample.Contains("SubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native", StringComparison.Ordinal)
                  && sample.Contains("Encoding = FoxRunWireEncoding.Json", StringComparison.Ordinal)
                  && sample.Contains("byte[]", StringComparison.Ordinal)
                  && sample.Contains("List<long>", StringComparison.Ordinal)
                  && sample.Contains("int?", StringComparison.Ordinal),
                "181F-1: source-only sample exercises locked custom DTO output, input, and directional P&S contracts");

            Check(importedSample.Contains("public sealed class Phase181State", StringComparison.Ordinal)
                  && importedSample.Contains("public sealed class Phase181NestedState", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("using Unity.FoxgloveSDK.Tests.FoxRun.Fixtures", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("public sealed class Phase181State", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("public sealed class Phase181NestedState", StringComparison.Ordinal),
                "181F-13: the imported sample is the sole Unity compile-surface owner of the locked custom DTO identity");

            const string authorityWarningDisable = "#pragma warning disable FOXRUN400";
            const string authorityWarningRestore = "#pragma warning restore FOXRUN400";
            const string bidirectionalField = "[SerializeField] private Phase181State _nativeInputWebSocketOutput";
            Check(sample.IndexOf(authorityWarningDisable, StringComparison.Ordinal) < sample.IndexOf(bidirectionalField, StringComparison.Ordinal)
                  && sample.IndexOf(authorityWarningRestore, StringComparison.Ordinal) > sample.IndexOf(bidirectionalField, StringComparison.Ordinal)
                  && importedSample.IndexOf(authorityWarningDisable, StringComparison.Ordinal) < importedSample.IndexOf(bidirectionalField, StringComparison.Ordinal)
                  && importedSample.IndexOf(authorityWarningRestore, StringComparison.Ordinal) > importedSample.IndexOf(bidirectionalField, StringComparison.Ordinal),
                "181F-14: the sample suppression spans the member declaration where the authority diagnostic is reported");

            const string inputPortField = "[SerializeField] private Phase181State _inputPort";
            const string inputPortView = "public Phase181State NativeInputPort => _inputPort;";
            Check(sample.Contains(inputPortField, StringComparison.Ordinal)
                  && sample.Contains(inputPortView, StringComparison.Ordinal)
                  && importedSample.Contains(inputPortField, StringComparison.Ordinal)
                  && importedSample.Contains(inputPortView, StringComparison.Ordinal)
                  && !sample.Contains("_nativeSubscribeOnly", StringComparison.Ordinal)
                  && !importedSample.Contains("_nativeSubscribeOnly", StringComparison.Ordinal),
                "181F-15: the SubscribeOnly sample member communicates input-port authority and is observably consumed");

            Check(sampleReadme.Contains("static interface lock", StringComparison.OrdinalIgnoreCase)
                  && sampleReadme.Contains("Windows-local", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("FoxRun Custom ROS2 Interface", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("Samples~/FoxRun Custom ROS2 Interface", StringComparison.Ordinal),
                "181F-2: package sample documents its immutable interface identity and has a discoverable import entry");

            var requiredMarkers = new[]
            {
                "PHASE181_CUSTOM_ROS2_READY",
                "PHASE181_CUSTOM_INTERFACE_READY",
                "PHASE181_CUSTOM_ROS2_PUBLISHED",
                "PHASE181_CUSTOM_ROS2_APPLIED",
                "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                "PHASE181_CUSTOM_ROS2_PASS",
                "PHASE181_CUSTOM_ROS2_FAIL",
                "PHASE181_CUSTOM_ROS2_UNAVAILABLE",
            };
            Check(requiredMarkers.All(marker => acceptanceComponent.Contains(marker, StringComparison.Ordinal))
                  && acceptanceComponent.Contains("FoxRunRos2SubscriptionAcceptanceDiagnostics", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("GenerateRunToken", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("IsCorrelatedInitialPayload", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("IsNullEmptyRemotePayload", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("_correlatedSubscribeApplied", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("_correlatedBidirectionalFinalApplied", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("boundedFields + \" token=\" + SanitizeToken(_runToken)", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("10f,\n                600f", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("using ROS2", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("CreateNode", StringComparison.Ordinal)
                  && !acceptanceComponent.Contains("Ros2cs.Init", StringComparison.Ordinal),
                "181F-3: acceptance component correlates the exact custom DTO proof through bounded diagnostics without creating a ROS2 node");

            const string acceptanceInputPort = "[SerializeField] private Phase181State _inputPort";
            const string unavailableGuard = "#if !(UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES)";
            Check(acceptanceComponent.IndexOf(acceptanceInputPort, StringComparison.Ordinal) >= 0
                  && acceptanceComponent.IndexOf(acceptanceInputPort, StringComparison.Ordinal)
                     < acceptanceComponent.IndexOf(unavailableGuard, StringComparison.Ordinal)
                  && acceptanceComponent.Contains("public Phase181State InputPort => _inputPort;", StringComparison.Ordinal)
                  && acceptanceComponent.Contains("#pragma warning restore FOXRUN400", StringComparison.Ordinal),
                "181F-16: acceptance contracts stay generator-visible before add-on selection, while native bindings remain conditional");

            Check(playerBuilder.Contains("CreateAcceptanceScene", StringComparison.Ordinal)
                  && playerBuilder.Contains("BuildWindowsStandalone64", StringComparison.Ordinal)
                  && playerBuilder.Contains("--phase181-custom-ros2-player-auto-quit", StringComparison.Ordinal)
                  && playerBuilder.Contains("EnsurePathWithinBuildRoot", StringComparison.Ordinal)
                  && playerBuilder.Contains("PlayerEnvironmentKeys", StringComparison.Ordinal)
                  && playerBuilder.Contains("ResolveStaticInterfaceLock", StringComparison.Ordinal)
                  && playerBuilder.Contains("UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive",
                      StringComparison.Ordinal),
                "181F-4: acceptance builder preserves interactive scenes and creates a batch-safe bounded Player artifact below the repository build root");

            Check(batchProbe.Contains("Phase181BatchModeCustomRos2InteropProbe", StringComparison.Ordinal)
                  && batchProbe.Contains("Phase181CustomRos2InterfacePlayerBuilder.AcceptanceSceneAssetPath", StringComparison.Ordinal)
                  && batchProbe.Contains("Application.logMessageReceived", StringComparison.Ordinal)
                  && batchProbe.Contains("PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED", StringComparison.Ordinal)
                  && batchProbe.Contains("EditorApplication.ExitPlaymode", StringComparison.Ordinal)
                  && batchProbe.Contains("EditorApplication.Exit", StringComparison.Ordinal)
                  && !batchProbe.Contains("using ROS2", StringComparison.Ordinal)
                  && !batchProbe.Contains("CreateNode", StringComparison.Ordinal),
                "181F-19: the Batch-only Editor probe drives the tracked custom-interface scene and accepts only its bounded terminal evidence markers");

            Check(typesupportPluginImporterBuilder.Contains(
                      "var stageFileName = Path.GetFileName(uniqueFolder) + \"_\" + Path.GetFileName(input);",
                      StringComparison.Ordinal)
                  && typesupportPluginImporterBuilder.Contains(
                      "var stageAssetPath = uniqueFolder + \"/\" + stageFileName;",
                      StringComparison.Ordinal),
                "181F-20: candidate PluginImporter staging gives each DLL a Batch-unique file name, avoiding collisions with active add-on plugins");
        }

        private static void VerifyInteropAutomationReleaseGate(
            string peerProtocol,
            string peerHelper,
            string linuxPeer,
            string matrixProfiles,
            string runCi,
            string dotnetWorkflow,
            string packageWorkflow)
        {
            Check(peerProtocol.Contains("SUMMARY_SCHEMA_VERSION", StringComparison.Ordinal)
                  && peerProtocol.Contains("write_summary_atomic", StringComparison.Ordinal)
                  && peerProtocol.Contains("FAIL_INTERFACE_DIGEST", StringComparison.Ordinal)
                  && !peerProtocol.Contains("shell=True", StringComparison.Ordinal)
                  && !peerProtocol.Contains("os.system", StringComparison.Ordinal),
                "181F-5: common peer protocol persists redacted correlated evidence without shell execution");

            Check(peerHelper.Contains("build_player_environment", StringComparison.Ordinal)
                  && peerHelper.Contains("require_player_exit_code", StringComparison.Ordinal)
                  && peerHelper.Contains("--probe-role", StringComparison.Ordinal)
                  && peerHelper.Contains("--surface", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_GRAPH_EVIDENCE", StringComparison.Ordinal)
                  && peerHelper.Contains("create_typed_worker_endpoints", StringComparison.Ordinal)
                  && peerHelper.Contains("observe_no_late_unity_apply", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_LATE_APPLY", StringComparison.Ordinal)
                  && peerHelper.Contains("FAIL_PEER_RUNTIME", StringComparison.Ordinal)
                  && !peerHelper.Contains("shell=True", StringComparison.Ordinal)
                  && !peerHelper.Contains("os.system", StringComparison.Ordinal),
                "181F-6: Windows Editor and Player paths reuse the strict generated-envelope protocol and bounded endpoint teardown");

            Check(linuxPeer.Contains("stage_or_verify_locked_ros_source", StringComparison.Ordinal)
                  && linuxPeer.Contains("build_linux_worker_environment", StringComparison.Ordinal)
                  && linuxPeer.Contains("--role", StringComparison.Ordinal)
                  && linuxPeer.Contains("--surface", StringComparison.Ordinal)
                  && !linuxPeer.Contains("shell=True", StringComparison.Ordinal)
                  && !linuxPeer.Contains("os.system", StringComparison.Ordinal),
                "181F-7: Linux peer keeps source/workspace ownership explicit and imports only its built generated package");

            Check(matrixProfiles.Contains("DEFAULT_READY_TIMEOUT_SECONDS = 300", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_HUMBLE_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("PHASE181_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS", StringComparison.Ordinal)
                  && matrixProfiles.Contains("write_profile_failure_summary", StringComparison.Ordinal)
                  && !matrixProfiles.Contains("shell=True", StringComparison.Ordinal),
                "181F-8: four named Windows-local wrappers preserve a bounded String-first and owned-Zenoh path");

            Check(runCi.Contains("phase181-ros2-regression", StringComparison.Ordinal)
                  && runCi.Contains("PHASE181_ROS2_PEER_REGRESSION", StringComparison.Ordinal)
                  && runCi.Contains("validate_foxrun_custom_typesupport_addon.py", StringComparison.Ordinal)
                  && dotnetWorkflow.Contains("Run Phase181 custom ROS2 acceptance helper regressions", StringComparison.Ordinal)
                  && packageWorkflow.Contains("Validate Phase181 custom ROS2 typesupport add-ons", StringComparison.Ordinal),
                "181F-10: public CI runs protocol regressions and all tracked custom typesupport preflight validators");
        }

        private static void VerifyRuntimeBatchSelection(string runtimeBatchSelection)
        {
            Check(runtimeBatchSelection.Contains("public static void SelectFromCommandLine()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2Distro", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("-phase181Ros2CommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityCustomTypesupportSelectionTransaction.Apply", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeSelection.SetCommunicationMode", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Client.Resolve()", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Events.registeredPackages", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("PackageInfo.FindForPackageName", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("[InitializeOnLoadMethod]", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("SessionState.SetString", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("Ros2ForUnityRuntimeDefineInstaller.ReconcileCompileSymbolForEditor();", StringComparison.Ordinal)
                  && runtimeBatchSelection.Contains("EditorApplication.Exit(0);", StringComparison.Ordinal),
                "181F-21: an explicit Unity Batch selector applies one validated runtime/add-on and communication-mode transaction before each isolated acceptance row");
        }

        private static void VerifyPublicOperationalDocumentation(
            string acceptanceSampleReadme,
            string ros2SmokeReadme,
            string r2fuPackageJson)
        {
            Check(acceptanceSampleReadme.Contains("exactly one matching", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("static interface lock", StringComparison.OrdinalIgnoreCase)
                  && acceptanceSampleReadme.Contains("PublishAndSubscribe", StringComparison.Ordinal)
                  && r2fuPackageJson.Contains("FoxRun Custom ROS2 Interface", StringComparison.Ordinal),
                "181F-11: package sample exposes the locked custom DTO workflow without treating typesupport as an implicit runtime fallback");

            Check(ros2SmokeReadme.Contains("Phase181 Windows-local Editor bring-up", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("phase181_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("exactly one matching add-on", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("echo-on-apply", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("same-origin", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("FixedRate", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("not individually recorded to MCAP", StringComparison.OrdinalIgnoreCase)
                  && ros2SmokeReadme.Contains("Linux", StringComparison.Ordinal)
                  && ros2SmokeReadme.Contains("Player", StringComparison.Ordinal),
                "181F-12: public custom-interface instructions distinguish local bring-up, certification, origin policy, and MCAP semantics");
        }

        private static string ReadRequiredJsonString(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(propertyName, out var property)
                || property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(property.GetString()))
            {
                throw new InvalidOperationException(
                    "[FAIL] Phase181 static interface lock lacks a non-empty string property: " + propertyName);
            }

            return property.GetString();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
