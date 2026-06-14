// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates the declarative FoxService RPC runtime surface.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxServiceDeclarativeRpcValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141B Tests ---");
            _passCount = 0;

            VerifyAttributeSurface();
            VerifyRuntimeDescriptorSurface();
            VerifyHubUsesExistingServiceRegistrationPath();
            VerifyRoslynGeneratorEmitsDirectServiceWrappers();
            VerifyValidationWiring();
            VerifyPlayerFallbackGenerationPath();
            VerifyFullDemoUsesDeclarativeService();

            Console.WriteLine("Phase 141B: " + _passCount + " checks passed.\n");
        }

        private static void VerifyAttributeSurface()
        {
            var attr = new FoxServiceAttribute("/phase141b/reset");
            Check(attr.Name == "/phase141b/reset", "141B-1: FoxServiceAttribute stores service name");
            Check(attr.Type == string.Empty, "141B-2: Type defaults to empty string");
            Check(attr.Description == string.Empty, "141B-3: Description defaults to empty string");
            Check(attr.RequestSchemaName == string.Empty, "141B-4: RequestSchemaName defaults to empty string");
            Check(attr.ResponseSchemaName == string.Empty, "141B-5: ResponseSchemaName defaults to empty string");

            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(
                typeof(FoxServiceAttribute),
                typeof(AttributeUsageAttribute));
            Check(usage != null && usage.ValidOn == AttributeTargets.Method,
                "141B-6: FoxServiceAttribute targets methods");
            Check(usage != null && !usage.AllowMultiple && !usage.Inherited,
                "141B-7: FoxServiceAttribute is explicit and non-inherited");
        }

        private static void VerifyRuntimeDescriptorSurface()
        {
            JToken Handler(JToken request) => new JObject { ["ok"] = true };
            var descriptor = new FoxgloveGeneratedServiceDescriptor(
                "/phase141b/reset",
                "Phase141B.Reset",
                "Reset test service.",
                "Phase141B.Reset.Request",
                "Phase141B.Reset.Response",
                Handler);

            Check(descriptor.Name == "/phase141b/reset", "141B-8: generated descriptor carries name");
            Check(descriptor.Type == "Phase141B.Reset", "141B-9: generated descriptor carries type");
            Check(descriptor.Description == "Reset test service.", "141B-10: generated descriptor carries description");
            Check(descriptor.RequestSchemaName.EndsWith(".Request", StringComparison.Ordinal),
                "141B-11: generated descriptor carries request schema name");
            Check(descriptor.ResponseSchemaName.EndsWith(".Response", StringComparison.Ordinal),
                "141B-12: generated descriptor carries response schema name");
            Check((bool)descriptor.Handler(new JObject())["ok"], "141B-13: generated descriptor carries handler delegate");

            var source = new Phase141BServiceSource(descriptor);
            Check(source.FoxgloveServices.Count == 1, "141B-14: IFoxgloveServiceSource exposes descriptors");
        }

        private static void VerifyHubUsesExistingServiceRegistrationPath()
        {
            var hub = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxService/FoxgloveServiceHub.cs");
            var managerServices = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Services.cs");
            var sessionServices = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Services.cs");

            Check(hub.Contains("IFoxgloveServiceSource", StringComparison.Ordinal),
                "141B-15: FoxgloveServiceHub discovers generated service sources");
            Check(hub.Contains("_manager.RegisterService(ToServiceDescriptor(descriptor), descriptor.Handler)", StringComparison.Ordinal),
                "141B-16: FoxgloveServiceHub registers through FoxgloveManager.RegisterService");
            Check(hub.Contains("_manager?.UnregisterService(id)", StringComparison.Ordinal),
                "141B-17: FoxgloveServiceHub unregisters through FoxgloveManager.UnregisterService");
            Check(hub.Contains("JsonMessageEncoding = \"json\"", StringComparison.Ordinal)
                  && hub.Contains("Encoding = JsonMessageEncoding", StringComparison.Ordinal)
                  && !hub.Contains("JsonSchemaEncoding = \"jsonschema\"", StringComparison.Ordinal),
                "141B-17a: generated service descriptors advertise json payload encoding");
            Check(hub.Contains("_ownersByServiceName", StringComparison.Ordinal),
                "141B-18: FoxgloveServiceHub tracks duplicate generated service names");
            Check(!hub.Contains("MethodInfo.Invoke", StringComparison.Ordinal),
                "141B-19: FoxgloveServiceHub does not invoke services through reflection");
            Check(managerServices.Contains("RegisterService", StringComparison.Ordinal)
                  && managerServices.Contains("System.Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken>", StringComparison.Ordinal),
                "141B-20: existing manual service registration API remains available");
            Check(sessionServices.Contains("Handler exception:", StringComparison.Ordinal),
                "141B-21: existing service drain path converts handler exceptions to failures");
        }

        private static void VerifyRoslynGeneratorEmitsDirectServiceWrappers()
        {
            var generated = GeneratedFoxServiceSource(RunGenerator(ServiceFixtureSource()));
            Check(generated.Contains("global::Unity.FoxgloveSDK.Components.IFoxgloveServiceSource", StringComparison.Ordinal),
                "141B-22: Roslyn generator emits IFoxgloveServiceSource implementation");
            Check(generated.Contains("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor(\"/phase141b/reset\"", StringComparison.Ordinal),
                "141B-23: Roslyn generator emits service descriptor");
            Check(generated.Contains("var request = requestToken == null", StringComparison.Ordinal)
                  && generated.Contains("requestToken.ToObject<global::Phase141B.Request>()", StringComparison.Ordinal),
                "141B-24: Roslyn generator deserializes request DTO from JToken");
            Check(generated.Contains("var response = ResetPose(request);", StringComparison.Ordinal),
                "141B-25: Roslyn generator calls the annotated method directly");
            Check(!generated.Contains("MethodInfo.Invoke", StringComparison.Ordinal),
                "141B-26: Roslyn generated service wrapper avoids runtime reflection invocation");
            Check(generated.Contains("\"Phase141B.ServiceFixture.NestedRequest\"", StringComparison.Ordinal)
                  && generated.Contains("\"Phase141B.ServiceFixture.NestedResponse\"", StringComparison.Ordinal)
                  && !generated.Contains("Phase141B.ServiceFixture+Nested", StringComparison.Ordinal),
                "141B-26a: default nested DTO schema names match Roslyn dot notation");
            Check(generated.Contains("default(global::Phase141B.ServiceFixture.NestedRequest)", StringComparison.Ordinal)
                  && generated.Contains("requestToken.ToObject<global::Phase141B.ServiceFixture.NestedRequest>()", StringComparison.Ordinal),
                "141B-26b: generated DTO references are globally qualified");

            var diagnostics = RunGenerator(InvalidServiceFixtureSource()).Diagnostics;
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE001"),
                "141B-27: Roslyn generator reports invalid service names");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE002"),
                "141B-28: Roslyn generator reports unsupported service signatures");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE003"),
                "141B-29: Roslyn generator reports unsupported request DTOs");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE004"),
                "141B-30: Roslyn generator reports unsupported response DTOs");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE005"),
                "141B-31: Roslyn generator reports duplicate service names");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE006" && diagnostic.Severity == DiagnosticSeverity.Warning),
                "141B-32: Roslyn generator warns when schema metadata defaults are used");
        }

        private static void VerifyValidationWiring()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(project.Contains("FoxServiceDeclarativeRpcValidation.cs", StringComparison.Ordinal),
                "141B-33: runtime test project includes FoxService validation");
            Check(registry.Contains("--phase141b", StringComparison.Ordinal)
                  && registry.Contains("FoxServiceDeclarativeRpcValidation.Validate", StringComparison.Ordinal),
                "141B-34: validation registry wires --phase141b");
        }

        private static void VerifyPlayerFallbackGenerationPath()
        {
            var generator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var reconciler = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGeneratedSourceReconciler.cs");

            Check(generator.Contains("ScanFoxServiceMethods", StringComparison.Ordinal)
                  && generator.Contains("FoxServiceSourceEmitter.GeneratedSourceName", StringComparison.Ordinal),
                "141B-35: build-time generator scans [FoxService] methods for Player fallback source");
            Check(generator.Contains("EmitServiceSourceFile", StringComparison.Ordinal)
                  && generator.Contains("#if !UNITY_EDITOR", StringComparison.Ordinal),
                "141B-36: build-time service fallback is guarded for Player builds");
            Check(generator.Contains("FOXSERVICE005", StringComparison.Ordinal)
                  && generator.Contains("duplicate service name", StringComparison.Ordinal),
                "141B-37: build-time service fallback rejects duplicate service names");
            Check(generator.Contains("Replace('+', '.')", StringComparison.Ordinal),
                "141B-37a: build-time service fallback uses dot notation for nested DTO schema names");
            Check(generator.Contains("skipping duplicate generated service wrappers", StringComparison.Ordinal)
                  && generator.Contains("duplicateNames", StringComparison.Ordinal)
                  && !generator.Contains("ownersByServiceName", StringComparison.Ordinal),
                "141B-37b: build-time service fallback skips duplicate services without aborting all generation");
            Check(reconciler.Contains("GeneratedServiceSourcePattern", StringComparison.Ordinal)
                  && reconciler.Contains("*_FoxService.g.cs", StringComparison.Ordinal),
                "141B-38: generated source reconciliation removes stale FoxService fallback files");
        }

        private static void VerifyFullDemoUsesDeclarativeService()
        {
            var sampleDemo = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs");
            var sceneDemo = ReadRepoText("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var readme = ReadRepoText("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/README.md");

            Check(sampleDemo.Contains("public partial class FoxgloveDemoSetup", StringComparison.Ordinal)
                  && sampleDemo.Contains("[FoxService(", StringComparison.Ordinal),
                "141B-39: Full Demo exposes reset service with declarative FoxService");
            Check(sampleDemo.Contains("private ResetPoseResponse ResetPose(ResetPoseRequest request)", StringComparison.Ordinal)
                  && !sampleDemo.Contains("_resetSvcId", StringComparison.Ordinal)
                  && !sampleDemo.Contains("RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor", StringComparison.Ordinal),
                "141B-40: Full Demo avoids duplicate manual reset service registration");
            Check(sceneDemo.Contains("public partial class FoxgloveDemoSetup", StringComparison.Ordinal)
                  && sceneDemo.Contains("[FoxService(", StringComparison.Ordinal)
                  && sceneDemo.Contains("private ResetPoseResponse ResetPose(ResetPoseRequest request)", StringComparison.Ordinal)
                  && !sceneDemo.Contains("_resetSvcId", StringComparison.Ordinal)
                  && !sceneDemo.Contains("RegisterService(new Unity.FoxgloveSDK.Protocol.ServiceDescriptor", StringComparison.Ordinal),
                "141B-41: Unity project Full Demo scene script uses declarative FoxService");
            Check(readme.Contains("declarative `/cube/reset_pose`", StringComparison.Ordinal)
                  || readme.Contains("Declarative `/cube/reset_pose`", StringComparison.Ordinal),
                "141B-42: Full Demo README documents declarative reset service");
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141BFoxServiceFixture",
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            return driver.GetRunResult();
        }

        private static string GeneratedFoxServiceSource(GeneratorDriverRunResult result)
        {
            var generated = result.Results
                .SelectMany(item => item.GeneratedSources)
                .FirstOrDefault(item => item.HintName.EndsWith("_FoxService.g.cs", StringComparison.Ordinal));
            if (generated.HintName == null)
                throw new InvalidOperationException("Generated sources do not contain an expected *_FoxService.g.cs hint.");
            return generated.SourceText.ToString();
        }

        private static MetadataReference[] References()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141B Roslyn reference resolution.");

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(typeof(FoxServiceAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(JToken).Assembly.Location)
                })
                .ToArray();
        }

        private static string ServiceFixtureSource()
            => @"
