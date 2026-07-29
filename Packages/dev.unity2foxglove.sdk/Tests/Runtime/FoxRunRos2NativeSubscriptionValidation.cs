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

namespace Unity.FoxgloveSDK.Tests
{
    internal static class FoxRunRos2NativeSubscriptionValidation
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

            var endpointNames = Enum.GetNames(typeof(FoxRunEndpoint));
            var sourceProperty = typeof(FoxRunAttribute).GetProperty(
                nameof(FoxRunAttribute.Source));
            var targetsProperty = typeof(FoxRunAttribute).GetProperty(
                nameof(FoxRunAttribute.Targets));
            Check(endpointNames.SequenceEqual(
                      new[] { "Foxglove", "Ros2Native", "Ros2Bridge" })
                  && (int)(FoxRunEndpoint)0 == 0
                  && (int)FoxRunEndpoint.Foxglove == 1
                  && (int)FoxRunEndpoint.Ros2Native == 2
                  && (int)FoxRunEndpoint.Ros2Bridge == 4
                  && sourceProperty?.PropertyType == typeof(FoxRunEndpoint)
                  && targetsProperty?.PropertyType == typeof(FoxRunEndpoint)
                  && typeof(FoxRunEndpoint) != typeof(FoxRunEncoding),
                "FoxRun Source and flagged Targets share one endpoint vocabulary separate from Encoding");
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
                      "BeginFoxRunSubscriptionSessionIfNeeded();",
                      "if (!_foxgloveOutputEnabled)")
                  && !stopServer.Contains("EndFoxRunSubscriptionSession", StringComparison.Ordinal)
                  && !startServer.Contains("BeginFoxRunPublishSessionIfNeeded", StringComparison.Ordinal)
                  && !stopServer.Contains("EndFoxRunPublishSession", StringComparison.Ordinal),
                "WebSocket restart preserves both Manager-lifetime directional snapshots");
        }

        private static void VerifyLegacyProviderMigrationDefaultsToWebSocket()
        {
            var inbound = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.Inbound.cs");
            var managerMigration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/Manager/FoxgloveManager.FoxRunPolicyMigration.cs");
            var migration = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunEncodingPolicyMigration.cs");
            var legacyBranch = migration.IndexOf(
                "if (serializationVersion < CurrentSerializationVersion)",
                StringComparison.Ordinal);
            var legacyWebSocket = migration.IndexOf(
                "sourceDefault = FoxRunEndpoint.Foxglove;",
                legacyBranch < 0 ? 0 : legacyBranch,
                StringComparison.Ordinal);
            var normalizeCall = migration.IndexOf(
                "sourceDefault = NormalizeSubscriptionSource(sourceDefault);",
                legacyBranch < 0 ? 0 : legacyBranch,
                StringComparison.Ordinal);
            var normalizeDefinition = migration.IndexOf(
                "private static FoxRunEndpoint NormalizeSubscriptionSource",
                StringComparison.Ordinal);
            var preserveNative = migration.IndexOf(
                "? FoxRunEndpoint.Ros2Native",
                normalizeDefinition < 0 ? 0 : normalizeDefinition,
                StringComparison.Ordinal);
            var fallbackWebSocket = migration.IndexOf(
                ": FoxRunEndpoint.Foxglove",
                normalizeDefinition < 0 ? 0 : normalizeDefinition,
                StringComparison.Ordinal);

            Check(inbound.Contains(
                      "_defaultFoxRunSubscriptionSource = FoxRunEndpoint.Foxglove",
                      StringComparison.Ordinal)
                  && managerMigration.Contains("ISerializationCallbackReceiver.OnAfterDeserialize()", StringComparison.Ordinal)
                  && managerMigration.Contains("ref _defaultFoxRunSubscriptionSource", StringComparison.Ordinal)
                  && legacyBranch >= 0
                  && legacyWebSocket > legacyBranch
                  && normalizeCall > legacyWebSocket
                  && normalizeDefinition > normalizeCall
                  && preserveNative > normalizeDefinition
                  && fallbackWebSocket > preserveNative,
                "serialized Source migration defaults legacy or invalid values to Foxglove and preserves Native");
        }

        private static void VerifyExistingR2fuSinkRemainsOutboundOnly()
        {
            var sink = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Ros2R2FUTopicSink.cs");
            Check(sink.Contains(
                      "public sealed class Ros2R2FUTopicSink",
                      StringComparison.Ordinal)
                  && sink.Contains("IFoxTopicSink,", StringComparison.Ordinal)
                  && sink.Contains("IRos2TopicPublisherFactory", StringComparison.Ordinal)
                  && sink.Contains("publisher.TryPublish(", StringComparison.Ordinal)
                  && !sink.Contains("CreateSubscription", StringComparison.Ordinal)
                  && !sink.Contains("IUnity2FoxgloveRos2Subscription", StringComparison.Ordinal)
                  && !sink.Contains("IRos2TopicSubscriber", StringComparison.Ordinal),
                "existing R2FU topic sink remains outbound-only without subscription ownership");
        }

        private static void VerifyTypedGenerationAndNativeCatalogExclusion()
        {
            var emitter = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Shared/FoxgloveSourceEmitter/Ros2InputDispatchEmitter.cs");
            var router = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunInputRouter.cs");
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
                  && emitter.Contains(".TryEnqueueOwned(", StringComparison.Ordinal),
                "generated native registration is closed-generic and supplies owned copy before apply without raw enqueue");

            Check(router.Contains("info.DeclaredSource", StringComparison.Ordinal)
                  && router.Contains("!info.SupportsWebSocket", StringComparison.Ordinal)
                  && router.Contains(
                      "topology.Topology.Source != FoxRunEndpoint.Foxglove",
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
            var shipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var unshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var subscribe = new[]
            {
                new KeyValuePair<string, string>("UnsupportedInboundShape", "FOXRUN200"),
                new KeyValuePair<string, string>("InboundNaming", "FOXRUN202"),
                new KeyValuePair<string, string>("InboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("SharedInboundTargetNotWritable", "FOXRUN203"),
                new KeyValuePair<string, string>("InvalidSource", "FOXRUN204"),
                new KeyValuePair<string, string>("Ros2MessageIdentity", "FOXRUN207"),
                new KeyValuePair<string, string>("Ros2MessageConstructor", "FOXRUN208"),
                new KeyValuePair<string, string>("Ros2MessageNamespace", "FOXRUN209"),
                new KeyValuePair<string, string>("Ros2SchemaMismatch", "FOXRUN210"),
                new KeyValuePair<string, string>("Ros2MessageShape", "FOXRUN211"),
                new KeyValuePair<string, string>("MissingNativeAssemblyReference", "FOXRUN212"),
            };
            var bidirectional = new[]
            {
                new KeyValuePair<string, string>("CustomNativeBidirectionalContract", "FOXRUN402"),
            };
            var infrastructure = new[]
            {
                new KeyValuePair<string, string>("InvalidFoxRunFlow", "FOXRUN600"),
                new KeyValuePair<string, string>("InvalidEncoding", "FOXRUN602"),
                new KeyValuePair<string, string>("InvalidProtobufFieldNumber", "FOXRUN603"),
                new KeyValuePair<string, string>("MixedTopicEncoding", "FOXRUN604"),
                new KeyValuePair<string, string>("DuplicateProtobufFieldNumber", "FOXRUN605"),
                new KeyValuePair<string, string>("InvalidQos", "FOXRUN613"),
                new KeyValuePair<string, string>("QosRequiresRos2Direction", "FOXRUN614"),
                new KeyValuePair<string, string>("MixedDirectionalQosContract", "FOXRUN615"),
            };
            var retired = Enumerable.Range(23, 22)
                .Select(number => "FOXRUN" + number.ToString("000"))
                .ToArray();

            Check(diagnostics.Contains("#region FoxRun publish diagnostics (FOXRUN001-199)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun Subscribe diagnostics (FOXRUN200-399)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun PublishAndSubscribe diagnostics (FOXRUN400-599)", StringComparison.Ordinal)
                  && diagnostics.Contains("#region FoxRun cross-direction diagnostics (FOXRUN600+)", StringComparison.Ordinal),
                "FoxRun diagnostic source declares direction-readable publish, Subscribe, PublishAndSubscribe, and cross-direction ranges");

            var expected = subscribe.Concat(bidirectional).Concat(infrastructure).ToArray();
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
                  && retired.All(id => !diagnostics.Contains(id, StringComparison.Ordinal)),
                "FoxRun diagnostics map active rules into stable ranges while permanently reserving removed descriptors");
        }

        private static void VerifyDiagnosticIdLegacyLedger()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            var shipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var unshipped = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");
            var retired = new[]
            {
                new KeyValuePair<string, string>("FOXRUN023", "FOXRUN600"),
                new KeyValuePair<string, string>("FOXRUN024", "FOXRUN200"),
                new KeyValuePair<string, string>("FOXRUN025", "FOXRUN201"),
                new KeyValuePair<string, string>("FOXRUN026", "FOXRUN400"),
                new KeyValuePair<string, string>("FOXRUN027", "FOXRUN202"),
                new KeyValuePair<string, string>("FOXRUN028", "FOXRUN203"),
                new KeyValuePair<string, string>("FOXRUN029", "FOXRUN601"),
                new KeyValuePair<string, string>("FOXRUN030", "FOXRUN602"),
                new KeyValuePair<string, string>("FOXRUN031", "FOXRUN603"),
                new KeyValuePair<string, string>("FOXRUN032", "FOXRUN604"),
                new KeyValuePair<string, string>("FOXRUN033", "FOXRUN605"),
                new KeyValuePair<string, string>("FOXRUN034", "FOXRUN401"),
                new KeyValuePair<string, string>("FOXRUN035", "FOXRUN204"),
                new KeyValuePair<string, string>("FOXRUN036", "FOXRUN205"),
                new KeyValuePair<string, string>("FOXRUN037", "FOXRUN206"),
                new KeyValuePair<string, string>("FOXRUN038", "FOXRUN207"),
                new KeyValuePair<string, string>("FOXRUN039", "FOXRUN208"),
                new KeyValuePair<string, string>("FOXRUN040", "FOXRUN209"),
                new KeyValuePair<string, string>("FOXRUN041", "FOXRUN210"),
                new KeyValuePair<string, string>("FOXRUN042", "FOXRUN211"),
                new KeyValuePair<string, string>("FOXRUN043", "FOXRUN212"),
                new KeyValuePair<string, string>("FOXRUN044", "FOXRUN213"),
            };

            Check(diagnostics.Contains(
                      "Legacy FoxRun diagnostic IDs 023 through 044 and unshipped IDs 201, 205, 206, 214, 400, 401, and 601 are permanently retired and must never be reused.",
                      StringComparison.Ordinal)
                  && retired.All(pair => shipped.Contains(pair.Key + " | FoxRun |", StringComparison.Ordinal))
                  && retired.All(pair => unshipped.Contains(pair.Value + " | FoxRun |", StringComparison.Ordinal))
                  && retired.All(pair => HasReleaseTrackingRow(
                      unshipped,
                      pair.Key,
                      "Retired; renumbered as " + pair.Value + " and permanently reserved.")),
                "FoxRun diagnostic migration retains shipped history and permanently reserves every retired ID with its replacement");
        }

        private static void VerifyOptionalCompilationLanes()
        {
            var props = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/FoxgloveSdk.TestSurface.props");
            var unitProject = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Unit/FoxgloveSdk.UnitTests.csproj");
            var runtimeProject = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var nativeStub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/NativeCompileStubs/Ros2ForUnityNativeCompileStubs.cs");

            Check(props.Contains("Runtime/Native/**/*.cs", StringComparison.Ordinal)
                  && props.Contains("Runtime/Native/FoxRun/**/*.cs", StringComparison.Ordinal)
                  && props.Contains("Ros2ForUnityNativeBridgeLifecycleGate.cs", StringComparison.Ordinal)
                  && props.Contains("ros2cs_common.dll", StringComparison.Ordinal)
                  && props.Contains("ros2cs_core.dll", StringComparison.Ordinal)
                  && props.Contains("std_msgs_assembly.dll", StringComparison.Ordinal)
                  && props.Contains("geometry_msgs_assembly.dll", StringComparison.Ordinal)
                  && props.Contains("sensor_msgs_assembly.dll", StringComparison.Ordinal),
                "shared test surface removes broad Native sources, re-includes FoxRun plus lifecycle gate, and pins Jazzy managed references");

            Check(unitProject.Contains("IncludeRos2ForUnityNative", StringComparison.Ordinal)
                  && unitProject.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && unitProject.Contains("ValidatePhase179NativeCompileSurface", StringComparison.Ordinal)
                  && unitProject.Contains("Ros2ForUnityNativeCompileStubs.cs", StringComparison.Ordinal)
                  && runtimeProject.Contains("IncludeRos2ForUnityNative", StringComparison.Ordinal)
                  && runtimeProject.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && runtimeProject.Contains("ValidatePhase179NativeCompileSurface", StringComparison.Ordinal)
                  && runtimeProject.Contains("Ros2ForUnityNativeCompileStubs.cs", StringComparison.Ordinal),
                "unit and runtime Native lanes imply adapter compilation, activate the define, and reject vacuous compile sets");

            Check(nativeStub.Contains("class ROS2UnityComponent", StringComparison.Ordinal)
                  && nativeStub.Contains("class ROS2Node", StringComparison.Ordinal)
                  && nativeStub.Contains("CreateSubscription<T>", StringComparison.Ordinal)
                  && nativeStub.Contains("RemoveSubscription(ISubscriptionBase", StringComparison.Ordinal)
                  && nativeStub.Contains("where T : Message, new()", StringComparison.Ordinal),
                "Native compile-only stubs expose only the source-owned R2FU node and subscription seam");
        }

        private static void VerifyNativeHostLifecycleBoundary()
        {
            const string nativeRoot =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/";
            var hub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2SubscriptionHub.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2SubscriptionBinding.cs");
            var backend = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/Ros2ForUnityFoxRunInboundBackend.cs");
            var lifecycleGate = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "Ros2ForUnityNativeBridgeLifecycleGate.cs");
            var provider = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                nativeRoot + "FoxRun/FoxRunRos2TransportProvider.cs");

            var update = PhaseValidationSourceHelpers.SourceMethod(hub, "private void Update()");
            var applySessionPolicy = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private void ApplySessionPolicy(FoxRunSubscriptionSessionPolicy policy)");
            var ensureNode = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private bool TryEnsureNodeOwner");
            var scan = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private void ScanAndReconcile()");
            var addBinding = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private void AddBinding<T>");
            var nativeAdmission = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "internal bool CanUseNativeRuntimeNow()");
            var beginShutdown = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private void BeginShutdown()");
            var onDisable = PhaseValidationSourceHelpers.SourceMethod(
                hub,
                "private void OnDisable()");
            var callback = PhaseValidationSourceHelpers.SourceMethod(
                binding,
                "private void OnBorrowedMessage");
            var stop = PhaseValidationSourceHelpers.SourceMethod(
                binding,
                "private void StopCore");
            var backendRegister = PhaseValidationSourceHelpers.SourceMethod(
                backend,
                "public FoxRunRos2NativeBackendRegistration Register<T>");
            var bootstrapGateStart = lifecycleGate.IndexOf(
                "internal static bool CanBootstrapBridge",
                StringComparison.Ordinal);
            var bootstrapGateEnd = lifecycleGate.IndexOf(
                "internal static bool CanInitializeNativeRuntimeForBridge",
                bootstrapGateStart,
                StringComparison.Ordinal);
            var bootstrapGate = bootstrapGateStart >= 0 && bootstrapGateEnd > bootstrapGateStart
                ? lifecycleGate.Substring(bootstrapGateStart, bootstrapGateEnd - bootstrapGateStart)
                : string.Empty;

            Check(bootstrapGate.Contains("!_nativeReloadWindow", StringComparison.Ordinal),
                "native subscription bootstrap remains closed while the native reload window would immediately shut its new host down");

            Check(provider.Contains(
                      "GetOrAddOwnedHub<FoxRunRos2SubscriptionHub>()",
                      StringComparison.Ordinal)
                  && provider.Contains(
                      "RegisterFoxRunTransportProvider(this)",
                      StringComparison.Ordinal)
                  && !hub.Contains(
                      "RuntimeInitializeOnLoadMethod",
                      StringComparison.Ordinal)
                  && !hub.Contains("HideAndDontSave", StringComparison.Ordinal),
                "native subscription host is Manager-local Provider-owned without a global bootstrap object");

            var lifecycleWindow = update.IndexOf(
                "IsShuttingDownForBridge(gameObject.scene)",
                StringComparison.Ordinal);
            var lifecycleRefresh = lifecycleWindow < 0
                ? -1
                : update.IndexOf("CanBootstrapBridge", lifecycleWindow, StringComparison.Ordinal);
            var lifecyclePause = lifecycleWindow < 0
                ? -1
                : update.IndexOf("PauseForLifecycleWindow();", lifecycleWindow, StringComparison.Ordinal);
            var lifecycleShutdown = lifecycleWindow < 0
                ? -1
                : update.IndexOf("BeginShutdown();", lifecycleWindow, StringComparison.Ordinal);
            Check(OccursBefore(update, "_stopping", "BeginShutdown();")
                  && lifecycleWindow >= 0
                  && lifecycleRefresh > lifecycleWindow
                  && lifecyclePause > lifecycleRefresh
                  && lifecycleShutdown < 0
                  && hub.Contains("private void PauseForLifecycleWindow()", StringComparison.Ordinal),
                "a transient dirty-scene lifecycle gate refreshes before permanent shutdown and pauses native bindings without orphaning the Hub");

            var nativeGate = ensureNode.IndexOf(
                "CanInitializeNativeRuntimeForBridge(gameObject.scene)",
                StringComparison.Ordinal);
            Check(nativeGate >= 0
                  && nativeGate < ensureNode.IndexOf("_nodeOwner", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("GetComponent<", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("AddComponent<", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf(".Ok()", StringComparison.Ordinal)
                  && nativeGate < ensureNode.IndexOf("CreateNode(", StringComparison.Ordinal)
                  && OccursBefore(
                      scan,
                      "CanInitializeNativeRuntimeForBridge",
                      "Binding.TryRegister()")
                  && OccursBefore(
                      addBinding,
                      "CanInitializeNativeRuntimeForBridge",
                      "binding.TryRegister()")
                  && hub.Contains(
                      "internal sealed class FoxRunRos2NativeRuntimeAdmission",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains(
                      "_activeSession.ReadGeneration() >= 0",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains(
                      "CanInitializeNativeRuntimeForBridge(",
                      StringComparison.Ordinal)
                  && nativeAdmission.Contains("_ownerScene", StringComparison.Ordinal)
                  && ensureNode.Contains(
                      "new FoxRunRos2NativeRuntimeAdmission(",
                      StringComparison.Ordinal)
                  && ensureNode.Contains(
                      "admission.CanUseNativeRuntimeNow",
                      StringComparison.Ordinal)
                  && OccursBefore(
                      backendRegister,
                      "_canUseNativeRuntime()",
                      "_driver.CreateSubscription("),
                "every component lookup, R2FU readiness check, node creation, and subscription registration is lifecycle-gated");

            Check(OccursBefore(beginShutdown, "_stopping = true", "SetManager(null);")
                  && OccursBefore(beginShutdown, "SetManager(null);", "StopBindingsAndNode();")
                  && beginShutdown.Contains(
                      "Application.quitting -= OnApplicationQuitting;",
                      StringComparison.Ordinal)
                  && OccursBefore(onDisable, "_stopping = true", "BeginShutdown();")
                  && OccursBefore(stop, "Volatile.Write(ref _stopping, 1)", "_slot.BeginStop")
                  && OccursBefore(stop, "_slot.BeginStop", "_backend.RemoveSubscription")
                  && OccursBefore(stop, "_backend.RemoveSubscription", "_slot.Stop")
                  && OccursBefore(stop, "_slot.Stop", "ReleaseNodeIfClaimed"),
                "shutdown closes callback admission, detaches subscriptions, drains owned copies, releases the node, and unsubscribes events in order");

            var forbiddenCallbackTokens = new[]
            {
                "UnityEngine.Object",
                "FindObject",
                "GetComponent",
                "AddComponent",
                "ROS2UnityComponent.Ok",
                "CreateNode",
                "CreateSubscription",
                "Debug.Log",
                "SceneManager",
            };
            Check(forbiddenCallbackTokens.All(
                      token => callback.IndexOf(token, StringComparison.Ordinal) < 0)
                  && hub.Contains("_activeSession.ReadGeneration", StringComparison.Ordinal)
                  && !addBinding.Contains("ActiveGeneration,", StringComparison.Ordinal)
                  && !nativeAdmission.Contains(
                      "FoxRunRos2SubscriptionHub",
                      StringComparison.Ordinal)
                  && backendRegister.Contains("_driver.CreateSubscription(", StringComparison.Ordinal)
                  && backendRegister.Contains("callback,", StringComparison.Ordinal),
                "executor callbacks retain only binding-owned managed state and never reacquire Unity or R2FU runtime services");

            var dispatchesLegacySource = scan.Contains(
                "source.Native.FoxRunRos2RegisterSubscriptions(registrar);",
                StringComparison.Ordinal)
                || scan.Contains(
                    "source.Native?.FoxRunRos2RegisterSubscriptions(registrar);",
                    StringComparison.Ordinal);
            Check(applySessionPolicy.Contains("_activeSession.Activate", StringComparison.Ordinal)
                  && !applySessionPolicy.Contains("TryEnsureNodeOwner", StringComparison.Ordinal)
                  && scan.Contains("_sources.Clear();", StringComparison.Ordinal)
                  && scan.Contains("FoxRunRos2SourceDiscovery.TryGet", StringComparison.Ordinal)
                  && dispatchesLegacySource,
                "default-native session demand is admitted for preflight while zero discovered native sources create no subscription binding");
        }

        private static void VerifyRuntimeDiagnosticsSurface()
        {
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionDiagnostics.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs");
            var backendBoundary = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/IFoxRunRos2NativeBackend.cs");
            var hub = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionHub.cs");
            var optionalInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRunRos2SubscriptionDiagnosticsInspector.cs");
            var optionalInspectorAsmdef = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/Unity2Foxglove.Ros2ForUnity.Native.Editor.asmdef");
            var coreInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.R2fuRuntime.cs");
            var subscribeInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.SubscribeData.cs");
            var runtimeTopicsInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Editor/Manager/FoxgloveManagerEditor.FoxServices.cs");
            var catalog = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Runtime/Components/FoxRun/FoxRunSubscriptionCatalog.cs");
            var runtimeTopicSummary = PhaseValidationSourceHelpers.SourceMethod(
                runtimeTopicsInspector,
                "private static void DrawFoxRunTopicSummary(FoxgloveManager manager)");

            Check(diagnostics.Contains(
                      "public readonly struct FoxRunRos2SubscriptionDiagnosticSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public static class FoxRunRos2SubscriptionRuntimeDiagnostics",
                      StringComparison.Ordinal)
                  && diagnostics.Contains("GetSnapshots", StringComparison.Ordinal)
                   && diagnostics.Contains("LastErrorMessage", StringComparison.Ordinal)
                   && diagnostics.Contains(
                       "LastErrorMessage = FoxRunRos2PublicDiagnostic.Describe(binding.Error);",
                       StringComparison.Ordinal)
                   && binding.Contains(
                       "Diagnostic = FoxRunRos2PublicDiagnostic.Describe(error);",
                       StringComparison.Ordinal)
                   && backendBoundary.Contains(
                       "internal static class FoxRunRos2PublicDiagnostic",
                       StringComparison.Ordinal)
                   && !hub.Contains("exception.Message", StringComparison.Ordinal)
                   && diagnostics.Contains("new LoggedDiagnostic(snapshot.ContractId, snapshot.Error)", StringComparison.Ordinal)
                   && diagnostics.Contains("_lastLogged.Add(signature)", StringComparison.Ordinal)
                   && diagnostics.Contains("ReconcileLoggedDiagnosticsForContract(snapshot.ContractId)", StringComparison.Ordinal)
                   && diagnostics.Contains("HasSnapshotForDiagnostic", StringComparison.Ordinal),
                "native diagnostics publish stable non-secret error descriptions and debounce warnings by contract plus error code");

            Check(binding.Contains("Stopwatch.GetTimestamp", StringComparison.Ordinal)
                  && binding.Contains("_lastReceiveStopwatchTimestamp", StringComparison.Ordinal)
                  && binding.Contains("_lastApplyStopwatchTimestamp", StringComparison.Ordinal)
                  && binding.Contains("Interlocked.Exchange", StringComparison.Ordinal)
                  && hub.Contains("Environment.GetEnvironmentVariable(\"ROS_DISTRO\")", StringComparison.Ordinal)
                  && hub.Contains("Environment.GetEnvironmentVariable(\"RMW_IMPLEMENTATION\")", StringComparison.Ordinal)
                  && (hub.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal)
                      || diagnostics.Contains("rmw_fastrtps_cpp", StringComparison.Ordinal))
                  && (hub.Contains("rmw_zenoh_cpp", StringComparison.Ordinal)
                      || diagnostics.Contains("rmw_zenoh_cpp", StringComparison.Ordinal))
                  && hub.Contains("GetDiagnosticSnapshots", StringComparison.Ordinal)
                  && !binding.Contains("UnityEngine.", StringComparison.Ordinal),
                "native callback diagnostics use managed Stopwatch timestamps and capture active ROS runtime identity only after readiness");

            Check(optionalInspector.Contains(
                      "public static void DrawFoxRunNativeSubscriptionDiagnostics()",
                      StringComparison.Ordinal)
                  && optionalInspector.Contains("FoxRunRos2SubscriptionRuntimeDiagnostics.GetSnapshots", StringComparison.Ordinal)
                  && optionalInspector.Contains("CopyableField", StringComparison.Ordinal)
                  && (hub.Contains("ROS2 Native / FastDDS (DDS)", StringComparison.Ordinal)
                      || diagnostics.Contains("ROS2 Native / FastDDS (DDS)", StringComparison.Ordinal))
                  && (hub.Contains("ROS2 Native / Zenoh", StringComparison.Ordinal)
                      || diagnostics.Contains("ROS2 Native / Zenoh", StringComparison.Ordinal))
                  && coreInspector.Contains(
                      "R2fuNativeSubscriptionDiagnosticsInspectorTypeName",
                      StringComparison.Ordinal)
                   && coreInspector.Contains(
                       "ResolveR2fuNativeSubscriptionDiagnosticsDrawMethod",
                       StringComparison.Ordinal)
                   && coreInspector.Contains(
                       "ForUnity.Native.Editor",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity2Foxglove.Ros2ForUnity.Native",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity.FoxgloveSDK",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "Unity2Foxglove.Ros2ForUnity.Editor",
                       StringComparison.Ordinal)
                   && optionalInspectorAsmdef.Contains(
                       "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                       StringComparison.Ordinal)
                   && subscribeInspector.Contains(
                       "DrawOptionalR2fuNativeSubscriptionDiagnostics();",
                       StringComparison.Ordinal),
                "core Inspector reaches a native-constrained optional diagnostics assembly only through cached reflection and exposes safe topic/type copying");

            Check(runtimeTopicsInspector.Contains("ROS2 Native Unity Contracts", StringComparison.Ordinal)
                  && runtimeTopicsInspector.Contains("DrawFoxRunNativeUnityContracts", StringComparison.Ordinal)
                  && runtimeTopicsInspector.Contains("FoxRunEndpointResolver.Resolve", StringComparison.Ordinal)
                  && runtimeTopicSummary.IndexOf(
                      "DrawFoxRunNativeUnityContracts(manager);",
                      StringComparison.Ordinal)
                     < runtimeTopicSummary.IndexOf(
                         "return;",
                         runtimeTopicSummary.IndexOf("No generated FoxRun topic metadata", StringComparison.Ordinal),
                         StringComparison.Ordinal)
                  && catalog.Contains("IsWebSocketEncoding", StringComparison.Ordinal)
                  && !catalog.Contains("cdr", StringComparison.OrdinalIgnoreCase),
                "native-only Unity contract status stays visible in Runtime Topics without entering the Foxglove subscription catalog");
        }

        private static void VerifyManualOwnershipProbe()
        {
            const string probePath =
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179Ros2OwnershipProbe.cs";
            var probe = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath);
            var probeMeta = PhaseValidationSourceHelpers.ReadRequiredRepoText(probePath + ".meta");
            var diagnostics = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionDiagnostics.cs");
            var binding = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2SubscriptionBinding.cs");
            var slot = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2OwnedLatestSlot.cs");
            var armBurst = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "public void ArmBurstAttempt()");
            var emitBurst = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "private void TryEmitBurstLatestMarker");
            var observe = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "private void ObserveGeneratedOwnedCopy()");
            var attachSessionObserver = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "private void AttachManagerSessionPolicyObserver()");
            var detachSessionObserver = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "private void DetachManagerSessionPolicyObserver()");
            var captureSessionPolicy = PhaseValidationSourceHelpers.SourceMethod(
                probe,
                "private void CaptureManagerSessionPolicy(");
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var probeMetaAbsolute = Path.Combine(
                repoRoot,
                (probePath + ".meta").Replace('/', Path.DirectorySeparatorChar));
            var probeGuid = ReadUnityAssetGuid(probeMetaAbsolute);

            Check(probe.Contains("/foxrun/phase179/string", StringComparison.Ordinal)
                  && probe.Contains("Mode = FoxRunFlow.Subscribe", StringComparison.Ordinal)
                  && probe.Contains(
                      "Source = FoxRunEndpoint.Ros2Native",
                      StringComparison.Ordinal)
                  && probe.Contains("QoS = FoxRunQosProfile.Default", StringComparison.Ordinal)
                  && probe.Contains("private std_msgs.msg.String inputString;", StringComparison.Ordinal)
                  && probe.Contains(
                      "BorrowedLifetimeEvidence => borrowedLifetimeEvidence",
                      StringComparison.Ordinal),
                "manual ownership probe declares the explicit native reliable String contract");

            Check(probe.Contains("[Header(\"Captured Subscription Session Policy\")]", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private bool capturedSessionEnabled;", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private ulong capturedSessionGeneration;", StringComparison.Ordinal)
                  && probe.Contains(
                      "[SerializeField] private FoxRunEndpoint capturedDefaultSubscriptionSource",
                      StringComparison.Ordinal)
                  && probe.Contains(
                      "[SerializeField] private FoxRunEncoding capturedFoxgloveEncoding",
                      StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunQosProfile capturedQosProfile", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunQosReliability capturedQosReliability", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunQosDurability capturedQosDurability", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private FoxRunQosHistory capturedQosHistory", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private int capturedQosDepth", StringComparison.Ordinal)
                  && probe.Contains("[SerializeField] private int capturedNativeCopyBudgetBytes", StringComparison.Ordinal)
                  && probe.Contains("FoxRunSubscriptionSessionChanged +=", StringComparison.Ordinal)
                  && probe.Contains("FoxRunSubscriptionSessionChanged -=", StringComparison.Ordinal)
                  && OccursBefore(
                      PhaseValidationSourceHelpers.SourceMethod(probe, "private void OnEnable()"),
                      "AttachManagerSessionPolicyObserver();",
                      "#if UNITY2FOXGLOVE_ROS2_FOR_UNITY")
                  && PhaseValidationSourceHelpers.SourceMethod(probe, "private void OnDisable()")
                      .Contains("DetachManagerSessionPolicyObserver();", StringComparison.Ordinal)
                  && attachSessionObserver.Contains(
                      "ActiveFoxRunSubscriptionSessionPolicy",
                      StringComparison.Ordinal)
                  && !attachSessionObserver.Contains("Find", StringComparison.Ordinal)
                  && detachSessionObserver.Contains(
                      "FoxRunSubscriptionSessionChanged -=",
                      StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.SubscriptionsEnabled", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.SessionGeneration", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultSource", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.FoxgloveEncoding", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultRos2Qos.Profile", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultRos2Qos.Reliability", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultRos2Qos.Durability", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultRos2Qos.History", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.DefaultRos2Qos.Depth", StringComparison.Ordinal)
                  && captureSessionPolicy.Contains("policy.NativeCopyBudgetBytes", StringComparison.Ordinal)
                  && !captureSessionPolicy.Contains("Create", StringComparison.Ordinal)
                  && !captureSessionPolicy.Contains("Selector", StringComparison.Ordinal),
                "manual ownership probe captures immutable Manager session policy through symmetric event observation only");

            Check(probe.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && probe.Contains("ROS2 native subscription support is unavailable", StringComparison.Ordinal)
                  && probe.Contains("nextFrameReadableCount", StringComparison.Ordinal)
                  && probe.Contains("bindingReceivedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingReplacedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingAppliedCount", StringComparison.Ordinal)
                  && probe.Contains("bindingPendingCount", StringComparison.Ordinal)
                  && probe.Contains("disableNoApplyPassCount", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_APPLIED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_NEXT_FRAME_READABLE", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_BURST_ARMED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_BURST_LATEST", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_DISABLE_ARMED", StringComparison.Ordinal)
                  && probe.Contains("PHASE179_ROS2_OWNERSHIP_DISABLE_CLEAN", StringComparison.Ordinal),
                "manual ownership probe exposes bounded copied-value, latest-wins, and disable-clean evidence");

            Check(diagnostics.Contains(
                      "public readonly struct FoxRunRos2SubscriptionAcceptanceSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public readonly struct FoxRunRos2AcceptanceAttemptSnapshot",
                      StringComparison.Ordinal)
                  && diagnostics.Contains(
                      "public static class FoxRunRos2SubscriptionAcceptanceDiagnostics",
                      StringComparison.Ordinal)
                  && diagnostics.Contains("public static FoxRunRos2AcceptanceArmStatus ArmAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool TryGetAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool EndAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("public static bool TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && diagnostics.Contains("IsSingleApplyLatestWinsComplete", StringComparison.Ordinal)
                  && diagnostics.Contains("Received == Replaced + Applied", StringComparison.Ordinal)
                  && probe.Contains(
                      "FoxRunRos2SubscriptionAcceptanceDiagnostics.TryGet",
                      StringComparison.Ordinal)
                  && probe.Contains("bindingPendingCount > 0", StringComparison.Ordinal)
                  && probe.Contains("_disablePendingArmed", StringComparison.Ordinal),
                "managed diagnostics expose cumulative cleanup evidence and an explicit exact acceptance attempt");

            Check(armBurst.Contains(
                      "FoxRunRos2SubscriptionAcceptanceDiagnostics.ArmAttempt",
                      StringComparison.Ordinal)
                  && armBurst.Contains("FoxRunRos2AcceptanceArmStatus.Armed", StringComparison.Ordinal)
                  && armBurst.Contains("CopyAttemptSnapshot", StringComparison.Ordinal)
                  && emitBurst.Contains("!burstArmed", StringComparison.Ordinal)
                  && emitBurst.Contains("SessionMatchesArmedToken", StringComparison.Ordinal)
                  && probe.Contains("TryCompleteArmedBurstAttempt", StringComparison.Ordinal)
                  && probe.Contains("TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && probe.Contains("_latestAttemptSnapshot.Epoch != completedEpoch", StringComparison.Ordinal)
                  && probe.Contains("IsSingleApplyLatestWinsComplete", StringComparison.Ordinal)
                  && !emitBurst.Contains("TryGetAttempt", StringComparison.Ordinal)
                  && !emitBurst.Contains("EndAttempt", StringComparison.Ordinal)
                  && probe.Contains("finally", StringComparison.Ordinal)
                  && probe.Contains("FoxRunRos2SubscriptionAcceptanceDiagnostics.EndAttempt", StringComparison.Ordinal)
                  && !probe.Contains("Burst Baseline", StringComparison.Ordinal)
                  && !probe.Contains("_burstBaseline", StringComparison.Ordinal)
                  && !probe.Contains("HasPositiveBurstProgressFrom", StringComparison.Ordinal)
                  && !probe.Contains("receivedDelta=", StringComparison.Ordinal)
                  && !probe.Contains("inferredReplaced", StringComparison.OrdinalIgnoreCase),
                "manual burst acceptance arms an idle epoch and requires exact one-apply latest-wins accounting for the matching token");

            Check(binding.Contains("private long _acceptanceAdmission", StringComparison.Ordinal)
                  && binding.Contains("_slot.PendingCount != 0", StringComparison.Ordinal)
                  && binding.Contains("_acceptanceCallbacksInFlight", StringComparison.Ordinal)
                  && binding.Contains("AcceptanceCompleting", StringComparison.Ordinal)
                  && binding.Contains("AcceptanceCompleted", StringComparison.Ordinal)
                  && binding.Contains("TryCompleteAcceptanceAttempt", StringComparison.Ordinal)
                  && binding.Contains("var acceptanceEpoch = Volatile.Read(ref _acceptanceAdmission)", StringComparison.Ordinal)
                  && binding.Contains("out var replacedPending", StringComparison.Ordinal)
                  && binding.Contains("Interlocked.Increment(ref _acceptanceReplaced)", StringComparison.Ordinal)
                  && slot.Contains("out bool replacedPending", StringComparison.Ordinal)
                  && slot.Contains("replacedPending = true", StringComparison.Ordinal),
                "acceptance admission excludes pending or in-flight history and counts actual pending replacement from callback entry");

            Check(probe.Contains("private int _lastObservedLength", StringComparison.Ordinal)
                  && probe.Contains("private ulong _lastObservedFingerprint", StringComparison.Ordinal)
                  && probe.Contains("private int _pendingNextFrameLength", StringComparison.Ordinal)
                  && probe.Contains("private ulong _pendingNextFrameFingerprint", StringComparison.Ordinal)
                  && !probe.Contains("_lastObservedValue", StringComparison.Ordinal)
                  && !probe.Contains("_pendingNextFrameValue", StringComparison.Ordinal)
                  && OccursBefore(
                      observe,
                      "if (_burstCompletionPending)",
                      "currentFingerprint == _lastObservedFingerprint")
                  && OccursBefore(observe, "currentFingerprint == _lastObservedFingerprint", "TryParseBurstValue(")
                  && probe.Contains("private bool CanEmitMarker", StringComparison.Ordinal)
                  && probe.Contains("!_evidenceComplete", StringComparison.Ordinal)
                  && CountOccurrences(probe, "if (CanEmitMarker)") >= 6,
                "manual probe retains only bounded Inspector strings, fingerprints unchanged values, and stops constructing capped evidence markers");

            Check(!string.IsNullOrEmpty(probeGuid)
                  && probeMeta.Contains("MonoImporter:", StringComparison.Ordinal)
                  && CountUnityAssetGuidOccurrences(repoRoot, probeGuid) == 1,
                "manual ownership probe metadata keeps one unique 32-hex Unity GUID and a complete MonoImporter block");

            Check(!probe.Contains("CreateSubscription", StringComparison.Ordinal)
                  && !probe.Contains("CreateNode", StringComparison.Ordinal)
                  && !probe.Contains("ROS2Node", StringComparison.Ordinal)
                  && !probe.Contains("ISubscription", StringComparison.Ordinal)
                  && !probe.Contains("OnRos2", StringComparison.Ordinal)
                  && !probe.Contains("rawCallback", StringComparison.OrdinalIgnoreCase)
                  && !probe.Contains("Debug.Log(inbound", StringComparison.Ordinal)
                  && probe.Contains("MaximumMarkersPerEnable", StringComparison.Ordinal),
                "manual ownership probe uses only the generated host and bounds diagnostics without retaining callback objects");
        }

        private static void VerifyInteropAcceptanceSurface()
        {
            var sample = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs");
            var sampleReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md");
            var importedSampleReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/FoxRun ROS2 Native Subscribe/README.md");
            var packageReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/README.md");
            var packageJson = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/package.json");
            var acceptance = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs");
            var playerBuilder = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs");

            Check(sample.Contains("std_msgs.msg.String", StringComparison.Ordinal)
                  && sample.Contains("geometry_msgs.msg.Twist", StringComparison.Ordinal)
                  && sample.Contains("sensor_msgs.msg.Joy", StringComparison.Ordinal)
                  && sample.Contains("sensor_msgs.msg.Imu", StringComparison.Ordinal)
                  && sample.Contains("Source = FoxRunEndpoint.Ros2Native", StringComparison.Ordinal)
                  && sample.Contains("QoS = FoxRunQosProfile.Default", StringComparison.Ordinal)
                  && sample.Contains("QoS = FoxRunQosProfile.SensorData", StringComparison.Ordinal)
                  && sample.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal)
                  && sample.Contains("CopyBounded", StringComparison.Ordinal)
                  && packageJson.Contains("FoxRun ROS2 Native Subscribe", StringComparison.Ordinal),
                "public sample exposes four existing typed native Subscribe contracts with bounded Inspector copies");

            Check(sampleReadme.Contains("phase179_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_jazzy_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_lyrical_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("phase179_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && sampleReadme.Contains("ros2-windows/ros2_<distro>", StringComparison.Ordinal)
                  && sampleReadme.Contains("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", StringComparison.Ordinal)
                  && sampleReadme.Contains("ZENOH_SESSION_CONFIG_URI", StringComparison.Ordinal)
                  && sampleReadme.Contains("first `Ok()`", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_humble_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_jazzy_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_lyrical_fastrtps_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("phase179_lyrical_zenoh_acceptance.py", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("ros2-windows/ros2_<distro>", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("ZENOH_SESSION_CONFIG_URI", StringComparison.Ordinal)
                  && importedSampleReadme.Contains("first `Ok()`", StringComparison.Ordinal),
                "source and imported native-subscribe samples document the four named Linux interop profiles, pending half-evidence, repo-local Windows CLI, and Zenoh first-initialization boundary");

            Check(playerBuilder.Contains("PackageAssetPathToAbsolutePath", StringComparison.Ordinal)
                  && playerBuilder.Contains("ProjectAssetPathToAbsolutePath", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Path.Combine(RepositoryRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar))",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "Path.Combine(ProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar))",
                      StringComparison.Ordinal),
                "sample metadata generator resolves package sources from the repository root and temporary Assets staging from the Unity project root");

            Check(playerBuilder.Contains("CleanupStaleSampleMetadataStaging", StringComparison.Ordinal)
                  && playerBuilder.Contains("IsRecognizedSampleMetadataStaging", StringComparison.Ordinal)
                  && playerBuilder.Contains("Unexpected Phase179 metadata staging content", StringComparison.Ordinal)
                  && OccursBefore(
                      playerBuilder,
                      "CleanupStaleSampleMetadataStaging();",
                      "Directory.CreateDirectory(stagingSampleAbsolutePath)"),
                "sample metadata generator recovers only its recognized stale staging root before retrying Unity generation");

            Check(playerBuilder.Contains("SampleMetadataStagingAssetPath", StringComparison.Ordinal)
                  && playerBuilder.Contains("FileUtil.CopyFileOrDirectory", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "AssetDatabase.DeleteAsset(SampleMetadataStagingAssetPath)",
                      StringComparison.Ordinal)
                  && !playerBuilder.Contains("AssetDatabase.ImportAsset(assetPath", StringComparison.Ordinal),
                "sample metadata generator stages source files under Assets so Unity generates sidecars before package copy-back");

            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var phase179AssetPaths = new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs",
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md",
                "Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity",
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs",
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs",
            };
            var phase179MetadataPaths = phase179AssetPaths
                .Select(path => Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)) + ".meta")
                .ToArray();
            var phase179ScriptMetadataPaths = new[]
            {
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs.meta",
                "Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase179AcceptancePlayerBuilder.cs.meta",
                "Unity2Foxglove/Assets/Samples/Unity2Foxglove ROS2 For Unity/0.1.0-preview.1/FoxRun ROS2 Native Subscribe/Phase179FoxRunRos2NativeSubscribe.cs.meta",
                "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs.meta",
            }
                .Select(path => Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            var phase179Guids = phase179MetadataPaths
                .Where(File.Exists)
                .Select(ReadUnityAssetGuid)
                .ToArray();
            Check(phase179Guids.Length == phase179MetadataPaths.Length
                  && phase179Guids.All(guid => !string.IsNullOrEmpty(guid))
                  && phase179Guids.Distinct(StringComparer.OrdinalIgnoreCase).Count() == phase179Guids.Length
                  && phase179Guids.All(guid => CountUnityAssetGuidOccurrences(repoRoot, guid) == 1)
                  && phase179ScriptMetadataPaths.All(HasCompleteUnityScriptMetadata),
                "Unity-generated Phase179 sample, scene, and manual-acceptance metadata use valid unique GUIDs and complete script importers");

            var refreshReadyMarker = PhaseValidationSourceHelpers.SourceMethod(
                acceptance,
                "private void EmitReadyMarkerWhenRuntimeIsObserved()");
            Check(refreshReadyMarker.Contains(
                      "snapshot.State != FoxRunRos2SubscriptionBindingState.Ready",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "snapshot.State != FoxRunRos2SubscriptionBindingState.Receiving",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains("_readyMarkerEmitted = false;", StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "Waiting for native ROS2 String subscription registration.",
                      StringComparison.Ordinal)
                  && refreshReadyMarker.Contains(
                      "Native ROS2 String subscription registered: ",
                      StringComparison.Ordinal),
                "acceptance READY evidence requires a live String binding and clears a stale ready state when that binding disappears");

            Check(acceptance.Contains("PHASE179_ROS2_INBOUND_READY", StringComparison.Ordinal)
                  && acceptance.Contains("PHASE179_ROS2_INBOUND_APPLIED", StringComparison.Ordinal)
                  && acceptance.Contains("PHASE179_ROS2_INBOUND_COMPLETE", StringComparison.Ordinal)
                  && acceptance.Contains("value=", StringComparison.Ordinal)
                  && acceptance.Contains("received=", StringComparison.Ordinal)
                  && acceptance.Contains("replaced=", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-player-auto-quit", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-token", StringComparison.Ordinal)
                  && acceptance.Contains("--phase179-player-burst-final-sequence", StringComparison.Ordinal)
                  && acceptance.Contains("Application.Quit(exitCode)", StringComparison.Ordinal)
                  && acceptance.Contains("TryParseBurstValue", StringComparison.Ordinal)
                  && acceptance.Contains("MaximumMarkerTextLength", StringComparison.Ordinal)
                  && acceptance.Contains("CopySafeText", StringComparison.Ordinal)
                  && acceptance.Contains("ContainsSensitiveMarkerText", StringComparison.Ordinal)
                  && acceptance.Contains("sequence == total - 1", StringComparison.Ordinal)
                  && acceptance.Contains("private string _activeCorrelationToken", StringComparison.Ordinal)
                  && acceptance.Contains("CaptureCorrelationToken(ExtractCorrelationToken(data))", StringComparison.Ordinal)
                   && acceptance.Contains("CaptureCorrelationToken(frameId)", StringComparison.Ordinal)
                   && acceptance.Contains("IsSafeCorrelationToken(_activeCorrelationToken)", StringComparison.Ordinal)
                   && acceptance.Contains("private string _readyRunToken = string.Empty;", StringComparison.Ordinal)
                   && acceptance.Contains("_readyRunToken = Guid.NewGuid().ToString(\"N\");", StringComparison.Ordinal)
                   && acceptance.Contains("PlayerOrFallbackToken(_readyRunToken)", StringComparison.Ordinal)
                   && acceptance.Contains("+ \"|\" + markerToken + \"|\" + valueJson", StringComparison.Ordinal)
                   && !acceptance.Contains("CreateSubscription", StringComparison.Ordinal)
                   && !acceptance.Contains("CreateNode", StringComparison.Ordinal),
                "acceptance receiver emits bounded redacted correlation markers, uses a new READY token for each activation, carries a current peer token to tokenless Twist evidence, preserves a terminal latest-wins burst value, and auto-quits only from its main-thread surface");

            Check(playerBuilder.Contains("NewSceneSetup.EmptyScene, NewSceneMode.Additive", StringComparison.Ordinal)
                  && playerBuilder.Contains("Refusing to overwrite", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "SceneManager.MoveGameObjectToScene(managerObject, acceptanceScene)",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "SceneManager.MoveGameObjectToScene(receiverObject, acceptanceScene)",
                      StringComparison.Ordinal)
                  && !playerBuilder.Contains(
                      "SceneManager.SetActiveScene(acceptanceScene)",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains("_foxgloveOutputEnabled", StringComparison.Ordinal)
                  && playerBuilder.Contains("_ros2NativeEnabled", StringComparison.Ordinal)
                  && playerBuilder.Contains("_enableFoxRunInbound", StringComparison.Ordinal)
                  && playerBuilder.Contains("Ros2Native", StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "manager.DefaultFoxRunSubscriptionSource = FoxRunEndpoint.Ros2Native",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains(
                      "UnityEditor.PackageManager.PackageInfo.FindForPackageName",
                      StringComparison.Ordinal)
                  && playerBuilder.Contains("RuntimeSupport", StringComparison.Ordinal)
                  && playerBuilder.Contains("SceneManager.GetSceneByPath", StringComparison.Ordinal)
                  && playerBuilder.Contains("FindComponentsInScene", StringComparison.Ordinal)
                  && playerBuilder.Contains("AssetDatabase.AssetPathToGUID", StringComparison.Ordinal)
                  && playerBuilder.Contains("GenerateSampleMetadata", StringComparison.Ordinal)
                  && playerBuilder.Contains("SampleMetadataAssetPaths", StringComparison.Ordinal)
                  && playerBuilder.Contains("ValidateSampleMetadata", StringComparison.Ordinal)
                  && playerBuilder.Contains("build", StringComparison.Ordinal)
                  && playerBuilder.Contains("Phase179FoxRunRos2NativeSubscribe.exe", StringComparison.Ordinal),
                "tracked scene builder fails closed, obtains Unity-generated sample metadata, verifies the actual acceptance scene, records one resolved runtime, and keeps Player output under repository build paths");

            Check(sampleReadme.Contains("FOXRUN212", StringComparison.Ordinal)
                  && sampleReadme.Contains("Foxglove Desktop is unrelated", StringComparison.Ordinal)
                  && sampleReadme.Contains(
                      "Generated custom DTO native Publish, Subscribe, and diagnostic full duplex",
                      StringComparison.Ordinal)
                  && sampleReadme.Contains("Subscribe-only `FoxRunStream<T>`", StringComparison.Ordinal)
                  && sampleReadme.Contains("4 MiB", StringComparison.Ordinal)
                  && sampleReadme.Contains("1 KiB", StringComparison.Ordinal)
                  && sampleReadme.Contains("CopyFailed", StringComparison.Ordinal)
                  && packageReadme.Contains("WindowsStandalone64", StringComparison.Ordinal)
                  && packageReadme.Contains("ROS domain IDs are discovery isolation, not authentication", StringComparison.Ordinal)
                  && packageReadme.Contains("FOXRUN212", StringComparison.Ordinal)
                  && packageReadme.Contains("4 MiB", StringComparison.Ordinal)
                  && packageReadme.Contains("CopyFailed", StringComparison.Ordinal),
                "public native-subscribe documentation states assembly, transport, copy-budget, security, Player, custom-message, and stream boundaries");
        }

        private static void VerifyPermanentOptionalCompilationCiGates()
        {
            var localCi = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Scripts/release/run_ci.py");
            var workflow = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                ".github/workflows/dotnet-tests.yml");
            const string inboundAcceptanceRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_inbound_acceptance";
            const string playerHostRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_player_host";
            const string matrixProfilesRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_matrix_profiles";
            const string zenohTopologyRegression =
                "Scripts.smoke.ros2.regression_checks.test_phase179_zenoh_topology";

            Check(localCi.Contains("UNIT_ADAPTER_TEST_PROPS", StringComparison.Ordinal)
                  && localCi.Contains("UNIT_NATIVE_TEST_PROPS", StringComparison.Ordinal)
                  && localCi.Contains("-p:IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && localCi.Contains("-p:IncludeRos2ForUnityNative=true", StringComparison.Ordinal),
                "local CI runs dedicated optional adapter and Native compile/test lanes");

            Check(workflow.Contains("optional-ros2-adapter:", StringComparison.Ordinal)
                  && workflow.Contains("name: optional ROS2 adapter gate", StringComparison.Ordinal)
                  && workflow.Contains("optional-ros2-native:", StringComparison.Ordinal)
                  && workflow.Contains("name: optional ROS2 Native gate", StringComparison.Ordinal)
                  && workflow.Contains("-p:IncludeRos2ForUnityAdapter=true", StringComparison.Ordinal)
                  && workflow.Contains("-p:IncludeRos2ForUnityNative=true", StringComparison.Ordinal),
                "GitHub Actions keeps the default test check and names permanent optional adapter and Native gates");

            Check(localCi.Contains("phase179-ros2-regression", StringComparison.Ordinal)
                  && localCi.Contains(inboundAcceptanceRegression, StringComparison.Ordinal)
                  && localCi.Contains(playerHostRegression, StringComparison.Ordinal)
                  && localCi.Contains(matrixProfilesRegression, StringComparison.Ordinal)
                  && localCi.Contains(zenohTopologyRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + inboundAcceptanceRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + playerHostRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + matrixProfilesRegression, StringComparison.Ordinal)
                  && workflow.Contains("python3 -m unittest " + zenohTopologyRegression, StringComparison.Ordinal),
                "local and GitHub CI run the pure Phase179 Linux, Windows, profile, and Zenoh helper regression modules");
        }

        private static void VerifyRegistryAndProjectWiring()
        {
            var entries = PhaseValidationRegistry.All
                .Where(item => string.Equals(item.Flag, "--phase179", StringComparison.Ordinal))
                .ToArray();
            Check(entries.Length == 1
                  && entries[0].Name == "FoxRun native ROS2 subscription boundary"
                  && entries[0].Category == ValidationCategory.CiSafe
                  && entries[0].Evidence == (ValidationEvidence.Behavior | ValidationEvidence.Structural)
                  && entries[0].IncludeInDefault
                  && entries[0].Run.Method.DeclaringType == typeof(FoxRunRos2NativeSubscriptionValidation),
                "native subscription registry entry is singular, behavior-and-structural, CI-safe, and included in default");

            var registry = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                CorePackageRoot + "/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var meta = PhaseValidationSourceHelpers.ReadRequiredRepoText(ValidationMeta);
            var guidLine = meta
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .SingleOrDefault(line => line.StartsWith("guid: ", StringComparison.Ordinal));
            var guid = guidLine?.Substring("guid: ".Length) ?? string.Empty;
            var repoRoot = PhaseValidationSourceHelpers.FindRequiredRepoRoot();
            var numericValidationPath = Path.Combine(
                repoRoot,
                CorePackageRoot.Replace('/', Path.DirectorySeparatorChar),
                "Tests",
                "Runtime",
                "Phase179Validation.cs");

            Check(CountOccurrences(registry, "\"--phase179\"") == 1
                  && CountOccurrences(
                      project,
                      "<Compile Include=\"FoxRunRos2NativeSubscriptionValidation.cs\" />") == 1
                  && project.Contains("FoxRunRos2NativeSubscriptionValidation.cs", StringComparison.Ordinal)
                  && !project.Contains("Phase179Validation.cs", StringComparison.Ordinal)
                  && !File.Exists(numericValidationPath)
                  && guid.Length == 32
                  && guid.All(Uri.IsHexDigit)
                  && meta.Contains("MonoImporter:", StringComparison.Ordinal),
                "descriptive validator source, project wiring, and Unity metadata remain singular and valid");
        }

        private static IEnumerable<string> ReadAsmdefReferences(string path)
        {
            return ReadAsmdefReferences(JObject.Parse(File.ReadAllText(path)));
        }

        private static IEnumerable<string> ReadAsmdefReferences(JObject root)
        {
            return new[] { "references", "precompiledReferences" }
                .SelectMany(property => (root[property] as JArray ?? new JArray()).Values<string>())
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToArray();
        }

        private static IEnumerable<string> ReadProjectDependencyReferences(string path)
        {
            return ReadProjectDependencyReferences(XDocument.Load(path));
        }

        private static IEnumerable<string> ReadProjectDependencyReferences(XDocument document)
        {
            return document
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference"
                                  || element.Name.LocalName == "PackageReference"
                                  || element.Name.LocalName == "Reference")
                .Select(element => (string)element.Attribute("Include"))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToArray();
        }

        private static IEnumerable<string> ReadPackageDependencyReferences(JObject package)
        {
            if (!(package["dependencies"] is JObject dependencies))
                return Array.Empty<string>();

            var references = new List<string>();
            foreach (var property in dependencies.Properties())
            {
                references.Add(property.Name);
                if (property.Value.Type == JTokenType.String)
                    references.Add(property.Value.Value<string>());
            }

            return references;
        }

        private static IEnumerable<string> EnumerateSourceDefinitionFiles(
            string root,
            string searchPattern)
        {
            return Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
                .Where(path => !IsBuildArtifactPath(path));
        }

        private static bool IsBuildArtifactPath(string path)
        {
            return path
                .Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTestProjectPath(string coreRoot, string path)
        {
            var relative = Path.GetRelativePath(coreRoot, path)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return relative.StartsWith("Tests" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyDictionary<string, AsmdefIdentity> BuildAsmdefGuidIndex(
            string repoRoot)
        {
            var packagesRoot = Path.Combine(repoRoot, "Packages");
            var index = new Dictionary<string, AsmdefIdentity>(StringComparer.OrdinalIgnoreCase);
            foreach (var asmdefPath in EnumerateSourceDefinitionFiles(packagesRoot, "*.asmdef")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var metaPath = asmdefPath + ".meta";
                if (!File.Exists(metaPath))
                    continue;

                var guid = ReadUnityAssetGuid(metaPath);
                if (string.IsNullOrEmpty(guid))
                    continue;

                var root = JObject.Parse(File.ReadAllText(asmdefPath));
                var identity = new AsmdefIdentity(
                    Path.GetRelativePath(repoRoot, asmdefPath).Replace('\\', '/'),
                    root.Value<string>("name") ?? string.Empty);
                if (index.TryGetValue(guid, out var existing))
                {
                    throw new InvalidOperationException(
                        "Duplicate asmdef GUID " + guid + " maps to both "
                        + existing.RelativePath + " and " + identity.RelativePath + ".");
                }

                index.Add(guid, identity);
            }

            return index;
        }

        private static string ReadUnityAssetGuid(string metaPath)
        {
            var prefix = "guid: ";
            var line = File.ReadLines(metaPath)
                .FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            var guid = line?.Substring(prefix.Length).Trim() ?? string.Empty;
            return guid.Length == 32 && guid.All(Uri.IsHexDigit) ? guid : string.Empty;
        }

        private static bool HasDiagnosticDescriptorId(string source, string descriptorName, string id)
        {
            var marker = "public static readonly DiagnosticDescriptor " + descriptorName
                         + " = new DiagnosticDescriptor(";
            var start = source.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return false;

            var next = source.IndexOf(
                "public static readonly DiagnosticDescriptor ",
                start + marker.Length,
                StringComparison.Ordinal);
            var descriptor = next < 0 ? source.Substring(start) : source.Substring(start, next - start);
            return descriptor.Contains("\"" + id + "\"", StringComparison.Ordinal);
        }

        private static bool HasReleaseTrackingRow(string releaseTracking, string id, string note)
        {
            return releaseTracking
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Any(line => line.StartsWith(id + " | FoxRun |", StringComparison.Ordinal)
                             && line.Contains(note, StringComparison.Ordinal));
        }

        private static bool HasReservedBeforeReleaseRow(string releaseTracking, string id, string note)
        {
            return releaseTracking
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Any(line => line.StartsWith("; " + id + " | FoxRun |", StringComparison.Ordinal)
                             && line.Contains(note, StringComparison.Ordinal));
        }

        private static bool HasCompleteUnityScriptMetadata(string metaPath)
        {
            if (!File.Exists(metaPath))
                return false;

            var metadata = File.ReadAllText(metaPath).Replace("\r\n", "\n");
            return metadata.EndsWith("\n", StringComparison.Ordinal)
                   && metadata.Contains(
                       "MonoImporter:\n"
                       + "  externalObjects: {}\n"
                       + "  serializedVersion: 2\n"
                       + "  defaultReferences: []\n"
                       + "  executionOrder: 0\n"
                       + "  icon: {instanceID: 0}\n"
                       + "  userData:\n"
                       + "  assetBundleName:\n"
                       + "  assetBundleVariant:\n",
                       StringComparison.Ordinal);
        }

        private static int CountUnityAssetGuidOccurrences(string repoRoot, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return 0;

            return new[]
                {
                    Path.Combine(repoRoot, "Packages"),
                    Path.Combine(repoRoot, "Unity2Foxglove", "Assets"),
                }
                .Where(Directory.Exists)
                .SelectMany(root => EnumerateSourceDefinitionFiles(root, "*.meta"))
                .Count(path => string.Equals(
                    ReadUnityAssetGuid(path),
                    guid,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOptionalAsmdefReference(
            string reference,
            IReadOnlyDictionary<string, AsmdefIdentity> guidIndex)
        {
            if (IsOptionalNativeDependency(reference))
                return true;

            return TryResolveAsmdefReference(reference, guidIndex, out var target)
                   && (IsOptionalNativeDependency(target.RelativePath)
                       || IsOptionalNativeDependency(target.Name));
        }

        private static bool TryResolveAsmdefReference(
            string reference,
            IReadOnlyDictionary<string, AsmdefIdentity> guidIndex,
            out AsmdefIdentity identity)
        {
            identity = null;
            const string prefix = "GUID:";
            if (string.IsNullOrWhiteSpace(reference)
                || !reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var guid = reference.Substring(prefix.Length).Trim();
            return guid.Length == 32
                   && guid.All(Uri.IsHexDigit)
                   && guidIndex.TryGetValue(guid, out identity);
        }

        private static bool IsOptionalNativeDependency(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return false;

            var normalized = reference.Replace('\\', '/').Trim();
            return normalized.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("dev.unity2foxglove.ros2forunity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("Ros2ForUnity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("ros2-for-unity", StringComparison.OrdinalIgnoreCase)
                   || normalized.Contains("r2fu", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "ROS2", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("ROS2.", StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith("ROS2,", StringComparison.OrdinalIgnoreCase);
        }

        private static bool OccursBefore(string source, string first, string second)
        {
            var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
            var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
            return firstIndex >= 0 && secondIndex > firstIndex;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }

        private sealed class AsmdefIdentity
        {
            public AsmdefIdentity(string relativePath, string name)
            {
                RelativePath = relativePath;
                Name = name;
            }

            public string RelativePath { get; }
            public string Name { get; }
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
