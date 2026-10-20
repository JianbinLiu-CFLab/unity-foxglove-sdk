// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Guards the FoxRun native subscription policy and dependency boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class FoxRunRos2NativeSubscriptionValidation
    {
        private const string CorePackageRoot = "Packages/dev.unity2foxglove.sdk";
        private const string OptionalRuntimeAsmdefGuid = "f8feed905315b394d8d0f92bf2441283";
        private const string OptionalRuntimeAsmdefPath =
            "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef";
        private const string ValidationSource =
            CorePackageRoot + "/Tests/Runtime/FoxRunRos2NativeSubscriptionValidation.cs";
        private const string ValidationMeta = ValidationSource + ".meta";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun Native ROS2 Subscription Generation Boundary ---");
            _passed = 0;

            VerifyWireEncodingAndProviderAxes();
            VerifyDependencyInspectionCoverage();
            VerifyCoreDependencyBoundary();
            VerifyIndependentManagerSubscriptionSession();
            VerifyLegacyProviderMigrationDefaultsToWebSocket();
            VerifyExistingR2fuSinkRemainsOutboundOnly();
            VerifyTypedGenerationAndNativeCatalogExclusion();
            VerifyDiagnosticIdSemanticRanges();
            VerifyDiagnosticIdLegacyLedger();
            VerifyOptionalCompilationLanes();
            VerifyNativeHostLifecycleBoundary();
            VerifyRuntimeDiagnosticsSurface();
            VerifyManualOwnershipProbe();
            VerifyInteropAcceptanceSurface();
            VerifyPermanentOptionalCompilationCiGates();
            VerifyRegistryAndProjectWiring();

            Console.WriteLine(
                "FoxRun native ROS2 subscription generation boundary: " + _passed + " checks passed.\n");
        }

        private static void VerifyWireEncodingAndProviderAxes()
        {
            var wireNames = Enum.GetNames(typeof(FoxRunEncoding));
            Check(wireNames.SequenceEqual(new[] { "Protobuf", "JSON", "MessagePack" })
                  && (int)(FoxRunEncoding)0 == 0
                  && (int)FoxRunEncoding.Protobuf == 1
                  && (int)FoxRunEncoding.JSON == 2
                  && (int)FoxRunEncoding.MessagePack == 3,
                "FoxRun encoding exposes Protobuf, JSON, and MessagePack while zero remains an internal omission sentinel");

            var publishProviders = typeof(FoxRunAttribute).GetProperty(
                nameof(FoxRunAttribute.PublishTransportIds));
            var subscribeProvider = typeof(FoxRunAttribute).GetProperty(
                nameof(FoxRunAttribute.SubscribeTransportId));
            Check(publishProviders?.PropertyType == typeof(string[])
                  && subscribeProvider?.PropertyType == typeof(string)
                  && FoxRunRos2TransportProvider.IdValue
                     == "unity2foxglove.r2fu"
                  && FoxgloveWebSocketTransport.Id
                     == "foxglove.websocket"
                  && typeof(FoxRunDeliveryReliability).IsEnum,
                "FoxRun directions use stable neutral Provider IDs while R2FU owns its transport identity");
        }

        private static void VerifyDependencyInspectionCoverage()
        {
            var asmdefGuidFixture = JObject.Parse(
                "{\"references\":[\"GUID:" + OptionalRuntimeAsmdefGuid + "\"]}");
            var precompiledAsmdefFixture = JObject.Parse(
                "{\"precompiledReferences\":[\"ROS2.dll\"]}");
            var projectFixture = XDocument.Parse(
                "<Project><ItemGroup><Reference Include=\"ROS2ForUnity, Version=1.0.0.0\" /></ItemGroup></Project>");
            var packageFixture = JObject.Parse(
                "{\"dependencies\":{\"optional-adapter\":\"file:../dev.unity2foxglove.ros2forunity\"}}");
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var guidIndex = BuildAsmdefGuidIndex(repoRoot);
            var guidReference = ReadAsmdefReferences(asmdefGuidFixture).Single();
            var resolved = TryResolveAsmdefReference(guidReference, guidIndex, out var target);

            Check(resolved
                  && target.RelativePath == OptionalRuntimeAsmdefPath
                  && target.Name == "Unity2Foxglove.Ros2ForUnity"
                  && IsOptionalAsmdefReference(guidReference, guidIndex)
                  && !IsOptionalAsmdefReference(
                      "GUID:00000000000000000000000000000000",
                      guidIndex)
                  && ReadAsmdefReferences(precompiledAsmdefFixture).Any(IsOptionalNativeDependency)
                  && ReadProjectDependencyReferences(projectFixture).Any(IsOptionalNativeDependency)
                  && ReadPackageDependencyReferences(packageFixture).Any(IsOptionalNativeDependency)
                  && IsBuildArtifactPath(Path.Combine("source", "obj", "generated.csproj"))
                  && !IsBuildArtifactPath(Path.Combine("source", "Runtime", "source.csproj")),
                "dependency inspection resolves tracked asmdef GUIDs, covers direct and precompiled references, and excludes build artifacts");
        }

        private static void VerifyCoreDependencyBoundary()
        {
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var coreRoot = Path.Combine(
                repoRoot,
                CorePackageRoot.Replace('/', Path.DirectorySeparatorChar));
            var guidIndex = BuildAsmdefGuidIndex(repoRoot);

            var forbiddenAsmdefReferences = EnumerateSourceDefinitionFiles(coreRoot, "*.asmdef")
                .SelectMany(ReadAsmdefReferences)
                .Where(reference => IsOptionalAsmdefReference(reference, guidIndex))
                .ToArray();
            Check(forbiddenAsmdefReferences.Length == 0,
                "core asmdef references remain free of optional ROS2 For Unity dependencies");

            var forbiddenProjectReferences = EnumerateSourceDefinitionFiles(coreRoot, "*.csproj")
                .Where(path => !IsTestProjectPath(coreRoot, path))
                .SelectMany(ReadProjectDependencyReferences)
                .Where(IsOptionalNativeDependency)
                .ToArray();
            var package = JObject.Parse(PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/package.json"));
            var packageReferences = ReadPackageDependencyReferences(package);
            Check(forbiddenProjectReferences.Length == 0
                  && !packageReferences.Any(IsOptionalNativeDependency),
                "core project and package references remain free of optional ROS2 For Unity dependencies");
        }

        private static void VerifyIndependentManagerSubscriptionSession()
        {
            var manager = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.cs");
            var server = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var session = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunSubscriptionSession.cs");
            var onEnable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnEnable()");
            var update = PhaseValidationSourceHelpers.SourceMethod(manager, "private void Update()");
            var onDisable = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDisable()");
            var onDestroy = PhaseValidationSourceHelpers.SourceMethod(manager, "private void OnDestroy()");
            var sync = PhaseValidationSourceHelpers.SourceMethod(
                session,
                "private void SyncFoxRunSubscriptionSession()");

            Check(OccursBefore(
                      onEnable,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "StartServer();")
                  && update.Contains("SyncFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && sync.Contains("if (_enableFoxRunInbound)", StringComparison.Ordinal)
                  && sync.Contains("BeginFoxRunSubscriptionSessionIfNeeded();", StringComparison.Ordinal)
                  && sync.Contains("EndFoxRunSubscriptionSession();", StringComparison.Ordinal)
                  && OccursBefore(
                      onDisable,
                      "EndFoxRunSubscriptionSession,",
                      "StopServer(restoreLivePublishers: true),")
                  && OccursBefore(
                      onDestroy,
                      "EndFoxRunSubscriptionSession,",
                      "StopServer(restoreLivePublishers: true),"),
                "Manager enable, update, and teardown own the subscription session lifecycle");

            var startServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "public void StartServer()");
            var stopServer = PhaseValidationSourceHelpers.SourceMethod(
                server,
                "private void StopServer(bool restoreLivePublishers)");
            Check(OccursBefore(
                      startServer,
                      "BeginFoxRunPublishSessionIfNeeded();",
                      "BeginFoxRunSubscriptionSessionIfNeeded();")
                  && OccursBefore(
                      startServer,
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "if (!_foxgloveOutputEnabled)")
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunPublishSession", StringComparison.Ordinal),
                "WebSocket start ensures both Manager-lifetime directional snapshots before the output gate and restart preserves them");
        }

        private static void VerifyLegacyProviderMigrationDefaultsToWebSocket()
        {
            var inbound = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var providers = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");
            var managerMigration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var attribute = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Attributes/FoxRunAttribute.cs");

            Check(providers.Contains(
                      "_foxRunPublishTransportIds =",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "_foxRunSubscribeTransportId =",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && providers.Contains(
                      "string.IsNullOrWhiteSpace(_foxRunSubscribeTransportId)",
                      StringComparison.Ordinal)
                  && inbound.Contains(
                      "DefaultFoxRunSubscribeTransportId =>",
                      StringComparison.Ordinal)
                  && inbound.Contains(
                      "ConfiguredFoxRunSubscribeTransportId",
                      StringComparison.Ordinal)
                  && managerMigration.Contains(
                      "ISerializationCallbackReceiver.OnAfterDeserialize()",
                      StringComparison.Ordinal)
                  && !managerMigration.Contains(
                      "_defaultFoxRunSubscriptionSource",
                      StringComparison.Ordinal)
                  && attribute.Contains(
                      "PublishTransportIds",
                      StringComparison.Ordinal)
                  && attribute.Contains(
                      "SubscribeTransportId",
                      StringComparison.Ordinal)
                  && !attribute.Contains("FoxRunEndpoint", StringComparison.Ordinal),
                "neutral Provider selections default to WebSocket and the retired serialized endpoint model is absent");
        }

        private static void VerifyExistingR2fuSinkRemainsOutboundOnly()
        {
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var legacySink = Path.Combine(
                repoRoot,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Runtime",
                "Ros2R2FUTopicSink.cs");
            var provider = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs");
            Check(!File.Exists(legacySink)
                  && provider.Contains(
                      "public sealed class FoxRunRos2TransportProvider",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportCapabilities.Publish",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportCapabilities.Subscribe",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportPublishResult Publish(",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "FoxRunTransportSubscribeResult Subscribe(",
                      StringComparison.Ordinal),
                "the retired outbound-only sink is gone and one R2FU Provider owns both directions");
        }

        private static void VerifyTypedGenerationAndNativeCatalogExclusion()
        {
            var emitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2InputDispatchEmitter.cs");
            var router = PhaseValidationSourceHelpers.ReadRegisteredCSharpSource(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunInputRouter.cs",
                CorePackageRoot + "/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Dispatch.cs",
                CorePackageRoot + "/Runtime/Components/FoxRun/Decomposed/FoxRunInputRouter/Lifetime.cs");
            var catalog = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");

            var registerIndex = emitter.IndexOf(
                ": \"Register<\" + typeName + \">(\"",
                StringComparison.Ordinal);
            var copyIndex = emitter.IndexOf(
                "static (source, budget) => __FoxRunRos2Copy_",
                registerIndex < 0 ? 0 : registerIndex,
                StringComparison.Ordinal);
            var applyIndex = emitter.IndexOf(
                "owned => __FoxRunRos2Apply_",
                registerIndex < 0 ? 0 : registerIndex,
                StringComparison.Ordinal);
            Check(registerIndex >= 0
                  && copyIndex > registerIndex
                  && applyIndex > copyIndex
                  && !emitter.Contains("MakeGenericMethod", StringComparison.Ordinal)
                  && !emitter.Contains("Activator", StringComparison.Ordinal)
                  && !emitter.Contains("dynamic", StringComparison.Ordinal)
                  && !emitter.Contains("EnqueueRaw", StringComparison.Ordinal)
                  && emitter.Contains(".TryEnqueueOwnedAfterAdmission(", StringComparison.Ordinal),
                "generated native registration is closed-generic and supplies owned copy before apply without raw enqueue");

            Check(router.Contains("info.SubscribeTransportId", StringComparison.Ordinal)
                  && router.Contains("!info.SupportsWebSocket", StringComparison.Ordinal)
                  && router.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "selectedIds.Length != 1",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "FoxgloveWebSocketTransport.Id",
                      StringComparison.Ordinal)
                  && catalog.Contains(
                      "bindings.Any(binding => !binding.SupportsWebSocket)",
                      StringComparison.Ordinal)
                  && !catalog.Contains("\"cdr\"", StringComparison.OrdinalIgnoreCase),
                "byte router and subscription catalog exclude effective native contracts and never advertise cdr");
        }

        private static void VerifyDiagnosticIdSemanticRanges()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            var unshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var r2fuDiagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunR2fuDiagnostics.cs");
            var r2fuUnshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var subscribe = new[]
            {
                new KeyValuePair<string, string>("UnsupportedInboundShape", "FOXRUN200"),
                new KeyValuePair<string, string>("InboundNaming", "FOXRUN202"),
                new KeyValuePair<string, string>("InboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("SharedInboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("InvalidStreamDeclaration", "FOXRUN215"),
                new KeyValuePair<string, string>("StreamInitializerMissing", "FOXRUN216"),
            };
            var infrastructure = new[]
            {
                new KeyValuePair<string, string>("InvalidFoxRunFlow", "FOXRUN600"),
                new KeyValuePair<string, string>("InvalidEncoding", "FOXRUN602"),
                new KeyValuePair<string, string>("InvalidProtobufFieldNumber", "FOXRUN603"),
                new KeyValuePair<string, string>("MixedTopicEncoding", "FOXRUN604"),
                new KeyValuePair<string, string>("DuplicateProtobufFieldNumber", "FOXRUN605"),
                new KeyValuePair<string, string>("TriggerRateConflict", "FOXRUN609"),
                new KeyValuePair<string, string>("GeneratedMethodConflict", "FOXRUN610"),
                new KeyValuePair<string, string>("UnsupportedMessagePackShape", "FOXRUN616"),
                new KeyValuePair<string, string>("MessagePackProtobufFieldNumber", "FOXRUN617"),
                new KeyValuePair<string, string>("MessagePackInboundTopology", "FOXRUN618"),
                new KeyValuePair<string, string>("MessagePackSchedule", "FOXRUN619"),
                new KeyValuePair<string, string>("InvalidTransportSelection", "FOXRUN620"),
                new KeyValuePair<string, string>("InvalidDirectionalTransport", "FOXRUN621"),
                new KeyValuePair<string, string>("InvalidDeliveryPolicy", "FOXRUN622"),
            };
            var retiredClosedTransport = new[]
            {
                "FOXRUN204",
                "FOXRUN207",
                "FOXRUN208",
                "FOXRUN209",
                "FOXRUN210",
                "FOXRUN211",
                "FOXRUN212",
                "FOXRUN402",
                "FOXRUN606",
                "FOXRUN607",
                "FOXRUN608",
                "FOXRUN611",
                "FOXRUN612",
                "FOXRUN613",
                "FOXRUN614",
                "FOXRUN615",
            };
            var r2fuOwned = Enumerable.Range(1, 16)
                .Select(number => "FOXR2F" + number.ToString("000"))
                .ToArray();

            Check(diagnostics.Contains("#region FoxRun publish diagnostics (FOXRUN001-199)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun Subscribe diagnostics (FOXRUN200-399)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun cross-direction diagnostics (FOXRUN600+)", StringComparison.Ordinal)
                  && !diagnostics.Contains("PublishAndSubscribe diagnostics", StringComparison.Ordinal),
                "core FoxRun diagnostics retain only neutral publish, subscribe, and cross-direction ranges");

            var expected = subscribe.Concat(infrastructure).ToArray();
            Check(expected.All(pair => HasDiagnosticDescriptorId(diagnostics, pair.Key, pair.Value))
                  && expected.All(pair => unshipped.Contains(pair.Value + " | FoxRun |", StringComparison.Ordinal))
                  && !diagnostics.Contains("IgnoredSubscribePolicy", StringComparison.Ordinal)
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN201",
                      "Retired before release; subscription policy now applies symmetrically and this ID is permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN400",
                      "Retired before release; valid PublishAndSubscribe is an explicit flow and ownership guidance belongs in documentation, so this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN401",
                      "Retired before release; directional profiles resolve full-duplex encodings independently and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN205",
                      "Retired before release; Ros2Native is now a legal Subscribe or PublishAndSubscribe Source and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN206",
                      "Retired before release; directional Encoding legality now uses FOXRUN612 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN214",
                      "Retired before release; directional Source legality now uses FOXRUN612 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN213",
                      "Retired before release; explicit QoS without a ROS 2 direction now fails with FOXRUN614 and this ID remains permanently reserved.")
                  && HasReservedBeforeReleaseRow(
                      unshipped,
                      "FOXRUN601",
                      "Retired before release with the removed Unless declaration and remains permanently reserved.")
                  && unshipped.Contains(
                      "FOXRUN204, FOXRUN207-FOXRUN212, FOXRUN402, FOXRUN606-FOXRUN608, and FOXRUN611-FOXRUN615 are retired before release with the removed closed transport model and remain permanently reserved.",
                      StringComparison.Ordinal)
                  && retiredClosedTransport.All(id =>
                      !diagnostics.Contains(id, StringComparison.Ordinal))
                  && r2fuOwned.All(id =>
                      r2fuDiagnostics.Contains(id, StringComparison.Ordinal)
                      && r2fuUnshipped.Contains(
                          id + " | FoxRun.R2FU |",
                          StringComparison.Ordinal))
                  && !r2fuUnshipped.Contains("FOXRUN", StringComparison.Ordinal),
                "core reserves removed ROS diagnostics while the R2FU analyzer owns executable FOXR2F diagnostics");
        }
    }
}