using Unity.FoxgloveSDK.Components;

namespace Phase141B
{
    public sealed class Request
    {
        public int Value { get; set; }
    }

    public sealed class Response
    {
        public bool Ok { get; set; }
    }

    public partial class ServiceFixture
    {
        public sealed class NestedRequest
        {
            public int Id { get; set; }
        }

        public sealed class NestedResponse
        {
            public string Status { get; set; }
        }

        [FoxService(""/phase141b/reset"", Type = ""Phase141B.Reset"", RequestSchemaName = ""Phase141B.Request"", ResponseSchemaName = ""Phase141B.Response"")]
        private Response ResetPose(Request request)
        {
            return new Response { Ok = request != null };
        }

        [FoxService(""/phase141b/nested"")]
        private NestedResponse Nested(NestedRequest request)
        {
            return new NestedResponse { Status = request == null ? ""missing"" : ""ok"" };
        }
    }
}
";

        private static string InvalidServiceFixtureSource()
            => @"
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;

namespace Phase141B
{
    public sealed class Request { }
    public sealed class Response { }

    public partial class InvalidServices
    {
        [FoxService(""relative"")]
        private Response InvalidName(Request request) => new Response();

        [FoxService(""/phase141b/static"")]
        private static Response StaticService(Request request) => new Response();

        [FoxService(""/phase141b/bad_request"")]
        private Response BadRequest(Task request) => new Response();

        [FoxService(""/phase141b/bad_response"")]
        private Task BadResponse(Request request) => null;

        [FoxService(""/phase141b/duplicate"")]
        private Response DuplicateA(Request request) => new Response();

        [FoxService(""/phase141b/duplicate"")]
        private Response DuplicateB(Request request) => new Response();
    }
}
";

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
                throw new DirectoryNotFoundException("Could not find repository root for Phase141B validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class Phase141BServiceSource : IFoxgloveServiceSource
        {
            public Phase141BServiceSource(FoxgloveGeneratedServiceDescriptor descriptor)
            {
                FoxgloveServices = new[] { descriptor };
            }

            public IReadOnlyList<FoxgloveGeneratedServiceDescriptor> FoxgloveServices { get; }
        }
    }
}
