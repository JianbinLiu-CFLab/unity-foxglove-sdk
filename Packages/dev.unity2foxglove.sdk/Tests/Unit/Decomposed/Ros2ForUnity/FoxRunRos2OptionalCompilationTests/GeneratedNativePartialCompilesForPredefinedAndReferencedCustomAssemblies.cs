// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Pins the focused Phase179 optional compilation lanes and source-only R2FU stubs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{


public sealed partial class FoxRunRos2OptionalCompilationTests
    {
        [Fact]
        public void GeneratedNativePartialCompilesForPredefinedAndReferencedCustomAssemblies()
        {
            var predefined = CompileGeneratedFixture("Assembly-CSharp", includeNativeReference: true);
            var customReferenced = CompileGeneratedFixture("Demo.Custom.Runtime", includeNativeReference: true);
            var customMissing = CompileGeneratedFixture("Demo.Custom.MissingNative", includeNativeReference: false);

            Assert.DoesNotContain(predefined.GeneratorDiagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.DoesNotContain(customReferenced.GeneratorDiagnostics, diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.Empty(predefined.CompilerErrors);
            Assert.Empty(customReferenced.CompilerErrors);

            Assert.DoesNotContain(
                customMissing.GeneratorDiagnostics,
                diagnostic => diagnostic.Id == "FOXRUN212");
            Assert.Empty(customMissing.CompilerErrors);
            Assert.DoesNotContain(
                customMissing.GeneratedSource,
                "IFoxRunRos2SubscriptionSource",
                StringComparison.Ordinal);
        }

        [Fact]
        public void PublishOnlyR2fuContractRequiresTheNativeAssemblyReference()
        {
            var missing = CompilePublishOnlyCustomFixtureWithoutNativeReference();

            Assert.Contains(
                missing.GeneratorDiagnostics,
                diagnostic => diagnostic.Id == "FOXR2F007");
            Assert.DoesNotContain(
                "IFoxRunRos2CustomPublisherSource",
                missing.GeneratedSource,
                StringComparison.Ordinal);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void NativeFoxRunShutdownHooksAreReflectionDiscoverable()
        {
            const string methodName = "StopForNativeRuntimeShutdown";
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.NonPublic;

            Assert.NotNull(
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionHub)
                    .GetMethod(methodName, flags));
            Assert.NotNull(
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2CustomPublisherHub)
                    .GetMethod(methodName, flags));
        }

        [Fact]
        public void NativeLaneCompiledNamedPhase179TypesAndDefine()
        {
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.IFoxRunRos2SubscriptionRegistrar).FullName);
            Assert.Equal(
                "ROS2.ROS2UnityComponent",
                typeof(ROS2.ROS2UnityComponent).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionHub",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionHub).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.Ros2ForUnityFoxRunInboundBackend",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.Ros2ForUnityFoxRunInboundBackend).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionDiagnostics",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2SubscriptionDiagnostics).FullName);
            Assert.Equal(
                "Unity2Foxglove.Ros2ForUnity.Native",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract)
                    .Assembly.GetName().Name);
            var completeConstructor = typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract)
                .GetConstructors()
                .Single(constructor => constructor.GetParameters().Length == 22);
            Assert.Equal(
                new[]
                {
                    typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                    typeof(Unity.FoxgloveSDK.Components.FoxRunFlow),
                    typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2RouteEndpoint),
                    typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosProfile),
                    typeof(bool),
                    typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosReliability),
                    typeof(bool),
                    typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosDurability),
                    typeof(bool),
                    typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunQosHistory),
                    typeof(bool),
                    typeof(int),
                    typeof(bool),
                    typeof(bool),
                    typeof(Unity.FoxgloveSDK.Components.FoxRunPolicy),
                    typeof(float),
                    typeof(bool),
                    typeof(float)
                },
                completeConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        }
#else
        [Fact]
        public void NonNativeLaneKeepsTheUnitTestAssemblyIdentity()
        {
            Assert.Equal("FoxgloveSdk.UnitTests", typeof(FoxRunRos2OptionalCompilationTests).Assembly.GetName().Name);
        }
