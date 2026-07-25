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
            VerifyLegacyAttributeSyntaxGuard();
            End("Phase 184A");
        }

        public static void ValidatePhase184B()
        {
            Begin("Phase 184B: frozen directional FoxRun profiles");
            VerifyDirectionalProfileFreeze();
            VerifyExplicitTargetsReplaceOnlyThePublishProfile();
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
                var root = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    ? CSharpSyntaxTree.ParseText(text, path: path).GetRoot()
                    : null;
                var sourceAttributes = root?.DescendantNodes()
                    .OfType<AttributeSyntax>()
                    .Where(IsFoxRunAttribute)
                    .ToArray()
                    ?? Array.Empty<AttributeSyntax>();

                foreach (var block in FindFoxRunAttributeBlocks(text))
                {
                    foreach (Match match in LegacyNamedArgument.Matches(block.Text)
                        .Cast<Match>()
                        .Concat(LegacyNamedValue.Matches(block.Text).Cast<Match>()))
                    {
                        var position = block.Start + match.Index;
                        if (sourceAttributes.Any(attribute => attribute.FullSpan.Contains(position)))
                        {
                            violations.Add(relative + ":" + LineNumber(text, position)
                                           + " source attribute uses " + match.Value);
                            continue;
                        }

                        if (IsAnalyzerLedger(relative))
                            continue;
                        if (IsNegativeCompilationFixture(relative, root, position))
                            continue;

                        violations.Add(relative + ":" + LineNumber(text, position)
                                       + " non-fixture attribute text uses " + match.Value);
                    }
                }
            }

            Check(
                violations.Count == 0,
                "Structural 184A-6: old named arguments occur only in analyzer ledgers or the explicit negative-compilation string fixture"
                + FormatViolations(violations));
        }

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
            enabled.SetValue(receiver, true);
            var stale = router.Flush(2d, 60);
            var second = router.Dispatch(
                "/phase184/runtime/conditional",
                Encoding.UTF8.GetBytes("{\"Value\":2}"),
                "json",
                3d);
            var applied = router.Flush(3d, 60);

            Check(
                first.Status == FoxRunInputDispatchStatus.Staged
                && rejected == 0
                && stale == 0
                && second.Status == FoxRunInputDispatchStatus.Staged
                && applied == 1
                && Convert.ToInt32(value.GetValue(receiver)) == 2,
                "Behavioral 184D-1: Subscribe OnlyIf keeps routing registered, clears false-condition input, and applies only a later message after recovery");
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
                "Behavioral 184D-2: the selected three-target publication resolves before dispatch"
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
                "Behavioral 184D-3: one capture and timestamp fan out deterministically while one target failure is isolated without rerouting");
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
                "Behavioral 184D-4: remote-owned values suppress scheduled echo, local mutation releases ownership, and explicit Trigger remains authoritative");
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
                Path.Combine(root, "Unity2Foxglove", "Assets", "Scripts")
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
                if (!StartsWithAttributeName(text, index + 1, "FoxRun")
                    && !StartsWithAttributeName(text, index + 1, "FoxRunAttribute")
                    && !StartsWithAttributeName(text, index + 1, "FoxRunMessage")
                    && !StartsWithAttributeName(text, index + 1, "FoxRunMessageAttribute"))
                {
                    continue;
                }

                var end = text.IndexOf(']', index + 1);
                if (end < 0)
                    yield break;
                yield return new AttributeBlock(index, text.Substring(index, end - index + 1));
                index = end;
            }
        }

        private static bool StartsWithAttributeName(string text, int start, string name)
        {
            if (start + name.Length > text.Length
                || !string.Equals(
                    text.Substring(start, name.Length),
                    name,
                    StringComparison.Ordinal))
                return false;
            if (start + name.Length == text.Length)
                return true;
            var next = text[start + name.Length];
            return char.IsWhiteSpace(next) || next == '(' || next == ']';
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
