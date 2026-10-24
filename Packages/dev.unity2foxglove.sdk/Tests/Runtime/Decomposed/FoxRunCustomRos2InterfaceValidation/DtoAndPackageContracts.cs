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
    internal static partial class FoxRunCustomRos2InterfaceValidation
    {
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

        private static void VerifyNoImplicitWebSocketInputFallback(
            string validator,
            string emitter,
            string r2fuAnalyzerPipeline,
            string manifestBuilder)
        {
            Check(validator.Contains("InvalidDirectionalTransportId", StringComparison.Ordinal)
                  && validator.Contains("CanEncodingReachWebSocket(member)", StringComparison.Ordinal)
                  && validator.Contains(
                      "FoxRun Encoding applies only to the foxglove.websocket Provider.",
                      StringComparison.Ordinal)
                  && !validator.Contains("CustomNativeBidirectionalContractDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeSubscribeDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeEncodingDiagnosticId", StringComparison.Ordinal)
                  && !validator.Contains("NativeSourcePublishDiagnosticId", StringComparison.Ordinal),
                "181A-7: neutral validation rejects WebSocket-only encoding on Provider-only routes without restoring legacy ROS diagnostics");

            Check(emitter.Contains("webSocketInputMembers", StringComparison.Ordinal)
                  && emitter.Contains(
                      ".Where(member => member.GeneratesWebSocketCodec)",
                      StringComparison.Ordinal)
                  && !emitter.Contains("Ros2InputDispatchEmitter", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains(
                      "private const string ProviderId = \"unity2foxglove.r2fu\"",
                      StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains("SelectedForSubscribe", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains("SelectedForPublish", StringComparison.Ordinal)
                  && r2fuAnalyzerPipeline.Contains(
                      "Ros2InputDispatchEmitter.EmitConditionalPartial",
                      StringComparison.Ordinal)
                  && manifestBuilder.Contains(
                      ".Where(member => member.GeneratesWebSocketCodec)",
                      StringComparison.Ordinal)
                  && manifestBuilder.Contains("member.PublishTransportIds", StringComparison.Ordinal)
                  && manifestBuilder.Contains("member.SubscribeTransportId", StringComparison.Ordinal)
                  && manifestBuilder.Contains(
                      "member.Flow == (int)FoxRunFlow.PublishAndSubscribe",
                      StringComparison.Ordinal),
                "181A-8: the R2FU analyzer emits only selected Provider partials while core manifests retain neutral routing and WebSocket codec eligibility");
        }

        private static void VerifyDiagnosticRanges(
            string diagnostics,
            string unshippedLedger,
            string shippedLedger,
            string r2fuDiagnostics,
            string r2fuUnshippedLedger)
        {
            var expectedIds = new[]
            {
                "FOXR2F008",
                "FOXR2F009",
                "FOXR2F010",
                "FOXR2F011",
                "FOXR2F012",
                "FOXR2F013",
            };
            Check(expectedIds.All(id =>
                      r2fuDiagnostics.Contains("\"" + id + "\"", StringComparison.Ordinal))
                  && expectedIds.All(id =>
                      r2fuUnshippedLedger.Contains(
                          id + " | FoxRun.R2FU |",
                          StringComparison.Ordinal))
                  && !r2fuUnshippedLedger.Contains("FOXRUN", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN402\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN606\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN607\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN608\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN612\"", StringComparison.Ordinal)
                  && unshippedLedger.Contains(
                      "FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.",
                      StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN205\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN206\"", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN214\"", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN205 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN206 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN214 | FoxRun | Error | Retired before release;", StringComparison.Ordinal)
                  && !diagnostics.Contains("\"FOXRUN036\"", StringComparison.Ordinal)
                  && shippedLedger.Contains("FOXRUN036 | FoxRun |", StringComparison.Ordinal)
                  && unshippedLedger.Contains("FOXRUN036 | FoxRun | Error | Retired;", StringComparison.Ordinal),
                "181A-9: R2FU owns active custom/directional diagnostics while legacy core IDs and FOXRUN036 stay reserved");
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
                  && !interfacePreflight.Contains("DllImport", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("Assembly.Load", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("Ros2ForUnityRuntimeSelection", StringComparison.Ordinal)
                  && !interfacePreflight.Contains("RMW_IMPLEMENTATION", StringComparison.Ordinal),
                "181B-4: R2FU source preflight has independent typed states and performs no RMW or native loading");

            Check(interfaceCommand.Contains("--check", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--generate", StringComparison.Ordinal)
                  && interfaceCommand.Contains("--next-revision", StringComparison.Ordinal)
                  && interfaceCommand.Contains("ExecuteFromCommandLine", StringComparison.Ordinal)
                  && interfaceCommand.Contains("CollectReflectionGenerationModelForTransportProviders", StringComparison.Ordinal)
                  && interfaceCommand.Contains("EditorApplication.Exit", StringComparison.Ordinal),
                "181B-5: explicit R2FU Editor and batch command never lets Play Mode or source generation mutate the package");

            var staticRosPackageName = ReadRequiredJsonString(staticLock, "rosPackageName");
            Check(staticPackageJson.Contains("\"name\": \"dev.unity2foxglove.foxrun.ros2.interfaces\"", StringComparison.Ordinal)
                  && staticLock.Contains("\"lockSchemaVersion\":1", StringComparison.Ordinal)
                  && staticLock.Contains("\"interfaceDigest\":\"", StringComparison.Ordinal)
                  && staticCmake.Contains("project(" + staticRosPackageName + ")", StringComparison.Ordinal)
                  && staticCmake.Contains("rosidl_generate_interfaces", StringComparison.Ordinal),
                "181B-6: tracked static UPM package CMake identity follows the lock-selected portable ROS package revision");

            Check(interfaceRenderer.Contains(
                      "FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName",
                      StringComparison.Ordinal)
                  && interfaceRenderer.Contains("shape.CanonicalIdentity", StringComparison.Ordinal)
                  && interfaceRenderer.Contains("shape.PayloadIdentity", StringComparison.Ordinal)
                  && manifestModel.Contains("PublishTransportIds", StringComparison.Ordinal)
                  && manifestModel.Contains("SubscribeTransportId", StringComparison.Ordinal)
                  && manifestJsonWriter.Contains("publishTransportIds", StringComparison.Ordinal)
                  && manifestJsonWriter.Contains("subscribeTransportId", StringComparison.Ordinal)
                  && descriptorWriter.Contains("publishTransportIds", StringComparison.Ordinal)
                  && descriptorWriter.Contains("subscribeTransportId", StringComparison.Ordinal),
                "181B-7: R2FU owns DTO envelope identity while core manifest and descriptor evidence carry neutral Provider selection");

            Check(!sourceGeneratorProject.Contains("FoxRunRos2InterfaceDigest.cs", StringComparison.Ordinal)
                  && !sourceGeneratorProject.Contains("FoxRunRos2InterfaceLock.cs", StringComparison.Ordinal)
                  && !sourceGeneratorProject.Contains("FoxRunRos2InterfaceJsonWriter.cs", StringComparison.Ordinal)
                  && !descriptorWriter.Contains("using Unity.FoxgloveSDK.Components;", StringComparison.Ordinal),
                "181B-8: static-package editor helpers stay out of the analyzer compile surface");

            var mapperDirectory = Path.Combine(
                PhaseValidationSourceHelpers.FindRequiredRepoRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "Native",
                "FoxRun",
                "Decomposed",
                "Ros2CustomDtoMapperEmitter");
            var expectedMapperFiles = new[]
            {
                "Ros2CustomDtoMapperEmitter.Mapping.cs",
                "Ros2CustomDtoMapperEmitter.RosPayload.cs",
                "Ros2CustomDtoMapperEmitter.Equality.cs",
                "Ros2CustomDtoMapperEmitter.Utilities.cs",
            };
            var actualMapperFiles = Directory.GetFiles(mapperDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            Check(expectedMapperFiles.ToHashSet(StringComparer.Ordinal).SetEquals(actualMapperFiles)
                  && expectedMapperFiles.All(name => sourceGeneratorProject.Contains(name, StringComparison.Ordinal)),
                "181B-9: R2FU source-generator compile entries exactly cover the mapper partial source set");
        }

    }
}
