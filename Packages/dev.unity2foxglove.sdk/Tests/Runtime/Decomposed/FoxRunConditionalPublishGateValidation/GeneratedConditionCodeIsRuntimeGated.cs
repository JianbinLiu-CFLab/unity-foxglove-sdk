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
    public static partial class FoxRunConditionalPublishGateValidation
    {
        private static bool GeneratedConditionCodeIsRuntimeGated(string source)
        {
            return source.Contains("IFoxgloveLogConditionSource", StringComparison.Ordinal)
                   && source.Contains("FoxgloveLog_CanPublish(int topicIndex)", StringComparison.Ordinal)
                   && ((SwitchCaseContains(source, 0, "healthPublishingEnabled")
                        && SwitchCaseContains(source, 1, "telemetryEnabled"))
                       || (SwitchCaseContains(source, 0, "telemetryEnabled")
                           && SwitchCaseContains(source, 1, "healthPublishingEnabled")));
        }

        private static bool SwitchCaseContains(string source, int caseIndex, string expected)
        {
            var methodStart = source.IndexOf("FoxgloveLog_CanPublish(int topicIndex)", StringComparison.Ordinal);
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
