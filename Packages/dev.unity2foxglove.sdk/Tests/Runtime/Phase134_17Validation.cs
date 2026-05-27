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
    public static class Phase134_17Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifySharedValidatorRejectsInvalidTopics();
            VerifyRoslynGeneratorReportsInvalidTopicErrors();
            VerifyPhysicalFallbackCannotBypassValidator();
            VerifyTypeNormalizationHardening();
            VerifyDescriptorRoundTripAndComparisonHardening();
            VerifyModelAndManifestValidationHardening();
            VerifySchemaManifestHardening();
            VerifySourceWiring();

            Console.WriteLine($"Phase134_17Validation: PASS ({_passed} checks)");
        }

        private static void VerifySharedValidatorRejectsInvalidTopics()
        {
            var relativeDiagnostics = FoxRunGenerationModelValidator.Validate(ModelWithTopic("relative"));
            Check(relativeDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN008" && diagnostic.Severity == "Error"),
                "134-17-A1: shared validator reports FOXRUN008 Error for relative topics");

            var blankDiagnostics = FoxRunGenerationModelValidator.Validate(ModelWithTopic(string.Empty));
            Check(blankDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN008" && diagnostic.Severity == "Error"),
                "134-17-A2: shared validator reports FOXRUN008 Error for blank topics");
        }

        private static void VerifyRoslynGeneratorReportsInvalidTopicErrors()
        {
            var relativeDiagnostics = RunGeneratorDiagnostics(InvalidTopicSource("relative"), "FoxRunRelativeTopic13417");
            Check(relativeDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN008" && diagnostic.Severity == DiagnosticSeverity.Error),
                "134-17-B1: Roslyn generator reports FOXRUN008 Error for relative topics");

            var blankDiagnostics = RunGeneratorDiagnostics(InvalidTopicSource(string.Empty), "FoxRunBlankTopic13417");
            Check(blankDiagnostics.Any(diagnostic => diagnostic.Id == "FOXRUN008" && diagnostic.Severity == DiagnosticSeverity.Error),
                "134-17-B2: Roslyn generator reports FOXRUN008 Error for blank topics");
        }

        private static void VerifyPhysicalFallbackCannotBypassValidator()
        {
            var codeGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(codeGenerator.Contains("FoxRunGenerationModelValidator.Validate(model)", StringComparison.Ordinal)
                  && codeGenerator.Contains("diagnostic.Severity, \"Error\"", StringComparison.Ordinal)
                  && codeGenerator.Contains("throw new InvalidOperationException", StringComparison.Ordinal),
                "134-17-C1: physical fallback generation fails when shared validator reports errors");
        }

        private static void VerifySourceWiring()
        {
            var validator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            Check(validator.Contains("Error(\"FOXRUN008\"", StringComparison.Ordinal),
                "134-17-D1: invalid topic diagnostic is modeled as an error");

            var sourceGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(sourceGenerator.Contains("\"FOXRUN008\", \"FoxRun topic must be absolute\"", StringComparison.Ordinal)
                  && sourceGenerator.Contains("DiagnosticSeverity.Error", StringComparison.Ordinal),
                "134-17-D2: Roslyn FOXRUN008 descriptor uses error severity");
        }

        private static void VerifyTypeNormalizationHardening()
        {
            var clrNested = "System.Collections.Generic.Dictionary`2[[System.String, mscorlib],[System.Collections.Generic.List`1[[System.Int32, mscorlib]], mscorlib]]";
            Check(
                FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(clrNested) ==
                "System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>",
                "134-17-E1: CLR nested generic normalization is bracket-depth aware");

            Check(FoxRunEmissionTypeNameFormatter.FromReflectionType(typeof(int[,])) == "int[,]",
                "134-17-E2: multi-dimensional reflection arrays keep alias-normalized element names");

            Check(FoxRunCanonicalTypeNormalizer.NormalizeTypeName("Nullable<int>") == "int32",
                "134-17-E3: short Nullable<T> syntax unwraps to canonical type");
        }

        private static void VerifyDescriptorRoundTripAndComparisonHardening()
        {
            var model = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "DescriptorProbe", new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        "DescriptorProbe",
                        "values",
                        "field",
                        "System.Single[]",
                        "float[]",
                        null,
                        true,
                        true,
                        "System.Single",
                        "/demo/values",
                        float.NaN,
                        string.Empty,
                        2,
                        float.PositiveInfinity,
                        float.NegativeInfinity,
                        "Test",
                        0,
                        string.Empty)
                })
            });

            var json = FoxRunGenerationDescriptorJsonWriter.Write(model);
            var root = JObject.Parse(json);
            var member = (JObject)root["types"][0]["members"][0];
            Check(member.Value<bool>("isArray") && member.Value<string>("elementTypeName") == "float",
                "134-17-F1: descriptor JSON preserves array metadata");
            Check(member.Value<float>("rateHz") == 0f
                  && member.Value<float>("changeEpsilon") == 0f
                  && member.Value<float>("forceIntervalSeconds") == 0f,
                "134-17-F2: descriptor JSON writes finite float values");

            var reread = FoxRunGenerationDescriptorJsonReader.Read(json);
            var comparison = FoxRunGenerationDescriptorComparer.Compare(model, reread);
            Check(comparison.IsSemanticEqual,
                "134-17-F3: descriptor reader round-trips array metadata for semantic comparison");

            var duplicates = new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", "DuplicateProbe", new[]
                {
                    Member("Demo", "DuplicateProbe", "value", "/same", "float"),
                    Member("Demo", "DuplicateProbe", "value", "/same", "double")
                })
            });
            Check(FoxRunGenerationDescriptorComparer.Compare(duplicates, duplicates)
                .SemanticDifferences.Any(diff => diff.Contains("Duplicate", StringComparison.Ordinal)),
                "134-17-F4: descriptor comparer reports duplicate flattened keys");
            Check(comparison.IsProvenanceEqual,
                "134-17-F5: descriptor comparison exposes provenance equality");
        }

        private static void VerifyModelAndManifestValidationHardening()
        {
            var explicitCanonical = new FoxRunGenerationMember(
                "Demo",
                "CanonicalProbe",
                "value",
                "field",
                "float",
                "float",
                "System.Single",
                true,
                false,
                string.Empty,
                "/demo/value",
                1f,
                string.Empty,
                0,
                0f,
                0f,
                "Test",
                0,
                string.Empty);
            Check(explicitCanonical.CanonicalType == "float32",
                "134-17-G1: explicit canonical type constructor input is normalized");

            var invalidDiagnostics = FoxRunGenerationModelValidator.Validate(new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType("Demo", string.Empty, new[]
                {
                    new FoxRunGenerationMember(
                        "Demo",
                        string.Empty,
                        string.Empty,
                        "field",
                        "float",
                        true,
                        false,
                        string.Empty,
                        "/demo/value",
                        1f,
                        string.Empty,
                        99,
                        0f,
                        0f,
                        "Test",
                        0,
                        string.Empty)
                })
            }));
            Check(invalidDiagnostics.Any(d => d.Id == "FOXRUN011" && d.Severity == "Error")
                  && invalidDiagnostics.Any(d => d.Id == "FOXRUN012" && d.Severity == "Error")
                  && invalidDiagnostics.Any(d => d.Id == "FOXRUN013" && d.Severity == "Error"),
                "134-17-G2: validator rejects missing identifiers and out-of-range publish modes");

            var arrayManifest = FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "ManifestProbe",
                    "values",
                    "field",
                    "float[]",
                    false,
                    true,
                    string.Empty,
                    "/demo/values",
                    1f,
                    string.Empty,
                    0,
                    0f,
                    0f)
            });
            var arrayField = arrayManifest.Sections.FoxRun.Types[0].Contracts[0].Fields[0];
            Check(arrayField.Type == "float32" && arrayField.Array,
                "134-17-G3: manifest array fallback strips array suffix before canonical normalization");

            var metadataHashA = FoxRunManifestBuilder.Build(Array.Empty<FoxRunManifestMember>(), manifestVersion: 1);
            var metadataHashB = FoxRunManifestBuilder.Build(Array.Empty<FoxRunManifestMember>(), manifestVersion: 2);
            Check(metadataHashA.Sections.FoxRun.ManifestHash == metadataHashB.Sections.FoxRun.ManifestHash
                  && metadataHashA.GlobalManifestHash != metadataHashB.GlobalManifestHash,
                "134-17-G4: FoxRun global hash includes manifest metadata while section hash stays contract-scoped");

            CheckThrows<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[]
                {
                    new FoxRunManifestMember("Demo", "CollisionProbe", "_", "field", "float", true, false, string.Empty, "/demo/a", 1f, string.Empty, 0, 0f, 0f)
                }),
                "134-17-G5: manifest builder rejects empty JSON field names");

            CheckThrows<InvalidOperationException>(() => FoxRunManifestBuilder.Build(new[]
                {
                    new FoxRunManifestMember("Demo", "CollisionProbe", "__value", "field", "float", true, false, string.Empty, "/demo/a", 1f, string.Empty, 0, 0f, 0f),
                    new FoxRunManifestMember("Demo", "CollisionProbe", "value", "field", "float", true, false, string.Empty, "/demo/a", 1f, string.Empty, 0, 0f, 0f)
                }),
                "134-17-G6: manifest builder rejects colliding JSON field names");
        }

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
                    new[] { new Unity2FoxgloveSdkTypedPublisherEntry("Publisher", "typed", "family", "/topic", "foxglove.Log", "", true, true, false, false, string.Empty) }),
                "134-17-H2: SDK typed publishers section validates entryCount consistency");

            var builder = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            Check(builder.Contains("entry.SupportsJson || entry.SupportsProtobuf", StringComparison.Ordinal)
                  && builder.Contains("entry.SupportsRos2", StringComparison.Ordinal),
                "134-17-H3: schema manifest builder validates supports flags against schema names");

            var writer = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestWriter.cs");
            Check(writer.Contains("TryReadExistingBytes", StringComparison.Ordinal)
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
                        0,
                        0f,
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
            string typeName)
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
                1f,
                string.Empty,
                0,
                0f,
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
