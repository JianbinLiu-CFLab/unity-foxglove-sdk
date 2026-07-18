// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Phase181 custom ROS2 DTO interface boundary.

using System;
using System.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Locks the ROS-free custom DTO model and its directional native policy.
    /// Endpoint generation and R2FU activation deliberately belong to later
    /// Phase181 waves.
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

            Check(staticPackageJson.Contains("\"name\": \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && staticLock.Contains("\"lockSchemaVersion\":1", StringComparison.Ordinal)
                  && staticLock.Contains("\"interfaceDigest\":\"", StringComparison.Ordinal)
                  && staticCmake.Contains("project(unity2foxglove_foxrun_interfaces_v1)", StringComparison.Ordinal)
                  && staticCmake.Contains("rosidl_generate_interfaces", StringComparison.Ordinal),
                "181B-6: tracked static UPM package contains only locked portable ROS source metadata and message build inputs");

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
                  && !entries[0].IncludeInDefault
                  && entries[0].Evidence == ValidationEvidence.Structural
                  && defaultEntries.Length == 0
                  && string.Equals(entries[0].Name, "FoxRun custom ROS2 interface boundary", StringComparison.Ordinal),
                "181A-10: Phase181 has one non-default structural registry entry");
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
