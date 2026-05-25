// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-17 validation for FoxRun invalid-topic fail-fast behavior.

using System;
using System.IO;
using System.Linq;
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
