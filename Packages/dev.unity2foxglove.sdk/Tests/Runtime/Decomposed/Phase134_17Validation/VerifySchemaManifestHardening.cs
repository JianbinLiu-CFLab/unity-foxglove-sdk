// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-17 validation for FoxRun invalid-topic fail-fast behavior.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_17Validation.
    /// </summary>
    public static partial class Phase134_17Validation
    {
        private static void VerifySchemaManifestHardening()
        {
            CheckThrows<ArgumentException>(() => new Unity2FoxgloveProtobufRegistrySection(
                    "protobuf",
                    "hash",
                    "descriptor",
                    2,
                    new[] { new Unity2FoxgloveProtobufRegistryEntry("foxglove.Log", "T", "debug", true, string.Empty) }),
                "134-17-H1: protobuf schema manifest section validates entryCount consistency");

            CheckThrows<ArgumentException>(() => new Unity2FoxgloveSdkTypedPublishersSection(
                    2,
                    new[] { new Unity2FoxgloveSdkTypedPublisherEntry("Publisher", "typed", "family", "/topic", "foxglove.Log", true, true, false, false, string.Empty) }),
                "134-17-H2: SDK typed publishers section validates entryCount consistency");

            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            Check(builder.Contains("entry.SupportsJson || entry.SupportsProtobuf", StringComparison.Ordinal)
                  && builder.Contains("entry.FoxgloveSchemaName", StringComparison.Ordinal)
                  && !builder.Contains("SupportsRos2", StringComparison.Ordinal),
                "134-17-H3: schema manifest builder validates neutral encoding flags against schema names");

            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs");
            Check(writer.Contains("FileContentEquals", StringComparison.Ordinal)
                  && writer.Contains("FileContentEqualsOnce", StringComparison.Ordinal)
                  && writer.Contains("AggregateException", StringComparison.Ordinal),
                "134-17-H4: schema manifest writer retries stale reads and preserves replace/copy failures");
        }

        private static Diagnostic[] RunGeneratorDiagnostics(string source, string assemblyName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[] { MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location) })
                .ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var driverDiagnostics);
            return driverDiagnostics.Concat(outputCompilation.GetDiagnostics()).ToArray();
        }

        private static FoxRunGenerationModel ModelWithTopic(string topic)
        {
            return new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "TopicProbe", new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "TopicProbe",
                        "value",
                        "field",
                        "float",
                        true,
                        false,
                        string.Empty,
                        topic,
                        10f,
                        string.Empty,
                        (int)FoxRunPolicy.FixedRate,
                        0f,
                        "Test",
                        0,
                        string.Empty)
                })
            });
        }

        private static FoxRunGenerationMember Member(
            string ns,
            string className,
            string memberName,
            string topic,
            string typeName,
            float hz = 1f)
        {
            return new FoxRunGenerationMember(
                ns,
                className,
                memberName,
                "field",
                typeName,
                true,
                false,
                string.Empty,
                topic,
                hz,
                string.Empty,
                (int)FoxRunPolicy.FixedRate,
                0f,
                "Test",
                0,
                string.Empty);
        }

        private static void CheckThrows<TException>(Action action, string name)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                Check(true, name);
                return;
            }

            throw new InvalidOperationException(name);
        }

        private static string InvalidTopicSource(string topic)
        {
            return @"
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Validation type for InvalidTopicProbe.
/// </summary>
public partial class InvalidTopicProbe
{
    [FoxRun(""" + topic + @""")]
    public float value;
}
";
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
