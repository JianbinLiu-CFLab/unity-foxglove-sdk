// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase184 profile-model, routed-behavior, and repository-hygiene evidence.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Descriptively named Phase184 evidence. The five public entry points keep
    /// phase selections independently runnable without reviving a generic
    /// Phase184Validation filename.
    /// </summary>
    public static class FoxRunProfileModelValidation
    {
        private static readonly Regex LegacyNamedArgument = new Regex(
            @"\b(?:PublishMode|RateHz|ChangeEpsilon|ForceIntervalSeconds|When|Unless|Ros2Qos)\s*=",
            RegexOptions.CultureInvariant);

        private static readonly Regex LegacyNamedValue = new Regex(
            @"\b(?:Mode\s*=\s*(?:(?:FoxRunMode|FoxRunFlow)\s*\.\s*)?(?:PublishOnly|SubscribeOnly)|"
            + @"Policy\s*=\s*(?:(?:FoxRunPublishMode|FoxRunPolicy)\s*\.\s*)?(?:OnChange|OnTrigger|ChangeOrInterval))\b",
            RegexOptions.CultureInvariant);

        private static readonly Regex LegacyTypeReference = new Regex(
            @"\b(?:FoxRunMode|FoxRunPublishMode|FoxRunSubscriptionProvider|FoxRunWireEncoding|"
            + @"FoxRunRos2QosPreset|Ros2BridgeQosProfile)\b",
            RegexOptions.CultureInvariant);

        private static readonly Regex FoxRunAttributeReference = new Regex(
            @"(?<![A-Za-z0-9_])(?:(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*)?"
            + @"(?:FoxRun(?:Attribute)?|FoxRunMessage(?:Attribute)?)\s*(?:\(|(?=[,\]]))",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ForbiddenArtifactDirectories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "build",
                "bin",
                "obj",
                "Library",
                "Temp",
                "Logs",
                "node_modules",
                "__pycache__",
                ".cache",
                ".gradle",
                ".nuget"
            };

        private static int _passed;

        public static void ValidatePhase184A()
        {
            Begin("Phase 184A: clean declaration API and legality matrix");
            VerifyCleanDeclarationApi();
            VerifyEndpointLegalityMatrix();
            VerifyLegacyAttributeSyntaxGuardCoverage();
            VerifyMaintainedTextScope();
            VerifyLegacyAttributeSyntaxGuard();
            VerifyPhase184EvidenceClassification();
            VerifyPublicDocumentationContract();
            End("Phase 184A");
        }

        public static void ValidatePhase184B()
        {
            Begin("Phase 184B: frozen directional FoxRun profiles");
            VerifyDirectionalProfileFreeze();
            VerifyExplicitTargetsReplaceOnlyThePublishProfile();
            VerifyIndependentEndpointInheritanceAndProfileFailures();
            End("Phase 184B");
        }

        public static void ValidatePhase184C()
        {
            Begin("Phase 184C: portable ROS 2 QoS resolution");
            VerifyPortableQosResolution();
            End("Phase 184C");
        }

        public static void ValidatePhase184D()
        {
            Begin("Phase 184D: conditional input, fanout, and origin governance");
            VerifySubscribeOnlyIfStaleClearBehavior();
            VerifyPublishConditionAndToleranceBehavior();
            VerifyFanoutFailureIsolation();
            VerifyOriginGovernance();
            End("Phase 184D");
        }

        public static void ValidatePhase184E()
        {
            Begin("Phase 184E: bounded FoxRun input streams and package hygiene");
            VerifyBoundedStreamBehavior();
            VerifyNoPackageTestOrSampleArtifactDirectories();
            End("Phase 184E");
        }

        private static void VerifyCleanDeclarationApi()
        {
            var fieldProperties = PublicPropertyNames(typeof(FoxRunAttribute));
            var aggregateProperties = PublicPropertyNames(typeof(FoxRunMessageAttribute));
            var expectedFieldProperties = new HashSet<string>(
                new[]
                {
                    "Topic", "SchemaName", "ProtobufFieldNumber",
                    "Mode", "Policy", "Hz", "Tolerance", "OnlyIf",
                    "Source", "Targets", "Encoding", "QoS",
                    "Reliability", "Durability", "History", "Depth"
                },
                StringComparer.Ordinal);
            var expectedAggregateProperties = new HashSet<string>(
                new[]
                {
                    "Topic", "SchemaName", "Policy", "Hz", "Tolerance", "OnlyIf",
                    "Targets", "Encoding", "QoS",
                    "Reliability", "Durability", "History", "Depth"
                },
                StringComparer.Ordinal);
            var removed = new[]
            {
                "PublishMode",
                "RateHz",
                "ChangeEpsilon",
                "ForceIntervalSeconds",
                "When",
                "Unless",
                "Ros2Qos"
            };

            Check(
                fieldProperties.SetEquals(expectedFieldProperties)
                && aggregateProperties.SetEquals(expectedAggregateProperties)
                && removed.All(name => !fieldProperties.Contains(name)
                                       && !aggregateProperties.Contains(name)),
                "Structural 184A-1: FoxRun attributes expose only the approved topic, schema, scheduling, endpoint, encoding, and official QoS grammar");

            var assembly = typeof(FoxRunAttribute).Assembly;
            Check(
                assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunMode", false) == null
                && assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunPublishMode", false) == null
                && assembly.GetType("Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset", false) == null
                && Enum.GetNames(typeof(FoxRunPolicy)).SequenceEqual(
                    new[] { "FixedRate", "Change", "Trigger" })
                && Convert.ToInt32(FoxRunPolicy.Trigger) == 4,
                "Structural 184A-2: removed compatibility types stay absent and the retired policy slot remains unexposed");
        }

        private static void VerifyEndpointLegalityMatrix()
        {
            var valid = ResolveEndpoints(
                FoxRunFlow.PublishAndSubscribe,
                FoxRunEndpoint.Ros2Native,
                hasSource: true,
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge,
                hasTargets: true,
                FoxRunEncoding.Protobuf,
                hasEncoding: true,
                hasQos: true);
            var publishSource = ResolveEndpoints(
                FoxRunFlow.Publish,
                FoxRunEndpoint.Foxglove,
                hasSource: true,
                0,
                hasTargets: false,
                0,
                hasEncoding: false,
                hasQos: false);
            var subscribeTargets = ResolveEndpoints(
                FoxRunFlow.Subscribe,
                0,
                hasSource: false,
                FoxRunEndpoint.Foxglove,
                hasTargets: true,
                0,
                hasEncoding: false,
                hasQos: false);
            var bridgeSubscribe = ResolveEndpoints(
                FoxRunFlow.Subscribe,
                FoxRunEndpoint.Ros2Bridge,
                hasSource: true,
                0,
                hasTargets: false,
                0,
                hasEncoding: false,
                hasQos: false);
            var nativeJson = ResolveEndpoints(
                FoxRunFlow.Subscribe,
                FoxRunEndpoint.Ros2Native,
                hasSource: true,
                0,
                hasTargets: false,
                FoxRunEncoding.JSON,
                hasEncoding: true,
                hasQos: false);
            var foxgloveQos = ResolveEndpoints(
                FoxRunFlow.Publish,
                0,
                hasSource: false,
                FoxRunEndpoint.Foxglove,
                hasTargets: true,
                0,
                hasEncoding: false,
                hasQos: true);

            Check(
                valid.Success
                && valid.Topology.Source == FoxRunEndpoint.Ros2Native
                && valid.Topology.Targets
                   == (FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge)
                && valid.Topology.PublishEncoding == FoxRunEncoding.Protobuf,
                "Behavioral 184A-3: a legal full-duplex declaration resolves its independent source, replacement targets, and Foxglove encoding");
            Check(
                !publishSource.Success
                && publishSource.DiagnosticCode == FoxRunEndpointDiagnosticCode.SourceNotAllowed
                && !subscribeTargets.Success
                && subscribeTargets.DiagnosticCode == FoxRunEndpointDiagnosticCode.TargetsNotAllowed
                && !bridgeSubscribe.Success
                && bridgeSubscribe.DiagnosticCode == FoxRunEndpointDiagnosticCode.BridgeSubscribeUnsupported,
                "Behavioral 184A-4: direction and Bridge-subscribe contradictions fail closed with stable diagnostics");
            Check(
                !nativeJson.Success
                && nativeJson.DiagnosticCode == FoxRunEndpointDiagnosticCode.EncodingRequiresFoxglove
                && !foxgloveQos.Success
                && foxgloveQos.DiagnosticCode == FoxRunEndpointDiagnosticCode.QosRequiresRos2,
                "Behavioral 184A-5: explicit encoding and QoS require a resolved compatible transport direction");
        }

        private static void VerifyLegacyAttributeSyntaxGuard()
        {
            var violations = new List<string>();
            foreach (var path in MaintainedTextFiles())
            {
                var text = File.ReadAllText(path);
                var relative = RelativeRepoPath(path);
                violations.AddRange(FindLegacyAttributeSyntaxViolations(relative, text));
            }

            Check(
                violations.Count == 0,
                "Structural 184A-8: old declaration syntax occurs only in analyzer ledgers or explicit negative-compilation string fixtures"
                + FormatViolations(violations));
        }

        private static void VerifyLegacyAttributeSyntaxGuardCoverage()
        {
            var retiredForms = new[]
            {
                "[Fox" + "Run(\"/x\", Mode = FoxRunMode.PublishAndSubscribe)]",
                "[Fox" + "Run(\"/x\", Source = FoxRunSubscriptionProvider.Ros2Native)]",
                "[Fox" + "Run(\"/x\", Encoding = FoxRunWireEncoding.Json)]",
                "[Fox" + "Run(\"/x\", QoS = FoxRunRos2QosPreset.Default)]",
                "[Fox" + "Run(\"/x\", QoS = Ros2BridgeQosProfile.Default)]"
            };
            var qualifiedAttribute = @"
class Qualified
{
    [Unity.FoxgloveSDK.Components.Fox" + @"Run(
        ""/x"", Mode = FoxRun" + @"Mode.PublishAndSubscribe)]
    private int _value;
}";
            var combinedAttributeList = @"
class Combined
{
    [System.Obsolete, Fox" + @"Run(
        ""/x"", Encoding = FoxRunWire" + @"Encoding.Json)]
    private int _value;
}";

            Check(
                retiredForms.All(form => FindLegacySyntaxMatches(form).Any())
                && FindLegacyAttributeSyntaxViolations(
                    "synthetic/Qualified.cs",
                    qualifiedAttribute).Count != 0
                && FindLegacyAttributeSyntaxViolations(
                    "synthetic/Combined.cs",
                    combinedAttributeList).Count != 0
                && FindLegacyAttributeSyntaxViolations(
                    "synthetic/Qualified.md",
                    qualifiedAttribute).Count != 0
                && FindLegacyAttributeSyntaxViolations(
                    "synthetic/Combined.md",
                    combinedAttributeList).Count != 0,
                "Structural 184A-6: the old-syntax guard detects every retired type family in simple, qualified, and combined attribute forms");
        }

        private static void VerifyMaintainedTextScope()
        {
            var maintained = new HashSet<string>(
                MaintainedTextFiles().Select(RelativeRepoPath),
                StringComparer.OrdinalIgnoreCase);
            var required = new[]
            {
                "README.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/README.md",
                "Unity2Foxglove/README.md",
                "docs/architecture-patterns.md",
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md"
            };

            Check(
                required.All(maintained.Contains),
                "Structural 184A-7: old-syntax scanning covers every maintained Phase184 documentation surface");
        }

        private static IEnumerable<Match> FindLegacySyntaxMatches(string text)
            => LegacyNamedArgument.Matches(text)
                .Cast<Match>()
                .Concat(LegacyNamedValue.Matches(text).Cast<Match>())
                .Concat(LegacyTypeReference.Matches(text).Cast<Match>());

        private static IReadOnlyList<string> FindLegacyAttributeSyntaxViolations(
            string relative,
            string text)
        {
            var violations = new List<string>();
            var root = relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? CSharpSyntaxTree.ParseText(text, path: relative).GetRoot()
                : null;
            var sourceAttributes = root?.DescendantNodes()
                .OfType<AttributeSyntax>()
                .Where(IsFoxRunAttribute)
                .ToArray()
                ?? Array.Empty<AttributeSyntax>();

            foreach (var attribute in sourceAttributes)
            {
                var attributeText = text.Substring(attribute.Span.Start, attribute.Span.Length);
                foreach (var match in FindLegacySyntaxMatches(attributeText))
                {
                    var position = attribute.Span.Start + match.Index;
                    violations.Add(relative + ":" + LineNumber(text, position)
                                   + " source attribute uses " + match.Value);
                }
            }

            foreach (var block in FindFoxRunAttributeBlocks(text))
            {
                foreach (var match in FindLegacySyntaxMatches(block.Text))
                {
                    var position = block.Start + match.Index;
                    if (sourceAttributes.Any(attribute => attribute.FullSpan.Contains(position)))
                        continue;

                    if (IsAnalyzerLedger(relative))
                        continue;
                    if (IsNegativeCompilationFixture(relative, root, position))
                        continue;

                    violations.Add(relative + ":" + LineNumber(text, position)
                                   + " non-fixture attribute text uses " + match.Value);
                }
            }

            return violations;
        }

        private static void VerifyPhase184EvidenceClassification()
        {
            var expected = new Dictionary<string, ValidationEvidence>(StringComparer.Ordinal)
            {
                ["--phase184a"] = ValidationEvidence.Behavior | ValidationEvidence.Structural,
                ["--phase184b"] = ValidationEvidence.Behavior,
                ["--phase184c"] = ValidationEvidence.Behavior,
                ["--phase184d"] = ValidationEvidence.Behavior,
                ["--phase184e"] = ValidationEvidence.Behavior | ValidationEvidence.Structural
            };

            Check(
                expected.All(pair =>
                    PhaseValidationRegistry.All.Single(item => item.Flag == pair.Key).Evidence
                    == pair.Value),
                "Structural 184A-9: Phase184 evidence labels match the behavior and structural work each selection actually performs");
        }

        private static void VerifyPublicDocumentationContract()
        {
            var rootReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText("README.md");
            var englishGuide = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md");
            var chineseGuide = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md");
            var customNativeSample = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun Custom ROS2 Interface/README.md");
            var packagedNativeSample = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe/README.md");
            var bridgeReadme = PhaseValidationSourceHelpers.ReadRequiredRepoText(
                "Tools/ros2_bridge/unity2foxglove_ros2_bridge/README.md");

            Check(
                ContainsAll(
                    rootReadme,
                    "[FoxRun(\"/topic\")]",
                    "Mode = FoxRunFlow.Subscribe",
                    "Source = FoxRunEndpoint.Foxglove",
                    "Targets = FoxRunEndpoint.Foxglove",
                    "QoS = FoxRunQosProfile.SensorData",
                    "Policy = FoxRunPolicy.Trigger",
                    "FoxRun_Publish_reset",
                    "PublishAndSubscribe",
                    "FoxRunStream<ControlSample>",
                    "dev.unity2foxglove.sdk",
                    "publish-only",
                    "localhost",
                    "R2FU"),
                "Structural 184A-10: root onboarding shows the minimal declaration and every advanced Phase184 contract without making R2FU mandatory");

            Check(
                new[] { englishGuide, chineseGuide }.All(guide => ContainsAll(
                    guide,
                    "[FoxRun(\"/topic\")]",
                    "Mode = Subscribe",
                    "Source",
                    "Targets",
                    "QoS = FoxRunQosProfile.Default",
                    "FoxRun_Publish_state",
                    "PublishAndSubscribe",
                    "FoxRunStream<ControlSample>",
                    "dev.unity2foxglove.sdk",
                    "localhost"))
                && englishGuide.Contains("publish-only", StringComparison.Ordinal)
                && chineseGuide.Contains("仅支持发布", StringComparison.Ordinal),
                "Structural 184A-11: English and Chinese FoxRun guides cover direction, endpoints, QoS, triggers, full duplex, streams, and package boundaries");

            Check(
                ContainsAll(
                    customNativeSample,
                    "Targets = FoxRunEndpoint.Ros2Native",
                    "Source = FoxRunEndpoint.Ros2Native",
                    "QoS = FoxRunQosProfile.Default",
                    "Native PublishAndSubscribe",
                    "dev.unity2foxglove.sdk",
                    "publish-only",
                    "localhost")
                && ContainsAll(
                    packagedNativeSample,
                    "Source = FoxRunEndpoint.Ros2Native",
                    "FoxRunStream<T>",
                    "dev.unity2foxglove.sdk",
                    "publish-only",
                    "localhost",
                    "R2FU"),
                "Structural 184A-12: native samples state their explicit contracts and preserve the optional-R2FU boundary");

            Check(
                ContainsAll(
                    bridgeReadme,
                    "localhost only",
                    "publish-only",
                    "portable ROS 2 profile",
                    "reliability",
                    "durability",
                    "history",
                    "depth",
                    "U2R2"),
                "Structural 184A-13: Bridge documentation stays localhost, publish-only, portable-QoS, and frame-contract explicit");
        }

        private static bool ContainsAll(string text, params string[] required)
            => required.All(value => text.Contains(value, StringComparison.Ordinal));

        private static void VerifyDirectionalProfileFreeze()
        {
            var publishState = new FoxRunPublishSessionState();
            var publish = publishState.BeginIfNeeded(
                FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.Protobuf,
                10f,
                FoxRunResolvedQos.SensorData,
                FoxRunResolvedQos.Default);
            var frozenPublish = publishState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Bridge,
                FoxRunEncoding.JSON,
                99f,
                FoxRunResolvedQos.SystemDefault,
                FoxRunResolvedQos.SystemDefault);

            var subscribeState = new FoxRunSubscriptionSessionState();
            var subscribe = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Ros2Native,
                FoxRunEncoding.JSON,
                FoxRunResolvedQos.SensorData,
                nativeCopyBudgetBytes: 4096,
                transportAdmissionRateLimitHz: 60,
                defaultSubscribeRateHz: 10);
            var frozenSubscribe = subscribeState.BeginIfNeeded(
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                FoxRunResolvedQos.Default,
                nativeCopyBudgetBytes: 1,
                transportAdmissionRateLimitHz: 1,
                defaultSubscribeRateHz: 1);

            Check(
                ReferenceEquals(publish, frozenPublish)
                && publish.DefaultTargets
                   == (FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native)
                && publish.FoxgloveEncoding == FoxRunEncoding.Protobuf
                && publish.DefaultPublishRateHz == 10f
                && publish.NativeRos2Qos == FoxRunResolvedQos.SensorData,
                "Behavioral 184B-1: the Publish Profile is captured once for the complete enabled session");
            Check(
                ReferenceEquals(subscribe, frozenSubscribe)
                && subscribe.DefaultSource == FoxRunEndpoint.Ros2Native
                && subscribe.FoxgloveEncoding == FoxRunEncoding.JSON
                && subscribe.TransportAdmissionRateLimitHz == 60
                && subscribe.DefaultSubscribeRateHz == 10,
                "Behavioral 184B-2: the Subscribe Profile freezes source, wire encoding, admission ceiling, and inherited apply rate independently");
        }

        private static void VerifyExplicitTargetsReplaceOnlyThePublishProfile()
        {
            var resolution = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: FoxRunEndpoint.Ros2Bridge,
                hasExplicitTargets: true,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Ros2Native,
                defaultTargets: FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);

            Check(
                resolution.Success
                && resolution.Topology.Source == FoxRunEndpoint.Ros2Native
                && resolution.Topology.Targets == FoxRunEndpoint.Ros2Bridge
                && resolution.Topology.PublishEncoding == 0
                && resolution.Topology.SubscribeEncoding == 0,
                "Behavioral 184B-3: explicit Targets replace only publish defaults while omitted Source still inherits the frozen subscribe profile");
        }

        private static void VerifyIndependentEndpointInheritanceAndProfileFailures()
        {
            var explicitSource = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.PublishAndSubscribe,
                declaredSource: FoxRunEndpoint.Foxglove,
                hasExplicitSource: true,
                declaredTargets: 0,
                hasExplicitTargets: false,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Ros2Native,
                defaultTargets: FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);
            var invalidSourceProfile = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.Subscribe,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: 0,
                hasExplicitTargets: false,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource: 0,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);
            var invalidTargetsProfile = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.Publish,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: 0,
                hasExplicitTargets: false,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: 0,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);
            var invalidEncodingProfile = FoxRunEndpointResolver.Resolve(
                FoxRunFlow.Publish,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: 0,
                hasExplicitTargets: false,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource: FoxRunEndpoint.Foxglove,
                defaultTargets: FoxRunEndpoint.Foxglove,
                publishDefaultEncoding: 0,
                subscribeDefaultEncoding: FoxRunEncoding.JSON);

            Check(
                explicitSource.Success
                && explicitSource.Topology.Source == FoxRunEndpoint.Foxglove
                && explicitSource.Topology.Targets
                   == (FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge)
                && explicitSource.Topology.PublishEncoding == FoxRunEncoding.Protobuf
                && explicitSource.Topology.SubscribeEncoding == FoxRunEncoding.JSON,
                "Behavioral 184B-4: explicit Source replaces only subscribe defaults while omitted Targets still inherit the frozen publish profile");
            Check(
                !invalidSourceProfile.Success
                && invalidSourceProfile.DiagnosticCode
                   == FoxRunEndpointDiagnosticCode.InvalidProfileSource
                && !invalidTargetsProfile.Success
                && invalidTargetsProfile.DiagnosticCode
                   == FoxRunEndpointDiagnosticCode.InvalidProfileTargets
                && !invalidEncodingProfile.Success
                && invalidEncodingProfile.DiagnosticCode
                   == FoxRunEndpointDiagnosticCode.InvalidProfileEncoding,
                "Behavioral 184B-5: invalid inherited source, targets, and encoding profiles fail closed with their stable diagnostics");
        }

        private static void VerifyPortableQosResolution()
        {
            var sensorOverride = FoxRunRos2QosProfileResolver.Resolve(
                FoxRunQosProfile.SensorData,
                hasProfile: true,
                FoxRunQosReliability.Reliable,
                hasReliability: true,
                0,
                hasDurability: false,
                FoxRunQosHistory.KeepLast,
                hasHistory: true,
                depth: 3,
                hasDepth: true,
                inherited: FoxRunResolvedQos.SystemDefault);
            var inherited = FoxRunRos2QosProfileResolver.Resolve(
                0, false, 0, false, 0, false, 0, false, 0, false,
                FoxRunResolvedQos.SystemDefault);
            var explicitSystemDefault = FoxRunRos2QosProfileResolver.Resolve(
                FoxRunQosProfile.SystemDefault,
                hasProfile: true,
                0,
                hasReliability: false,
                0,
                hasDurability: false,
                0,
                hasHistory: false,
                depth: 0,
                hasDepth: false,
                inherited: FoxRunResolvedQos.Default);
            var keepAllDepth = FoxRunRos2QosProfileResolver.Resolve(
                FoxRunQosProfile.Default,
                hasProfile: true,
                0,
                hasReliability: false,
                0,
                hasDurability: false,
                FoxRunQosHistory.KeepAll,
                hasHistory: true,
                depth: 2,
                hasDepth: true,
                inherited: FoxRunResolvedQos.Default);

            Check(
                sensorOverride.Success
                && sensorOverride.Qos.Profile == FoxRunQosProfile.SensorData
                && sensorOverride.Qos.Reliability == FoxRunQosReliability.Reliable
                && sensorOverride.Qos.Durability == FoxRunQosDurability.Volatile
                && sensorOverride.Qos.History == FoxRunQosHistory.KeepLast
                && sensorOverride.Qos.Depth == 3,
                "Behavioral 184C-1: official policy overrides resolve on one portable QoS base without transport-specific vocabulary");
            Check(
                inherited.Success
                && inherited.Qos == FoxRunResolvedQos.SystemDefault,
                "Behavioral 184C-2: omitted QoS preserves the frozen inherited System Default transport values");
            Check(
                !keepAllDepth.Success
                && keepAllDepth.DiagnosticCode == FoxRunQosDiagnosticCode.DepthRequiresKeepLast,
                "Behavioral 184C-3: Keep All plus Depth fails closed instead of being silently rewritten");
            Check(
                explicitSystemDefault.Success
                && explicitSystemDefault.Qos == FoxRunResolvedQos.SystemDefault
                && explicitSystemDefault.Qos.Profile == FoxRunQosProfile.SystemDefault
                && explicitSystemDefault.Qos.Reliability
                   == FoxRunQosReliability.SystemDefault
                && explicitSystemDefault.Qos.Durability
                   == FoxRunQosDurability.SystemDefault
                && explicitSystemDefault.Qos.History
                   == FoxRunQosHistory.SystemDefault
                && explicitSystemDefault.Qos.Depth == 0,
                "Behavioral 184C-4: an explicit System Default profile remains the real transport value instead of collapsing to Default");
        }

        private static void VerifySubscribeOnlyIfStaleClearBehavior()
        {
            var source = @"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Phase184RuntimeFixture
{
    public partial class ConditionalInput
    {
        public bool Enabled;

        [FoxRun(""/phase184/runtime/conditional"", Mode = Subscribe,
            Encoding = FoxRunEncoding.JSON, Policy = FoxRunPolicy.Change,
            OnlyIf = nameof(Enabled))]
        public int Value;
    }
}";
            var receiver = CompileFixture(source, "Phase184ConditionalInput");
            var input = (IFoxgloveInputSource)receiver;
            var type = receiver.GetType();
            var enabled = type.GetField("Enabled");
            var value = type.GetField("Value");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var first = router.Dispatch(
                "/phase184/runtime/conditional",
                Encoding.UTF8.GetBytes("{\"Value\":1}"),
                "json",
                1d);
            var rejected = router.Flush(1d, 60);
            var valueAfterRejected = Convert.ToInt32(value.GetValue(receiver));
            enabled.SetValue(receiver, true);
            var stale = router.Flush(2d, 60);
            var valueAfterRecovery = Convert.ToInt32(value.GetValue(receiver));
            var second = router.Dispatch(
                "/phase184/runtime/conditional",
                Encoding.UTF8.GetBytes("{\"Value\":2}"),
                "json",
                3d);
            var applied = router.Flush(3d, 60);

            Check(
                first.Status == FoxRunInputDispatchStatus.Staged
                && rejected == 0
                && valueAfterRejected == 0
                && stale == 0
                && valueAfterRecovery == 0
                && second.Status == FoxRunInputDispatchStatus.Staged
                && applied == 1
                && Convert.ToInt32(value.GetValue(receiver)) == 2,
                "Behavioral 184D-1: Subscribe OnlyIf keeps routing registered, clears false-condition input, and applies only a later message after recovery");
        }

        private static void VerifyPublishConditionAndToleranceBehavior()
        {
            const string topic = "/phase184/runtime/publish-condition";
            var topics = new List<string> { topic };
            var member = new FoxgloveSourceEmitter.TopicMember(
                "Value",
                "System.Single",
                topic,
                0f,
                string.Empty,
                policy: (int)FoxRunPolicy.Change,
                tolerance: 0.5f,
                onlyIf: "Enabled",
                hasExplicitHz: false,
                conditionMemberKind: FoxRunConditionMemberKind.Field);
            var topicMap = new Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>>
            {
                [topic] = new List<FoxgloveSourceEmitter.TopicMember> { member }
            };
            var topicModes = new Dictionary<string, int>
            {
                [topic] = (int)FoxRunPolicy.Change
            };
            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine("using Unity.FoxgloveSDK.Components;");
            var componentAssembly = typeof(FoxRunAttribute).Assembly;
            var needsPolicyInterface = componentAssembly.GetType(
                "Unity.FoxgloveSDK.Components.IFoxgloveLogPolicySource",
                throwOnError: false) == null;
            var needsConditionInterface = componentAssembly.GetType(
                "Unity.FoxgloveSDK.Components.IFoxgloveLogConditionSource",
                throwOnError: false) == null;
            if (needsPolicyInterface || needsConditionInterface)
            {
                sourceBuilder.AppendLine("namespace Unity.FoxgloveSDK.Components");
                sourceBuilder.AppendLine("{");
                if (needsPolicyInterface)
                {
                    sourceBuilder.AppendLine("    public interface IFoxgloveLogPolicySource");
                    sourceBuilder.AppendLine("    {");
                    sourceBuilder.AppendLine("        bool FoxgloveLog_ShouldPublish(int topicIndex, double nowSeconds);");
                    sourceBuilder.AppendLine("        void FoxgloveLog_MarkPublished(int topicIndex, double nowSeconds);");
                    sourceBuilder.AppendLine("    }");
                }
                if (needsConditionInterface)
                {
                    sourceBuilder.AppendLine("    public interface IFoxgloveLogConditionSource");
                    sourceBuilder.AppendLine("    {");
                    sourceBuilder.AppendLine("        bool FoxgloveLog_CanPublish(int topicIndex);");
                    sourceBuilder.AppendLine("    }");
                }
                sourceBuilder.AppendLine("}");
            }
            sourceBuilder.AppendLine("namespace Phase184RuntimeFixture");
            sourceBuilder.AppendLine("{");
            sourceBuilder.AppendLine(
                "    public partial class ConditionalInput : IFoxgloveLogPolicySource, IFoxgloveLogConditionSource");
            sourceBuilder.AppendLine("    {");
            sourceBuilder.AppendLine("        public bool Enabled;");
            sourceBuilder.AppendLine("        public float Value;");
            ConditionEmitter.EmitConditions(sourceBuilder, topics, topicMap, "    ");
            PolicyEmitter.EmitPolicy(sourceBuilder, topics, topicMap, topicModes, "    ");
            sourceBuilder.AppendLine("    }");
            sourceBuilder.AppendLine("}");

            var source = sourceBuilder.ToString();
            var sourceInstance = CompileFixture(source, "Phase184PublishCondition");
            var type = sourceInstance.GetType();
            var enabled = type.GetField("Enabled");
            var value = type.GetField("Value");
            var methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var canPublish = methods.Single(method =>
                method.Name.EndsWith(
                    ".FoxgloveLog_CanPublish",
                    StringComparison.Ordinal));
            var shouldPublish = methods.Single(method =>
                method.Name.EndsWith(
                    ".FoxgloveLog_ShouldPublish",
                    StringComparison.Ordinal));
            var markPublished = methods.Single(method =>
                method.Name.EndsWith(
                    ".FoxgloveLog_MarkPublished",
                    StringComparison.Ordinal));

            var disabled = Convert.ToBoolean(canPublish.Invoke(sourceInstance, new object[] { 0 }));
            enabled.SetValue(sourceInstance, true);
            var enabledNow = Convert.ToBoolean(canPublish.Invoke(sourceInstance, new object[] { 0 }));
            var initial = Convert.ToBoolean(
                shouldPublish.Invoke(sourceInstance, new object[] { 0, 0d }));
            markPublished.Invoke(sourceInstance, new object[] { 0, 0d });
            value.SetValue(sourceInstance, 0.25f);
            var withinTolerance = Convert.ToBoolean(
                shouldPublish.Invoke(sourceInstance, new object[] { 0, 0.1d }));
            value.SetValue(sourceInstance, 0.75f);
            var outsideTolerance = Convert.ToBoolean(
                shouldPublish.Invoke(sourceInstance, new object[] { 0, 0.2d }));

            Check(
                !disabled
                && enabledNow
                && initial
                && !withinTolerance
                && outsideTolerance,
                "Behavioral 184D-2: publish OnlyIf gates the topic and Change tolerance suppresses only values inside the configured band");
        }

        private static void VerifyFanoutFailureIsolation()
        {
            var info = new FoxgloveLogTopicInfo(
                "/phase184/runtime/fanout",
                10f,
                FoxRunPolicy.FixedRate,
                0f,
                FoxRunFlow.Publish,
                declaredSource: 0,
                hasExplicitSource: false,
                declaredTargets: FoxRunEndpoint.Foxglove
                                 | FoxRunEndpoint.Ros2Native
                                 | FoxRunEndpoint.Ros2Bridge,
                hasExplicitTargets: true,
                hasExplicitQos: false);
            Check(
                FoxRunResolvedPublishContract.TryResolve(
                    info,
                    FoxRunEndpoint.Foxglove,
                    FoxRunEncoding.Protobuf,
                    FoxRunResolvedQos.Default,
                    FoxRunResolvedQos.Default,
                    FoxRunEndpoint.Foxglove,
                    FoxRunEncoding.Protobuf,
                    out var contract,
                    out var diagnostic),
                "Behavioral 184D-3: the selected three-target publication resolves before dispatch"
                + (string.IsNullOrEmpty(diagnostic) ? string.Empty : " (" + diagnostic + ")"));

            var sample = new object();
            var captureCount = 0;
            var deliveries = new List<Tuple<FoxRunEndpoint, object, ulong>>();
            var faults = new List<FoxRunEndpoint>();
            const ulong timestamp = 184_000UL;
            var result = FoxRunPublishFanout.Dispatch(
                contract,
                timestamp,
                capture: () =>
                {
                    captureCount++;
                    return sample;
                },
                isReady: _ => true,
                publish: (target, captured, capturedTimestamp) =>
                {
                    if (target == FoxRunEndpoint.Ros2Native)
                        throw new InvalidOperationException("injected native failure");
                    deliveries.Add(Tuple.Create(target, captured, capturedTimestamp));
                    return true;
                },
                onTargetFault: (target, _, __) => faults.Add(target));

            Check(
                captureCount == 1
                && result.Status == FoxRunPublishTargetStatus.Degraded
                && result.SucceededTargets
                   == (FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge)
                && result.FailedTargets == FoxRunEndpoint.Ros2Native
                && deliveries.Count == 2
                && deliveries.All(delivery => ReferenceEquals(delivery.Item2, sample)
                                              && delivery.Item3 == timestamp)
                && faults.SequenceEqual(new[] { FoxRunEndpoint.Ros2Native }),
                "Behavioral 184D-4: one capture and timestamp fan out deterministically while one target failure is isolated without rerouting");

            var readinessCaptureCount = 0;
            var readinessDeliveries = new List<FoxRunEndpoint>();
            var degradedReadiness = FoxRunPublishFanout.Dispatch(
                contract,
                timestamp,
                capture: () =>
                {
                    readinessCaptureCount++;
                    return sample;
                },
                isReady: target => target != FoxRunEndpoint.Ros2Native,
                publish: (target, _, __) =>
                {
                    readinessDeliveries.Add(target);
                    return true;
                });
            Check(
                readinessCaptureCount == 1
                && degradedReadiness.Status == FoxRunPublishTargetStatus.Degraded
                && degradedReadiness.SucceededTargets
                   == (FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Bridge)
                && degradedReadiness.FailedTargets == FoxRunEndpoint.Ros2Native
                && readinessDeliveries.SequenceEqual(
                    new[] { FoxRunEndpoint.Foxglove, FoxRunEndpoint.Ros2Bridge }),
                "Behavioral 184D-5: one unavailable selected target produces Degraded readiness while ready siblings still publish in deterministic order");

            var unavailableCaptureCount = 0;
            var unavailablePublishCount = 0;
            var unavailable = FoxRunPublishFanout.Dispatch(
                contract,
                timestamp,
                capture: () =>
                {
                    unavailableCaptureCount++;
                    return sample;
                },
                isReady: _ => false,
                publish: (_, __, ___) =>
                {
                    unavailablePublishCount++;
                    return true;
                });
            Check(
                unavailable.Status == FoxRunPublishTargetStatus.Unavailable
                && unavailable.SucceededTargets == 0
                && unavailable.FailedTargets == contract.Targets
                && unavailableCaptureCount == 0
                && unavailablePublishCount == 0,
                "Behavioral 184D-6: all selected targets unavailable stops before capture and performs no publish");
        }

        private static void VerifyOriginGovernance()
        {
            var origin = new FoxRunPublishOriginState<int>();
            origin.MarkRemoteApplied(7);

            var firstSuppressed = !origin.CanPublishScheduled(7);
            var repeatSuppressed = !origin.CanPublishScheduled(7);
            var localMutation = origin.CanPublishScheduled(8);
            var released = origin.CanPublishScheduled(8);
            origin.MarkRemoteApplied(9);
            var explicitTrigger = origin.CanPublishExplicit(9);

            Check(
                firstSuppressed
                && repeatSuppressed
                && localMutation
                && released
                && explicitTrigger,
                "Behavioral 184D-7: remote-owned values suppress scheduled echo, local mutation releases ownership, and explicit Trigger remains authoritative");
        }

        private static void VerifyBoundedStreamBehavior()
        {
            var defaults = new FoxRunStreamOptions();
            Check(
                defaults.Capacity == 1024
                && defaults.MaxInputHz == 1000d
                && defaults.MaxBatch == 128
                && defaults.Overflow == FoxRunStreamOverflowPolicy.DropOldest
                && typeof(FoxRunStream<int>).GetProperty("Latest") == null,
                "Structural 184E-1: stream defaults are finite and the API exposes no raw racy Latest reference");

            long ticks = 0;
            var disposed = new List<int>();
            using var stream = new FoxRunStream<int>(
                new FoxRunStreamOptions(2, 10d, 2, FoxRunStreamOverflowPolicy.DropOldest),
                () => ticks,
                timestampFrequency: 100);

            Check(stream.TryAdmitInput(), "Behavioral 184E-2: the first stream input is admitted");
            Check(!stream.TryAdmitInput(), "Behavioral 184E-3: the finite stream admission ceiling rejects an immediate duplicate arrival");
            ticks = 10;
            Check(stream.TryAdmitInput(), "Behavioral 184E-4: input at the next admission boundary is accepted");
            stream.TryEnqueueOwned(1, disposed.Add);
            stream.TryEnqueueOwned(2, disposed.Add);
            ticks = 20;
            var repeatedBoundaryAdmitted = stream.TryAdmitInput();
            stream.TryEnqueueOwned(3, disposed.Add);

            Check(
                repeatedBoundaryAdmitted
                && disposed.SequenceEqual(new[] { 1 })
                && stream.Count == 2
                && stream.Stats.DroppedOldest == 1
                && stream.Stats.RateDropped == 1
                && stream.Stats.HighWater == 2,
                "Behavioral 184E-5: DropOldest stays bounded and reports visible admission and overflow diagnostics");

            Check(
                stream.TryTakeLatest(out var latest)
                && latest.Value == 3,
                "Behavioral 184E-6: TryTakeLatest transfers ownership of the newest sample");
            latest.Dispose();
            latest.Dispose();

            Check(
                disposed.SequenceEqual(new[] { 1, 2, 3 })
                && stream.Count == 0
                && stream.Stats.Cleared == 1
                && stream.Stats.Taken == 1,
                "Behavioral 184E-7: displaced, cleared, and leased values are disposed exactly once with monotonic counters");
        }

        private static void VerifyNoPackageTestOrSampleArtifactDirectories()
        {
            var violations = new List<string>();
            foreach (var root in PackageTestAndSampleRoots())
            {
                foreach (var directory in EnumerateDirectories(root))
                {
                    if (ForbiddenArtifactDirectories.Contains(
                            Path.GetFileName(directory.TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar))))
                    {
                        violations.Add(RelativeRepoPath(directory));
                    }
                }
            }

            Check(
                violations.Count == 0,
                "Structural 184E-8: maintained package Tests and Samples~ contain no build or cache directories"
                + FormatViolations(violations));
        }

        private static FoxRunEndpointResolution ResolveEndpoints(
            FoxRunFlow mode,
            FoxRunEndpoint source,
            bool hasSource,
            FoxRunEndpoint targets,
            bool hasTargets,
            FoxRunEncoding encoding,
            bool hasEncoding,
            bool hasQos)
            => FoxRunEndpointResolver.Resolve(
                mode,
                source,
                hasSource,
                targets,
                hasTargets,
                encoding,
                hasEncoding,
                FoxRunEndpoint.Foxglove,
                FoxRunEndpoint.Foxglove,
                FoxRunEncoding.Protobuf,
                FoxRunEncoding.JSON,
                hasQos);

        private static HashSet<string> PublicPropertyNames(Type type)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.DeclaringType == type)
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

        private static object CompileFixture(string source, string assemblyPrefix)
        {
            var compilation = CSharpCompilation.Create(
                assemblyPrefix + "_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver =
                CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            if (!emit.Success)
            {
                throw new InvalidOperationException(
                    "Phase184 generated fixture failed to compile: "
                    + string.Join("; ", emit.Diagnostics.Select(item => item.ToString())));
            }

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var type = assembly.GetType(
                "Phase184RuntimeFixture.ConditionalInput",
                throwOnError: true);
            return Activator.CreateInstance(type);
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trusted))
            {
                throw new InvalidOperationException(
                    "TRUSTED_PLATFORM_ASSEMBLIES is required for Phase184 runtime fixtures.");
            }

            return trusted
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunAttribute).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static IEnumerable<string> MaintainedTextFiles()
        {
            var root = RepoRoot();
            var roots = new[]
            {
                Path.Combine(root, "Packages", "dev.unity2foxglove.sdk"),
                Path.Combine(root, "Packages", "dev.unity2foxglove.ros2forunity"),
                Path.Combine(root, "Unity2Foxglove", "Assets", "Samples"),
                Path.Combine(root, "Unity2Foxglove", "Assets", "Scripts"),
                Path.Combine(root, "docs")
            };

            var files = roots
                .Where(Directory.Exists)
                .SelectMany(EnumerateMaintainedFiles)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var readme = Path.Combine(root, "README.md");
            if (File.Exists(readme))
                files.Add(readme);
            var unityReadme = Path.Combine(root, "Unity2Foxglove", "README.md");
            if (File.Exists(unityReadme))
                files.Add(unityReadme);
            var bridgeReadme = Path.Combine(
                root,
                "Tools",
                "ros2_bridge",
                "unity2foxglove_ros2_bridge",
                "README.md");
            if (File.Exists(bridgeReadme))
                files.Add(bridgeReadme);
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateMaintainedFiles(string root)
        {
            foreach (var file in Directory.EnumerateFiles(root))
                yield return file;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (ForbiddenArtifactDirectories.Contains(Path.GetFileName(directory)))
                    continue;
                foreach (var file in EnumerateMaintainedFiles(directory))
                    yield return file;
            }
        }

        private static IEnumerable<string> PackageTestAndSampleRoots()
        {
            var packages = Path.Combine(RepoRoot(), "Packages");
            if (!Directory.Exists(packages))
                yield break;

            foreach (var package in Directory.EnumerateDirectories(
                packages,
                "dev.unity2foxglove.*",
                SearchOption.TopDirectoryOnly))
            {
                foreach (var name in new[] { "Tests", "Samples~" })
                {
                    var candidate = Path.Combine(package, name);
                    if (Directory.Exists(candidate))
                        yield return candidate;
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectories(string root)
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                yield return directory;
                if (ForbiddenArtifactDirectories.Contains(Path.GetFileName(directory)))
                    continue;
                foreach (var nested in EnumerateDirectories(directory))
                    yield return nested;
            }
        }

        private static IEnumerable<AttributeBlock> FindFoxRunAttributeBlocks(string text)
        {
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '[')
                    continue;

                var end = text.IndexOf(']', index + 1);
                if (end < 0)
                    yield break;
                var block = text.Substring(index, end - index + 1);
                if (FoxRunAttributeReference.IsMatch(block))
                    yield return new AttributeBlock(index, block);
                index = end;
            }
        }

        private static bool IsFoxRunAttribute(AttributeSyntax attribute)
        {
            var name = attribute.Name.ToString();
            return name.EndsWith("FoxRun", StringComparison.Ordinal)
                   || name.EndsWith("FoxRunAttribute", StringComparison.Ordinal)
                   || name.EndsWith("FoxRunMessage", StringComparison.Ordinal)
                   || name.EndsWith("FoxRunMessageAttribute", StringComparison.Ordinal);
        }

        private static bool IsAnalyzerLedger(string relativePath)
            => relativePath.StartsWith(
                   "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.",
                   StringComparison.OrdinalIgnoreCase)
               && relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

        private static bool IsNegativeCompilationFixture(
            string relativePath,
            SyntaxNode root,
            int position)
        {
            if (!string.Equals(
                    relativePath,
                    "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxRunLegacyApiRemovalTests.cs",
                    StringComparison.OrdinalIgnoreCase)
                || root == null)
                return false;

            return root.DescendantTokens(descendIntoTrivia: true)
                .Any(token => token.FullSpan.Contains(position)
                              && (token.IsKind(SyntaxKind.StringLiteralToken)
                                  || token.IsKind(SyntaxKind.InterpolatedStringTextToken)));
        }

        private static int LineNumber(string text, int position)
        {
            var line = 1;
            for (var index = 0; index < position && index < text.Length; index++)
            {
                if (text[index] == '\n')
                    line++;
            }
            return line;
        }

        private static string RepoRoot()
            => TestRepoRootLocator.FindRepoRoot()
               ?? throw new DirectoryNotFoundException(
                   "Could not locate repository root for Phase184 validation.");

        private static string RelativeRepoPath(string path)
            => Path.GetRelativePath(RepoRoot(), path)
                .Replace(Path.DirectorySeparatorChar, '/');

        private static string FormatViolations(IReadOnlyCollection<string> violations)
            => violations.Count == 0
                ? string.Empty
                : ": " + string.Join("; ", violations.Take(12));

        private static void Begin(string name)
        {
            Console.WriteLine("\n--- " + name + " ---");
            _passed = 0;
        }

        private static void End(string name)
            => Console.WriteLine(name + ": " + _passed + " checks passed.\n");

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }

        private readonly struct AttributeBlock
        {
            public AttributeBlock(int start, string text)
            {
                Start = start;
                Text = text;
            }

            public int Start { get; }
            public string Text { get; }
        }
    }
}
