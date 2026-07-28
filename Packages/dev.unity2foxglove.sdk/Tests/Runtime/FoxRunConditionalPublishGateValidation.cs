// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxRun conditional publish gates and generated runtime enforcement.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Transport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunConditionalPublishGateValidation
    {
        private const string ExpectedCheckedInGeneratorSha256 = "78D36D85DBE6E8AD89ED531EDA3E0A336F8DDD0BA0ED26A31BCA785D40EA6F02";
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141A Tests ---");
            _passCount = 0;

            VerifyAttributeSurface();
            VerifyRuntimeGateContract();
            VerifyEmitterConditionOutput();
            VerifyRoslynAndCheckedInDllEmitRuntimeConditions();
            VerifyRuntimeToggleStopsSubscribedDataFrames();
            VerifyModelCarriesConditionMetadata();
            VerifyDiagnosticsInventory();
            VerifyInvalidConditionIdentifiersReportMissingMemberDiagnostic();
            VerifyDocsMentionConditions();

            Console.WriteLine("Phase 141A: " + _passCount + " checks passed.\n");
        }

        private static void VerifyAttributeSurface()
        {
            var attr = new FoxRunAttribute("/phase141a");
            Check(HasProperty(typeof(FoxRunAttribute), "OnlyIf"), "141A-1: FoxRunAttribute exposes OnlyIf");
            Check(ReadStringProperty(attr, "OnlyIf") == string.Empty, "141A-2: OnlyIf defaults to empty string");
        }

        private static void VerifyRuntimeGateContract()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs");
            var conditionPath = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/IFoxgloveLogConditionSource.cs");

            Check(File.Exists(conditionPath), "141A-5: IFoxgloveLogConditionSource file exists");
            Check(hub.Contains("IFoxgloveLogConditionSource", StringComparison.Ordinal),
                "141A-6: FoxgloveLogHub references condition source interface");
            Check(hub.Contains("CanPublishSourceTopic", StringComparison.Ordinal),
                "141A-7: FoxgloveLogHub centralizes condition gate checks");
            Check(hub.IndexOf("CanPublishSourceTopic(source, topicIndex", StringComparison.Ordinal)
                  < hub.IndexOf("FoxgloveLog_ShouldPublish(topicIndex, nowSec)", StringComparison.Ordinal),
                "141A-8: scheduled condition gate runs before policy gate");
        }

        private static void VerifyEmitterConditionOutput()
        {
            var conditional = FoxgloveSourceEmitter.EmitClass("Phase141A", "ConditionalSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    new("_position", "UnityEngine.Vector3", "/phase141a/position", 10f, "",
                        policy: (int)FoxRunPolicy.FixedRate, tolerance: 0f,
                        onlyIf: "TelemetryEnabled")
                });

            Check(conditional.Contains("IFoxgloveLogConditionSource", StringComparison.Ordinal),
                "141A-9: conditional source implements condition interface");
            Check(conditional.Contains("FoxgloveLog_CanPublish", StringComparison.Ordinal),
                "141A-10: conditional source emits CanPublish method");
            Check(conditional.Contains("return TelemetryEnabled;", StringComparison.Ordinal),
                "141A-11: condition expression uses direct member access");

            var unconditional = FoxgloveSourceEmitter.EmitClass("Phase141A", "UnconditionalSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    new("_value", "System.Int32", "/phase141a/value", 10f, "")
                });
            Check(!unconditional.Contains("IFoxgloveLogConditionSource", StringComparison.Ordinal),
                "141A-12: unconditional source does not implement condition interface");
        }

        private static void VerifyRoslynAndCheckedInDllEmitRuntimeConditions()
        {
            var sourceGenerated = GeneratedFoxRunSource(RunGenerator(
                new FoxgloveLogSourceGenerator(),
                "Phase141AConditionSourceGenerator"));
            Check(GeneratedConditionCodeIsRuntimeGated(sourceGenerated),
                "141A-13: source generator emits runtime OnlyIf condition checks");

            var dllPath = RepoPath("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll");
            var checkedInGenerated = GeneratedFoxRunSource(RunGenerator(
                LoadGeneratorFromDll(dllPath),
                "Phase141AConditionCheckedInDll"));
            Check(GeneratedConditionCodeIsRuntimeGated(checkedInGenerated),
                "141A-14: checked-in analyzer DLL emits runtime OnlyIf condition checks");
        }

        private static void VerifyRuntimeToggleStopsSubscribedDataFrames()
        {
            var source = FoxgloveSourceEmitter.EmitClass("Phase141A", "RuntimeConditionSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    new("conditionalPosition", "UnityEngine.Vector3", "/debug/conditional_position", 15f, "",
                        policy: (int)FoxRunPolicy.FixedRate, tolerance: 0f,
                        onlyIf: "telemetryEnabled"),
                    new("conditionalHealth", "System.Int32", "/debug/conditional_health", 15f, "",
                        policy: (int)FoxRunPolicy.FixedRate, tolerance: 0f,
                        onlyIf: "healthPublishingEnabled")
                });
            var compiled = CompileRuntimeConditionFixture(source);
            var sourceType = compiled.GetType("Phase141A.RuntimeConditionSource")
                             ?? throw new InvalidOperationException("Missing compiled runtime condition fixture.");
            var conditionInterface = compiled.GetType("Unity.FoxgloveSDK.Components.IFoxgloveLogConditionSource")
                                     ?? throw new InvalidOperationException("Missing compiled condition interface.");
            var instance = Activator.CreateInstance(sourceType);
            var telemetryEnabled = sourceType.GetField("telemetryEnabled")
                                   ?? throw new InvalidOperationException("Missing telemetryEnabled field.");
            var healthPublishingEnabled = sourceType.GetField("healthPublishingEnabled")
                                          ?? throw new InvalidOperationException("Missing healthPublishingEnabled field.");

            using var transport = new Phase141ADataTransport();
            using var session = new FoxgloveSession("phase141a", transport, schemaRegistry: new DefaultSchemaRegistry());
            session.RegisterChannel(new AdvertiseChannel { Id = 1410, Topic = "/debug/conditional_position", Encoding = "json", SchemaName = "", Schema = "" });
            session.RegisterChannel(new AdvertiseChannel { Id = 1411, Topic = "/debug/conditional_health", Encoding = "json", SchemaName = "", Schema = "" });
            transport.SimulateText(1, JsonConvert.SerializeObject(new SubscribeMessage
            {
                Subscriptions = new List<Subscription>
                {
                    new Subscription { Id = 10, ChannelId = 1410 },
                    new Subscription { Id = 11, ChannelId = 1411 }
                }
            }));

            Check(transport.BroadcastTexts.Any(text => text.Contains("\"op\":\"advertise\"", StringComparison.Ordinal)
                                                       && text.Contains("/debug/conditional_position", StringComparison.Ordinal)),
                "141A-15: conditional topic remains advertised before subscription");

            var expectedFrames = 0;
            for (var cycle = 0; cycle < 5; cycle++)
            {
                telemetryEnabled.SetValue(instance, true);
                healthPublishingEnabled.SetValue(instance, true);
                PublishIfAllowed(session, conditionInterface, instance, 0, 1410, (ulong)(cycle * 4 + 1), ref expectedFrames);
                PublishIfAllowed(session, conditionInterface, instance, 1, 1411, (ulong)(cycle * 4 + 2), ref expectedFrames);
                Check(transport.SentBinaries.Count == expectedFrames,
                    "141A-16: enabled cycle " + cycle + " publishes subscribed data frames");

                telemetryEnabled.SetValue(instance, false);
                healthPublishingEnabled.SetValue(instance, false);
                PublishIfAllowed(session, conditionInterface, instance, 0, 1410, (ulong)(cycle * 4 + 3), ref expectedFrames);
                PublishIfAllowed(session, conditionInterface, instance, 1, 1411, (ulong)(cycle * 4 + 4), ref expectedFrames);
                Check(transport.SentBinaries.Count == expectedFrames,
                    "141A-17: disabled cycle " + cycle + " stops subscribed data frames without reconnect");
            }
        }

        private static void VerifyModelCarriesConditionMetadata()
        {
            var member = new FoxRunGenerationMember(
                "Phase141A", "ModelSource", "_value", "field",
                "System.Int32", "System.Int32", "int32",
                true, false, string.Empty,
                "/phase141a/model", 10f, "",
                (int)FoxRunPolicy.FixedRate, 0f, "Reflection", 0, "",
                onlyIf: "Enabled");

            Check(member.OnlyIf == "Enabled", "141A-18: generation member carries OnlyIf");
            var topicMember = member.ToTopicMember();
            Check(topicMember.OnlyIf == "Enabled",
                "141A-20: generation member forwards OnlyIf to emitter");
        }

        private static void VerifyDiagnosticsInventory()
        {
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            var validator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            var shipped = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");

            foreach (var id in new[] { "FOXRUN015", "FOXRUN016", "FOXRUN017" })
            {
                Check(generator.Contains("\"" + id + "\"", StringComparison.Ordinal),
                    "141A-21: source generator declares " + id);
                Check(validator.Contains("\"" + id + "\"", StringComparison.Ordinal),
                    "141A-22: shared validator declares " + id);
                Check(shipped.Contains(id, StringComparison.Ordinal),
                    "141A-23: shipped analyzer docs include " + id);
            }
        }

        private static void VerifyInvalidConditionIdentifiersReportMissingMemberDiagnostic()
        {
            var diagnostics = RunGeneratorDiagnostics(@"
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests.Fixtures
{
    public partial class Phase141AInvalidConditionFixture
    {
        [FoxRun(""/debug/invalid_only_if"", Hz = 15, OnlyIf = ""not valid"")]
        public int invalidOnlyIf;
    }
}
");

            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN015")
                  && !diagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN016"),
                "141A-24: invalid OnlyIf identifiers report FOXRUN015 instead of bool-type FOXRUN016");
        }

        private static void VerifyDocsMentionConditions()
        {
            var en = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md");
            var zh = ReadRepoText("Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md");

            Check(en.Contains("`OnlyIf`", StringComparison.Ordinal),
                "141A-25: English docs mention OnlyIf");
            Check(zh.Contains("`OnlyIf`", StringComparison.Ordinal),
                "141A-26: Chinese docs mention OnlyIf");
        }

        private static void PublishIfAllowed(FoxgloveSession session, Type conditionInterface, object source, int topicIndex, uint channelId, ulong nowNs, ref int expectedFrames)
        {
            var canPublishMethod = conditionInterface.GetMethod("FoxgloveLog_CanPublish")
                ?? throw new InvalidOperationException("Missing IFoxgloveLogConditionSource.FoxgloveLog_CanPublish in runtime condition fixture.");
            var result = canPublishMethod.Invoke(source, new object[] { topicIndex });
            if (result is not bool canPublish)
                throw new InvalidOperationException("FoxgloveLog_CanPublish did not return a bool for topic index " + topicIndex + ".");
            if (!canPublish)
                return;

            session.PublishJson(channelId, new Dictionary<string, object> { { "value", topicIndex } }, nowNs);
            expectedFrames++;
        }

        private static Assembly CompileRuntimeConditionFixture(string generatedSource)
        {
            var sources = new[]
            {
                RuntimeConditionStubs(),
                @"
namespace Phase141A
{
    public partial class RuntimeConditionSource
    {
        public bool telemetryEnabled = true;
        public bool healthPublishingEnabled = true;
        public UnityEngine.Vector3 conditionalPosition;
        public int conditionalHealth;
    }
}
",
                generatedSource
            };
            var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9))).ToArray();
            var compilation = CSharpCompilation.Create(
                "Phase141ARuntimeConditionFixture",
                syntaxTrees,
                References().Where(reference => !Path.GetFileName(reference.Display).Equals("FoxgloveSdk.Tests.dll", StringComparison.OrdinalIgnoreCase)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            if (!emit.Success)
            {
                var errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
                throw new InvalidOperationException(errors);
            }

            return Assembly.Load(stream.ToArray());
        }

        private static string RuntimeConditionStubs()
            => @"
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }
}

namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunPolicy
    {
        FixedRate = 1,
        Change = 2,
        Trigger = 4
    }

    public readonly struct FoxgloveLogTopicInfo
    {
        public readonly string Topic;
        public readonly float Hz;
        public readonly FoxRunPolicy Policy;
        public readonly float Tolerance;

        public FoxgloveLogTopicInfo(string topic, float hz)
        {
            Topic = topic;
            Hz = hz;
            Policy = FoxRunPolicy.FixedRate;
            Tolerance = 0f;
        }

        public FoxgloveLogTopicInfo(string topic, float hz, FoxRunPolicy policy, float tolerance)
        {
            Topic = topic;
            Hz = hz;
            Policy = policy;
            Tolerance = tolerance;
        }
    }

    public sealed class FoxgloveManager
    {
        public void PublishJson(string topic, string schemaName, object message, ulong logTimeNs) { }
    }

    public interface IFoxgloveLogSource
    {
        int FoxgloveLog_TopicCount { get; }
        FoxgloveLogTopicInfo FoxgloveLog_GetTopic(int index);
        void FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs);
    }

    public enum FoxTopicVisibility
    {
        LocalOnly = 0,
        Exported = 1
    }

    public enum FoxTopicWriterPolicy
    {
        SingleWriter = 0,
        MultiWriter = 1
    }

    public sealed class FoxTopicContract
    {
        public FoxTopicContract(string topic, string schemaName, string encoding, string canonicalType, string stableFingerprint, FoxTopicVisibility visibility, FoxTopicWriterPolicy writerPolicy)
        {
            Topic = topic;
            SchemaName = schemaName;
            Encoding = encoding;
            CanonicalType = canonicalType;
            StableFingerprint = stableFingerprint;
            Visibility = visibility;
            WriterPolicy = writerPolicy;
        }

        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string CanonicalType { get; }
        public string StableFingerprint { get; }
        public FoxTopicVisibility Visibility { get; }
        public FoxTopicWriterPolicy WriterPolicy { get; }
    }

    public interface IFoxgloveTopicContractSource
    {
        string FoxgloveLog_Origin { get; }
        FoxTopicContract FoxgloveLog_GetContract(int index);
    }

    public sealed class FoxTopicBus
    {
        public bool HasSubscribers(string topic) => true;

        public void Publish<T>(FoxTopicContract contract, ulong timestampNs, in T payload, string origin) { }
    }

    public sealed class FoxTopicSinkRouter
    {
        public bool HasSinks => true;

        public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin) { }
    }

    public interface IFoxgloveTopicBusSource
    {
        void FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs);
    }

    public interface IFoxgloveTopicSinkSource
    {
        void FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs);
    }

    public interface IFoxgloveLogConditionSource
    {
        bool FoxgloveLog_CanPublish(int topicIndex);
    }
}
";

        private static bool GeneratedConditionCodeIsRuntimeGated(string source)
        {
            return source.Contains("IFoxgloveLogConditionSource", StringComparison.Ordinal)
                   && source.Contains("FoxgloveLog_CanPublish", StringComparison.Ordinal)
                   && SwitchCaseContains(source, 0, "healthPublishingEnabled")
                   && SwitchCaseContains(source, 1, "telemetryEnabled");
        }

        private static bool SwitchCaseContains(string source, int caseIndex, string expected)
        {
            var methodStart = source.IndexOf("FoxgloveLog_CanPublish", StringComparison.Ordinal);
            if (methodStart < 0)
                return false;

            var caseMarker = "case " + caseIndex + ":";
            var start = source.IndexOf(caseMarker, methodStart, StringComparison.Ordinal);
            if (start < 0)
                return false;

            var end = source.Length;
            foreach (var marker in new[] { "case " + (caseIndex + 1) + ":", "default:" })
            {
                var candidate = source.IndexOf(marker, start + caseMarker.Length, StringComparison.Ordinal);
                if (candidate >= 0 && candidate < end)
                    end = candidate;
            }

            return source.IndexOf(expected, start, end - start, StringComparison.Ordinal) >= 0;
        }

        private static IReadOnlyList<GeneratedSourceResult> RunGenerator(IIncrementalGenerator generator, string assemblyName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(ConditionFixtureSource(), CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            return driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToList();
        }

        private static IReadOnlyList<Diagnostic> RunGeneratorDiagnostics(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141AInvalidConditionFixture",
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult().Diagnostics;
        }

        private static IIncrementalGenerator LoadGeneratorFromDll(string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException("Checked-in analyzer DLL was not found.", dllPath);
            var dllBytes = File.ReadAllBytes(Path.GetFullPath(dllPath));
            using (var sha256 = SHA256.Create())
            {
                var actualSha256 = BitConverter.ToString(sha256.ComputeHash(dllBytes)).Replace("-", string.Empty);
                if (!string.Equals(actualSha256, ExpectedCheckedInGeneratorSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Checked-in analyzer DLL SHA-256 mismatch. Expected "
                        + ExpectedCheckedInGeneratorSha256 + ", got " + actualSha256 + ".");
            }

            var assembly = Assembly.Load(dllBytes);
            var type = assembly.GetType("Unity.FoxgloveSDK.SourceGenerators.FoxgloveLogSourceGenerator")
                       ?? throw new InvalidOperationException("Checked-in analyzer DLL does not contain FoxgloveLogSourceGenerator.");
            return (IIncrementalGenerator)Activator.CreateInstance(type);
        }

        private static string GeneratedFoxRunSource(IReadOnlyList<GeneratedSourceResult> sources)
        {
            var source = sources.FirstOrDefault(candidate =>
                candidate.HintName.EndsWith("_FoxRun.g.cs", StringComparison.Ordinal));
            if (source.HintName == null)
                throw new InvalidOperationException("Generated sources do not contain an expected *_FoxRun.g.cs hint.");
            return source.SourceText.ToString();
        }

        private static MetadataReference[] References()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141A Roslyn reference resolution.");

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location) })
                .ToArray();
        }

        private static string ConditionFixtureSource()
            => @"
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }
}

namespace Unity.FoxgloveSDK.Tests.Fixtures
{
    public partial class Phase141AConditionFixture
    {
        public bool telemetryEnabled = true;
        public bool healthPublishingEnabled = true;

        [FoxRun(""/debug/conditional_position"", Hz = 15, OnlyIf = nameof(telemetryEnabled))]
        public UnityEngine.Vector3 conditionalPosition;

        [FoxRun(""/debug/conditional_health"", Hz = 15, OnlyIf = nameof(healthPublishingEnabled))]
        public int conditionalHealth;
    }
}
";

        private static bool HasProperty(Type type, string name)
            => type.GetProperty(name) != null;

        private static string ReadStringProperty(object instance, string name)
            => (string)(instance.GetType().GetProperty(name)?.GetValue(instance) ?? "<missing>");

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase141A validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class Phase141ADataTransport : IFoxgloveTransport
        {
            public readonly List<string> BroadcastTexts = new();
            public readonly List<byte[]> SentBinaries = new();

            public bool IsRunning { get; private set; }

            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) => BroadcastTexts.Add(json);
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) => SentBinaries.Add(data);

            public void SimulateText(uint clientId, string json)
                => OnTextReceived?.Invoke(clientId, json);
        }
    }
}