#endif

        private static string[] RelevantSignatures(string source)
        {
            var root = CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(
                    LanguageVersion.CSharp9,
                    preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" }))
                .GetCompilationUnitRoot();
            var signatures = new List<string>();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (type.Identifier.ValueText != "ROS2UnityComponent"
                    && type.Identifier.ValueText != "ROS2Node")
                    continue;

                if (type.Identifier.ValueText == "ROS2Node")
                {
                    foreach (var constructor in type.Members.OfType<ConstructorDeclarationSyntax>())
                    {
                        var accessibility = string.Join(
                            ",",
                            constructor.Modifiers
                                .Where(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)
                                                   || modifier.IsKind(SyntaxKind.InternalKeyword)
                                                   || modifier.IsKind(SyntaxKind.ProtectedKeyword)
                                                   || modifier.IsKind(SyntaxKind.PrivateKeyword))
                                .Select(modifier => modifier.ValueText));
                        var parameters = string.Join(",", constructor.ParameterList.Parameters.Select(parameter =>
                            Normalize(parameter.Type?.ToString())
                            + ":" + parameter.Identifier.ValueText
                            + (parameter.Default == null
                                ? string.Empty
                                : "=" + Normalize(parameter.Default.Value.ToString()))));
                        signatures.Add(
                            type.Identifier.ValueText + ".ctor[" + accessibility + "](" + parameters + ")");
                    }
                }

                foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                {
                    if (!method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
                        continue;
                    if (!IsRequiredMethod(type.Identifier.ValueText, method))
                        continue;

                    var parameters = string.Join(",", method.ParameterList.Parameters.Select(parameter =>
                        Normalize(parameter.Type?.ToString())
                        + (parameter.Default == null ? string.Empty : "=" + Normalize(parameter.Default.Value.ToString()))));
                    var constraints = string.Join(",", method.ConstraintClauses.Select(clause => Normalize(clause.ToString())));
                    signatures.Add(
                        type.Identifier.ValueText + "." + method.Identifier.ValueText
                        + "`" + (method.TypeParameterList?.Parameters.Count ?? 0)
                        + "(" + parameters + "):" + Normalize(method.ReturnType.ToString())
                        + ":" + constraints);
                }
            }

            return signatures.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        private static CompilationFixtureResult CompileGeneratedFixture(
            string assemblyName,
            bool includeNativeReference)
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[] { "UNITY2FOXGLOVE_ROS2_FOR_UNITY" });
            var source = @"
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class Receiver
    {
        [Unity.FoxgloveSDK.Components.FoxRun(""/native/string"",
            Mode = Unity.FoxgloveSDK.Components.FoxRunFlow.Subscribe,
            SubscribeTransportId = ""unity2foxglove.r2fu"",
            Reliability = Unity.FoxgloveSDK.Components.FoxRunDeliveryReliability.BestEffort,
            SchemaName = ""std_msgs/msg/String"")]
        private std_msgs.msg.String _incoming;
    }
}";
            var coreReference = BuildCoreAttributeAssemblyReference();
            var references = PlatformReferences()
                .Concat(new[]
                {
                    coreReference,
                    JazzyReference("ros2cs_common.dll"),
                    JazzyReference("std_msgs_assembly.dll")
                })
                .Concat(includeNativeReference
                    ? new[] { BuildNativeSeamReference(parseOptions, coreReference) }
                    : Array.Empty<MetadataReference>())
                .ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness.FoxRunAnalyzerTestComposition
                    .LegacyCombined(),
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generatorDiagnostics);
            var runResult = driver.GetRunResult();
            var generatedSource = string.Join(
                Environment.NewLine,
                runResult.Results.SelectMany(result => result.GeneratedSources)
                    .Select(result => result.SourceText.ToString()));
            var compilerErrors = outputCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                     && !diagnostic.Id.StartsWith("FOXRUN", StringComparison.Ordinal))
                .ToArray();
            return new CompilationFixtureResult(
                runResult.Diagnostics.Concat(generatorDiagnostics)
                    .GroupBy(diagnostic => diagnostic.Id + diagnostic.Location + diagnostic.GetMessage(), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray(),
                compilerErrors,
                generatedSource);
        }

        private static CompilationFixtureResult CompilePublishOnlyCustomFixtureWithoutNativeReference()
        {
            var parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp9,
                preprocessorSymbols: new[]
                {
                    "UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                    "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES"
                });
            var source = @"
namespace Demo
{
    public sealed class State
    {
        public int Value { get; set; }
    }

    public partial class Publisher
    {
        [Unity.FoxgloveSDK.Components.FoxRun(""/phase187/r2fu/publish-only"",
            Mode = Unity.FoxgloveSDK.Components.FoxRunFlow.Publish,
            PublishTransportIds = new[] { ""unity2foxglove.r2fu"" })]
        public State Outgoing { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Demo.Custom.MissingNativePublish",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                PlatformReferences().Concat(new[]
                {
                    BuildCoreAttributeAssemblyReference()
                }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                Unity.FoxgloveSDK.UnitTests.Harness.FoxRunAnalyzerTestComposition
                    .LegacyCombined(),
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out var outputCompilation,
                out var generatorDiagnostics);
            var runResult = driver.GetRunResult();
            var generatedSource = string.Join(
                Environment.NewLine,
                runResult.Results.SelectMany(result => result.GeneratedSources)
                    .Select(result => result.SourceText.ToString()));
            var compilerErrors = outputCompilation.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                     && !diagnostic.Id.StartsWith("FOXRUN", StringComparison.Ordinal)
                                     && !diagnostic.Id.StartsWith("FOXR2F", StringComparison.Ordinal))
                .ToArray();
            return new CompilationFixtureResult(
                runResult.Diagnostics.Concat(generatorDiagnostics)
                    .GroupBy(
                        diagnostic => diagnostic.Id + diagnostic.Location + diagnostic.GetMessage(),
                        StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToArray(),
                compilerErrors,
                generatedSource);
        }

