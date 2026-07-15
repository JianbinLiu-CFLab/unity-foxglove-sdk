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
    [Trait("Phase", "179-B")]
    [Trait("Domain", "OptionalCompilation")]
    public sealed class FoxRunRos2OptionalCompilationTests
    {
        private const string StubPath =
            "Packages/dev.unity2foxglove.sdk/Tests/NativeCompileStubs/Ros2ForUnityNativeCompileStubs.cs";

        [Fact]
        public void NativeLaneIsFocusedDefinedAndNonVacuous()
        {
            var props = Text("Packages/dev.unity2foxglove.sdk/Tests/FoxgloveSdk.TestSurface.props");
            var unitProject = Text("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj");
            var runtimeProject = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            foreach (var project in new[] { unitProject, runtimeProject })
            {
                Assert.Contains("IncludeRos2ForUnityNative", project, StringComparison.Ordinal);
                Assert.Contains("UNITY2FOXGLOVE_ROS2_FOR_UNITY", project, StringComparison.Ordinal);
                Assert.Contains("Ros2ForUnityNativeCompileStubs.cs", project, StringComparison.Ordinal);
                Assert.Contains("ValidatePhase179NativeCompileSurface", project, StringComparison.Ordinal);
                Assert.Contains("Phase179OptionalCompilationLane", project, StringComparison.Ordinal);
                Assert.Contains("<OutputPath>", project, StringComparison.Ordinal);
                Assert.Contains("<IntermediateOutputPath>", project, StringComparison.Ordinal);
                Assert.Contains("Unity2Foxglove.Ros2ForUnity.Native</AssemblyName>", project, StringComparison.Ordinal);
            }

            Assert.Contains("Compile Remove=", props, StringComparison.Ordinal);
            Assert.Contains("/Runtime/Native/**/*.cs", props.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("/Runtime/Native/FoxRun/**/*.cs", props.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains("Ros2ForUnityNativeBridgeLifecycleGate.cs", props, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "<Compile Include=\"../NativeCompileStubs/**/*.cs\"",
                unitProject,
                StringComparison.Ordinal);
        }

        [Fact]
        public void CompileOnlyR2fuStubsMatchAllPackagedSourceSignatures()
        {
            var expected = RelevantSignatures(Text(StubPath));
            Assert.NotEmpty(expected);
            Assert.Contains(
                "ROS2Node.ctor[internal](string:unityROS2NodeName=DefaultNodeName)",
                expected);
            Assert.DoesNotContain(
                expected,
                signature => string.Equals(
                    signature,
                    "ROS2Node.ctor[public]()",
                    StringComparison.Ordinal));

            foreach (var distro in new[] { "humble", "jazzy", "lyrical" })
            {
                var scripts = "Packages/dev.unity2foxglove.ros2forunity.runtime."
                              + distro + ".win64/Runtime/Ros2ForUnity/Scripts/";
                var actual = RelevantSignatures(
                    Text(scripts + "ROS2UnityComponent.cs")
                    + Environment.NewLine
                    + Text(scripts + "ROS2Node.cs"));
                Assert.Equal(expected, actual);
            }
        }

        [Fact]
        public void CoreProjectsDoNotReferenceConcreteRos2Assemblies()
        {
            foreach (var path in new[]
                     {
                         "Packages/dev.unity2foxglove.sdk/Runtime/Unity.FoxgloveSDK.asmdef",
                         "Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef",
                         "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
                     })
            {
                var source = Text(path);
                Assert.DoesNotContain("ros2cs_common", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ros2cs_core", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("_msgs_assembly", source, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Unity2Foxglove.Ros2ForUnity", source, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void GeneratedNativePartialCompilesForPredefinedAndReferencedCustomAssemblies()
        {
            var predefined = CompileGeneratedFixture("Assembly-CSharp", includeNativeReference: true);
            var customReferenced = CompileGeneratedFixture("Demo.Custom.Runtime", includeNativeReference: true);
            var customMissing = CompileGeneratedFixture("Demo.Custom.MissingNative", includeNativeReference: false);

            Assert.DoesNotContain(predefined.GeneratorDiagnostics, diagnostic => diagnostic.Id == "FOXRUN043");
            Assert.DoesNotContain(customReferenced.GeneratorDiagnostics, diagnostic => diagnostic.Id == "FOXRUN043");
            Assert.Empty(predefined.CompilerErrors);
            Assert.Empty(customReferenced.CompilerErrors);

            Assert.Contains(customMissing.GeneratorDiagnostics, diagnostic => diagnostic.Id == "FOXRUN043");
            Assert.Empty(customMissing.CompilerErrors);
            Assert.DoesNotContain(
                customMissing.GeneratedSource,
                "IFoxRunRos2SubscriptionSource",
                StringComparison.Ordinal);
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
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
                "Unity2Foxglove.Ros2ForUnity.Native",
                typeof(Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2GeneratedContract)
                    .Assembly.GetName().Name);
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
            Mode = Unity.FoxgloveSDK.Components.FoxRunMode.SubscribeOnly,
            SubscriptionProvider = Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider.Ros2Native,
            Ros2Qos = Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.SensorData,
            SchemaName = ""std_msgs/msg/String"")]
        private std_msgs.msg.String _incoming;
    }
}";
            var references = PlatformReferences()
                .Concat(new[]
                {
                    BuildCoreAttributeAssemblyReference(),
                    JazzyReference("ros2cs_common.dll"),
                    JazzyReference("std_msgs_assembly.dll")
                })
                .Concat(includeNativeReference
                    ? new[] { BuildNativeSeamReference(parseOptions) }
                    : Array.Empty<MetadataReference>())
                .ToArray();
            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
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

        private static MetadataReference BuildNativeSeamReference(CSharpParseOptions parseOptions)
        {
            var nativeRoot =
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/";
            var trees = new[]
                {
                    "IFoxRunRos2SubscriptionSource.cs",
                    "IFoxRunRos2SubscriptionRegistrar.cs",
                    "FoxRunRos2GeneratedContract.cs"
                }
                .Select(file => CSharpSyntaxTree.ParseText(Text(nativeRoot + file), parseOptions));
            var compilation = CSharpCompilation.Create(
                "Unity2Foxglove.Ros2ForUnity.Native",
                trees,
                PlatformReferences().Concat(new[] { JazzyReference("ros2cs_common.dll") }),
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
                    Path.Combine("..", "..", "Utilities", "FoxRunPublishPolicy.cs"),
                    "FoxRunWireEncoding.cs",
                    "FoxRunSubscriptionProvider.cs",
                    "FoxRunRos2QosPreset.cs"
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
