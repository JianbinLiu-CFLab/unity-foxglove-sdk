// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Util;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunDeclarationModelTests
    {
        private static GeneratorDriverRunResult RunGenerator(params string[] sources)
        {
            var compilation = CreateCompilation(sources);

            return RunGenerator(compilation);
        }

        private static GeneratorDriverRunResult RunGenerator(
            CSharpParseOptions parseOptions,
            params string[] sources)
        {
            var compilation = CreateCompilation(parseOptions, sources);

            return RunGenerator(compilation);
        }

        private static GeneratorDriverRunResult RunGenerator(
            CSharpCompilation compilation)
        {
            GeneratorDriver driver = CreateGeneratorDriver(
                compilation.SyntaxTrees.FirstOrDefault()?.Options
                    as CSharpParseOptions);
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static GeneratorDriver CreateGeneratorDriver(
            CSharpParseOptions parseOptions)
        {
            var sourceGenerator =
                new FoxgloveLogSourceGenerator().AsSourceGenerator();
            return CSharpGeneratorDriver.Create(
                new[] { sourceGenerator },
                additionalTexts: null,
                parseOptions: parseOptions,
                optionsProvider: null,
                driverOptions: default);
        }

        private static void AssertGeneratedSourcesParse(
            GeneratorDriverRunResult result,
            CSharpParseOptions parseOptions)
        {
            var generated = result.Results
                .SelectMany(run => run.GeneratedSources)
                .ToArray();
            Assert.NotEmpty(generated);
            foreach (var source in generated)
            {
                var tree = CSharpSyntaxTree.ParseText(
                    source.SourceText.ToString(),
                    parseOptions ?? CSharpParseOptions.Default);
                Assert.DoesNotContain(
                    tree.GetDiagnostics(),
                    diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            }
        }

        private static GeneratorDriverRunResult RunGeneratorWithR2fu(
            params string[] sources)
        {
            var compilation = CreateCompilation(sources);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.CoreAndR2fu());
            driver = driver.RunGenerators(compilation);
            return driver.GetRunResult();
        }

        private static string GeneratedDescriptor(GeneratorDriverRunResult result)
            => result.Results
                .Single()
                .GeneratedSources
                .Single(source => source.HintName == "FoxRunGeneratedDescriptorInfo.g.cs")
                .SourceText
                .ToString();

        private static string GeneratedDescriptorJson(GeneratorDriverRunResult result)
        {
            var descriptorSource = CSharpSyntaxTree.ParseText(GeneratedDescriptor(result));
            var descriptorVariable = descriptorSource
                .GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Single(variable => variable.Identifier.ValueText == "DescriptorJson");
            var literal = Assert.IsType<LiteralExpressionSyntax>(
                descriptorVariable.Initializer?.Value);
            return literal.Token.ValueText;
        }

        private static Unity.FoxgloveSDK.SourceGenerators.MemberData ExtractRoslynMemberData(
            string source,
            string memberName = null)
        {
            var compilation = CreateCompilation(source);
            var fields = compilation.SyntaxTrees
                .SelectMany(tree => tree.GetRoot().DescendantNodes())
                .OfType<FieldDeclarationSyntax>()
                .ToArray();
            var field = string.IsNullOrEmpty(memberName)
                ? fields.Single()
                : fields.Single(candidate =>
                    candidate.Declaration.Variables.Any(variable =>
                        variable.Identifier.ValueText == memberName));
            var constructor = typeof(GeneratorSyntaxContext).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(SyntaxNode), typeof(SemanticModel) },
                modifiers: null);
            Assert.NotNull(constructor);
            var context = (GeneratorSyntaxContext)constructor.Invoke(
                new object[] { field, compilation.GetSemanticModel(field.SyntaxTree) });
            var extract = typeof(FoxgloveLogSourceGenerator).GetMethod(
                "ExtractMember",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(extract);
            return Assert.IsType<Unity.FoxgloveSDK.SourceGenerators.MemberData>(
                extract.Invoke(
                    null,
                    new object[] { context, System.Threading.CancellationToken.None }));
        }

        private static Compilation RunGeneratorAndUpdateCompilation(string source)
        {
            return RunGeneratorAndUpdateCompilation(
                CSharpParseOptions.Default,
                source);
        }

        private static Compilation RunGeneratorAndUpdateCompilation(
            CSharpParseOptions parseOptions,
            string source)
        {
            var compilation = CreateCompilation(parseOptions, source);
            GeneratorDriver driver = CreateGeneratorDriver(parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
            return outputCompilation;
        }

        private static Compilation RunGeneratorAndUpdateCompilationWithR2fu(
            string source)
        {
            var compilation = CreateCompilation(source);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness
                    .FoxRunAnalyzerTestComposition.CoreAndR2fu());
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out _);
            return outputCompilation;
        }

        private static CSharpCompilation CreateCompilation(params string[] sources)
        {
            return CreateCompilation(CSharpParseOptions.Default, sources);
        }

        private static CSharpCompilation CreateCompilation(
            CSharpParseOptions parseOptions,
            params string[] sources)
        {
            return CSharpCompilation.Create(
                "Phase157GeneratorProbe",
                sources.Select(source => CSharpSyntaxTree.ParseText(source, parseOptions)),
                BasicReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }

        private static string NormalizeGeneratedSource(string source)
            => (source ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

        private sealed class ReflectionArgumentsFixture
        {
            private bool Enabled => true;

            [FoxRun("/phase184/reflection/omitted")]
            public float Omitted;

            [FoxRun(
                "/phase184/reflection/invalid-policy",
                Policy = (FoxRunPolicy)99)]
            public float InvalidPolicy;

            [FoxRun(
                "/phase184/reflection/whitespace-condition",
                OnlyIf = " Enabled ")]
            public float WhitespaceCondition;

            [FoxRun(
                "/phase186/reflection/providers",
                Mode = FoxRunFlow.PublishAndSubscribe,
                PublishTransportIds = new[]
                {
                    "unity2foxglove.zeta",
                    "foxglove.websocket"
                },
                SubscribeTransportId = "unity2foxglove.alpha")]
            public float ProviderSelection;
        }

        [FoxRunMessage(
            "/phase186/reflection/aggregate",
            PublishTransportIds = new[]
            {
                "unity2foxglove.zeta",
                "foxglove.websocket"
            })]
        private sealed class ReflectionAggregateFixture
        {
            [FoxRunField("value")]
            public int Value;
        }

        private class ReflectionInheritedConditionGrandBase
        {
            protected bool ShadowedCondition => true;
        }

        private class ReflectionInheritedConditionBase : ReflectionInheritedConditionGrandBase
        {
            public bool PublicField;
            protected bool ProtectedProperty => true;
            protected internal bool ProtectedInternalMethod() => true;
            internal bool InternalField;
            private protected bool PrivateProtectedProperty => true;
            private bool PrivateBaseCondition => true;
            public new int ShadowedCondition;
        }

        private sealed class ReflectionInheritedConditionFixture : ReflectionInheritedConditionBase
        {
            private bool CurrentPrivateCondition() => true;

            [FoxRun("/phase184/reflection/public-field", OnlyIf = "PublicField")]
            public float PublicFieldProbe;

            [FoxRun("/phase184/reflection/protected-property", OnlyIf = "ProtectedProperty")]
            public float ProtectedPropertyProbe;

            [FoxRun("/phase184/reflection/protected-internal-method", OnlyIf = "ProtectedInternalMethod")]
            public float ProtectedInternalMethodProbe;

            [FoxRun("/phase184/reflection/internal-field", OnlyIf = "InternalField")]
            public float InternalFieldProbe;

            [FoxRun("/phase184/reflection/private-protected-property", OnlyIf = "PrivateProtectedProperty")]
            public float PrivateProtectedPropertyProbe;

            [FoxRun("/phase184/reflection/current-private-method", OnlyIf = nameof(CurrentPrivateCondition))]
            public float CurrentPrivateMethodProbe;

            [FoxRun("/phase184/reflection/private-base", OnlyIf = "PrivateBaseCondition")]
            public float PrivateBase;

            [FoxRun("/phase184/reflection/invalid-shadow", OnlyIf = "ShadowedCondition")]
            public float InvalidShadowProbe;
        }

        private sealed class E01ReadonlyResponse
        {
            public readonly IntPtr Handle;
            public IntPtr ReadonlyProperty { get; }
        }

        private sealed class Phase184ContextDiagnosticProbe
        {
            private readonly MethodInfo _format;

            internal Phase184ContextDiagnosticProbe(string acceptanceSource)
            {
                var declaration = CSharpSyntaxTree.ParseText(acceptanceSource)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<ClassDeclarationSyntax>()
                    .Single(type => type.Identifier.ValueText == "Phase184ContextDiagnostic")
                    .NormalizeWhitespace()
                    .ToFullString();
                var isolatedSource =
                    "using System;\n"
                    + "namespace Unity2Foxglove.ManualAcceptance\n{\n"
                    + declaration
                    + "\n}";
                var trustedAssemblies =
                    AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                    ?? string.Empty;
                var references = trustedAssemblies
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => MetadataReference.CreateFromFile(path));
                var compilation = CSharpCompilation.Create(
                    "Phase184ContextDiagnosticProbe_" + Guid.NewGuid().ToString("N"),
                    new[] { CSharpSyntaxTree.ParseText(isolatedSource) },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var image = new MemoryStream();
                var emit = compilation.Emit(image);
                Assert.True(
                    emit.Success,
                    string.Join(
                        Environment.NewLine,
                        emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
                var type = Assembly.Load(image.ToArray()).GetType(
                    "Unity2Foxglove.ManualAcceptance.Phase184ContextDiagnostic",
                    throwOnError: true);
                _format = type.GetMethod(
                    "Format",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.NotNull(_format);
            }

            internal string Format(string reason, bool isManual)
                => Assert.IsType<string>(_format.Invoke(null, new object[] { reason, isManual }));
        }
    }
}