        private static MetadataReference BuildNativeSeamReference(
            CSharpParseOptions parseOptions,
            MetadataReference coreReference)
        {
            var nativeRoot =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/";
            var trees = new[]
                {
                    "IFoxRunRos2SubscriptionSource.cs",
                    "IFoxRunRos2SubscriptionRegistrar.cs",
                    "FoxRunRos2CopyBudget.cs",
                    "FoxRunRos2GeneratedContract.cs"
                }
                .Select(file => CSharpSyntaxTree.ParseText(
                    Text(nativeRoot + file),
                    parseOptions))
                .Concat(new[]
                {
                    CSharpSyntaxTree.ParseText(
                        Text(
                            "Packages/dev.unity2foxglove.ros2forunity/"
                            + "Runtime/FoxRunRos2Qos.cs"),
                        parseOptions),
                    CSharpSyntaxTree.ParseText(
                        "namespace Unity2Foxglove.Ros2ForUnity.Native"
                        + "{ public enum FoxRunRos2RouteEndpoint"
                        + "{ WebSocket = 1, R2fu = 2 } }",
                        parseOptions)
                });
            var compilation = CSharpCompilation.Create(
                "Unity2Foxglove.Ros2ForUnity.Native",
                trees,
                PlatformReferences().Concat(new[]
                {
                    coreReference,
                    JazzyReference("ros2cs_common.dll")
                }),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return MetadataReference.CreateFromImage(image.ToArray());
        }

        private static MetadataReference JazzyReference(string fileName)
            => MetadataReference.CreateFromFile(Path.Combine(
                FindRepoRoot(),
                "Packages",
                "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
                "Runtime",
                "Ros2ForUnity",
                "Plugins",
                fileName));

        private static MetadataReference BuildCoreAttributeAssemblyReference()
        {
            var attributeRoot = Path.Combine(
                FindRepoRoot(), "Packages", "dev.unity2foxglove.sdk", "Runtime", "Components", "Attributes");
            var trees = new[]
                {
                    "FoxRunAttribute.cs",
                    "FoxRunFlow.cs",
                    "FoxRunPolicy.cs",
                    Path.Combine("..", "..", "Utilities", "FoxRunUpdatePolicy.cs"),
                    "FoxRunEncoding.cs",
                    Path.Combine("..", "FoxRun", "Transport", "FoxRunTransportId.cs"),
                    Path.Combine("..", "FoxRun", "Transport", "FoxRunTransportContracts.cs"),
                    Path.Combine("..", "FoxRun", "Transport", "FoxRunGeneratedMemberAccess.cs")
                }
                .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(attributeRoot, file))));
            var compilation = CSharpCompilation.Create(
                "Unity.FoxgloveSDK.FoxRunContractFixture",
                trees,
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return MetadataReference.CreateFromImage(image.ToArray());
        }

        private static MetadataReference[] PlatformReferences()
            => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => !string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(typeof(FoxRunRos2OptionalCompilationTests).Assembly.Location),
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => MetadataReference.CreateFromFile(path))
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        private static bool IsRequiredMethod(string typeName, MethodDeclarationSyntax method)
        {
            var parameterCount = method.ParameterList.Parameters.Count;
            if (typeName == "ROS2UnityComponent")
            {
                return (method.Identifier.ValueText == "Ok" && parameterCount == 0)
                       || (method.Identifier.ValueText == "CreateNode" && parameterCount == 1)
                       || (method.Identifier.ValueText == "RemoveNode" && parameterCount == 1);
            }

            return (method.Identifier.ValueText == "CreateSubscription"
                    && method.TypeParameterList?.Parameters.Count == 1
                    && parameterCount == 3)
                   || (method.Identifier.ValueText == "RemoveSubscription"
                       && method.TypeParameterList == null
                       && parameterCount == 1);
        }

        private static string Normalize(string value)
            => new string((value ?? string.Empty).Where(character => !char.IsWhiteSpace(character)).ToArray());

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private sealed class CompilationFixtureResult
        {
            public CompilationFixtureResult(
                Diagnostic[] generatorDiagnostics,
                Diagnostic[] compilerErrors,
                string generatedSource)
            {
                GeneratorDiagnostics = generatorDiagnostics;
                CompilerErrors = compilerErrors;
                GeneratedSource = generatedSource;
            }

            public Diagnostic[] GeneratorDiagnostics { get; }
            public Diagnostic[] CompilerErrors { get; }
            public string GeneratedSource { get; }
        }
    }
}
