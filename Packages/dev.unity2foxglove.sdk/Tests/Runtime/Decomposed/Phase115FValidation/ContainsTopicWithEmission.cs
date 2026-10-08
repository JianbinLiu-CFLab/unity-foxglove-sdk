// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Hardens FoxRun generation-model equivalence after Phase 115E.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase115FValidation.
    /// </summary>
    public static partial class Phase115FValidation
    {
        private static bool ContainsTopicWithEmission(FoxRunGenerationModel model, string topic, string emissionTypeName)
        {
            return model.Types
                .SelectMany(type => type.Members)
                .Any(member => string.Equals(member.Topic, topic, StringComparison.Ordinal)
                               && string.Equals(member.EmissionTypeName, emissionTypeName, StringComparison.Ordinal));
        }

        private static string ExtractDescriptorJsonFromRoslyn()
        {
            var descriptorSource = GeneratedSourceText(RunRoslynGenerator(), "FoxRunGeneratedDescriptorInfo.g.cs");
            var match = Regex.Match(descriptorSource, "DescriptorJson = \"(?<json>.*)\";");
            if (!match.Success)
                throw new InvalidOperationException("Could not extract DescriptorJson from Roslyn descriptor carrier.");
            return Regex.Unescape(match.Groups["json"].Value);
        }

        private static string ExtractFoxRunSourceFromRoslyn()
            => GeneratedFoxRunSource(RunRoslynGenerator());

        private static IReadOnlyList<GeneratedSourceResult> RunRoslynGenerator()
            => RunGenerator(new FoxgloveLogSourceGenerator(), "FoxRunGenerationModelFixtureRoslyn115F");

        private static IReadOnlyList<GeneratedSourceResult> RunGenerator(IIncrementalGenerator generator, string assemblyName)
        {
            var compilation = CreateCompilation(ReadRepoText(FixtureRelativePath), assemblyName);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { generator.AsSourceGenerator() },
                parseOptions: FixtureParseOptions());
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
            var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            return driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).ToList();
        }

        private static IIncrementalGenerator LoadGeneratorFromDll(string dllPath)
        {
            var assembly = Assembly.Load(File.ReadAllBytes(Path.GetFullPath(dllPath)));
            var type = assembly.GetType("Unity.FoxgloveSDK.SourceGenerators.FoxgloveLogSourceGenerator")
                       ?? throw new InvalidOperationException("Checked-in analyzer DLL does not contain FoxgloveLogSourceGenerator.");
            return (IIncrementalGenerator)Activator.CreateInstance(type);
        }

        private static string GeneratedSourceText(IReadOnlyList<GeneratedSourceResult> sources, string hintName)
        {
            var source = sources.FirstOrDefault(candidate => candidate.HintName == hintName);
            if (source.HintName == null)
                throw new InvalidOperationException("Generated source is missing expected hint: " + hintName);
            return source.SourceText.ToString();
        }

        private static string GeneratedFoxRunSource(IReadOnlyList<GeneratedSourceResult> sources)
        {
            var source = sources.FirstOrDefault(candidate =>
                candidate.HintName.EndsWith("_FoxRun.g.cs", StringComparison.Ordinal));
            if (source.HintName == null)
                throw new InvalidOperationException("Generated sources do not contain an expected *_FoxRun.g.cs hint.");
            return source.SourceText.ToString();
        }

        private static FoxRunGenerationModel BuildReflectionModelFromFixtureAssembly()
        {
            var compilation = CreateCompilation(ReadRepoText(FixtureRelativePath), "FoxRunGenerationModelFixtureReflection115F");
            var outputPath = Path.Combine(Path.GetTempPath(), "foxrun_generation_model_115f_" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                using (var stream = File.Create(outputPath))
                {
                    var emit = compilation.Emit(stream);
                    if (!emit.Success)
                    {
                        var errors = string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                        throw new InvalidOperationException(errors);
                    }
                }

                var assembly = Assembly.Load(File.ReadAllBytes(outputPath));
                var type = assembly.GetType("Unity.FoxgloveSDK.Tests.Fixtures.FoxRunGenerationModelFixture")
                           ?? throw new InvalidOperationException("Missing reflection fixture type.");
                var members = new List<FoxRunReflectionGenerationMember>();
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var field in type.GetFields(flags))
                    AddReflectionMembers(type, field, "field", field.FieldType, field.MetadataToken, members);
                foreach (var property in type.GetProperties(flags))
                    AddReflectionMembers(type, property, "property", property.PropertyType, property.MetadataToken, members);
                return FoxRunReflectionGenerationModelLowerer.Lower(members);
            }
            finally
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                catch { /* Best-effort cleanup only. */ }
            }
        }

        private static void AddReflectionMembers(
            Type declaringType,
            MemberInfo member,
            string memberKind,
            Type memberType,
            int rawMemberOrder,
            List<FoxRunReflectionGenerationMember> members)
        {
            foreach (var attribute in CustomAttributeData.GetCustomAttributes(member)
                         .Where(data => data.AttributeType.FullName == typeof(FoxRunAttribute).FullName))
            {
                var topic = (string)attribute.ConstructorArguments[0].Value;
                var hz = -1f;
                var schemaName = string.Empty;
                var policy = (int)FoxRunPolicy.FixedRate;
                var tolerance = 0f;
                var presence = FoxRunNamedArgumentPresence.None;
                foreach (var argument in attribute.NamedArguments)
                {
                    switch (argument.MemberName)
                    {
                        case "Hz":
                            hz = Convert.ToSingle(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Hz;
                            break;
                        case "SchemaName":
                            schemaName = (string)argument.TypedValue.Value ?? string.Empty;
                            presence |= FoxRunNamedArgumentPresence.SchemaName;
                            break;
                        case "Policy":
                            policy = Convert.ToInt32(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Policy;
                            break;
                        case "Tolerance":
                            tolerance = Convert.ToSingle(argument.TypedValue.Value);
                            presence |= FoxRunNamedArgumentPresence.Tolerance;
                            break;
                    }
                }
                var isArray = TryGetArrayElementType(memberType, out var elementType);
                members.Add(new FoxRunReflectionGenerationMember(
                    declaringType.Namespace ?? string.Empty,
                    declaringType.Name,
                    member.Name,
                    memberKind,
                    memberType.FullName ?? memberType.Name,
                    FoxRunEmissionTypeNameFormatter.FromReflectionType(memberType),
                    isValueType: memberType.IsValueType,
                    isArray: isArray,
                    elementTypeName: elementType == null ? string.Empty : elementType.FullName ?? elementType.Name,
                    topic: topic,
                    schemaName: schemaName,
                    hz: hz,
                    policy: policy,
                     tolerance: tolerance,
                     rawMemberOrder: rawMemberOrder,
                     conditionalSymbols: "FOXRUN_FIXTURE_EXTRA",
                     typeShape: FoxRunReflectionTypeShapeBuilder.Build(memberType),
                     namedArgumentPresence: presence));
            }
        }

        private static bool TryGetArrayElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return elementType != null;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>))
                {
                    elementType = type.GetGenericArguments()[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }

        private static CSharpCompilation CreateCompilation(string source, string assemblyName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, FixtureParseOptions());
            return CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static CSharpParseOptions FixtureParseOptions()
            => CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp9)
                .WithPreprocessorSymbols("FOXRUN_FIXTURE_EXTRA");

        private static MetadataReference[] CreateReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase115F Roslyn reference resolution.");

            var trusted = trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>();
            var local = new[]
            {
                MetadataReference.CreateFromFile(typeof(FoxRunAttribute).Assembly.Location)
            };
            return trusted.Concat(local).ToArray();
        }

        private static string NormalizeNewlines(string value)
            => value.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
            => Check(condition, name, string.Empty);

        private static void Check(bool condition, string name, string details)
        {
            if (!condition)
                throw new InvalidOperationException(string.IsNullOrEmpty(details) ? name : name + " :: " + details);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
