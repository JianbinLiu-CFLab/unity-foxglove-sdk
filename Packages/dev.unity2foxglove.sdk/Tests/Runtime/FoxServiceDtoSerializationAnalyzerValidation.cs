// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxService DTO serialization analyzer diagnostics.

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
    public static class FoxServiceDtoSerializationAnalyzerValidation
    {
        private static int _passCount;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141C Tests ---");
            _passCount = 0;

            VerifyValidDtoShapesStillGenerate();
            VerifyUnsupportedDtoDiagnostics();
            VerifyWarningDtoDiagnostics();
            VerifySharedDtoTypeRules();
            VerifyValidationWiringAndReleaseMetadata();

            Console.WriteLine("Phase 141C: " + _passCount + " checks passed.\n");
        }

        private static void VerifyValidDtoShapesStillGenerate()
        {
            var result = RunGenerator(ValidDtoFixtureSource());
            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();
            Check(diagnostics.Length == 1 && diagnostics[0].Id == "FOXSERVICE006",
                "141C-1: valid DTO fixture only reports default schema metadata warning");

            var generated = GeneratedFoxServiceSource(result);
            Check(generated.Contains("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor(\"/phase141c/valid\"", StringComparison.Ordinal),
                "141C-2: valid DTO service still emits a descriptor");
            Check(generated.Contains("requestToken.ToObject<global::Phase141C.ValidRequest>()", StringComparison.Ordinal),
                "141C-3: valid DTO request still emits direct JToken deserialization");
            Check(generated.Contains("var response = Valid(request);", StringComparison.Ordinal)
                  && !generated.Contains("MethodInfo.Invoke", StringComparison.Ordinal),
                "141C-4: valid DTO service still emits direct method invocation");
        }

        private static void VerifyUnsupportedDtoDiagnostics()
        {
            var diagnostics = RunGenerator(InvalidDtoFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.transform", "UnityEngine.Transform"),
                "141C-5: request DTO rejects nested Unity Transform members with path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE004", "Response.owner", "UnityEngine.MonoBehaviour"),
                "141C-6: response DTO rejects nested MonoBehaviour members with path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.callback", "System.Action"),
                "141C-7: request DTO rejects delegate members with path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.payload", "object"),
                "141C-8: request DTO rejects object members with path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.lookup", "Dictionary<int"),
                "141C-9: request DTO rejects dictionaries with non-string keys");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE008", "Request.next.parent", "RecursiveNode"),
                "141C-10: recursive DTO graphs report FOXSERVICE008 with nested path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.inheritedObject", "UnityEngine.GameObject"),
                "141C-10a: request DTO rejects inherited Unity object members with path");
            Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.delayed", "Task<int>"),
                "141C-10b: request DTO rejects task-like members with path");
        }

        private static void VerifyWarningDtoDiagnostics()
        {
            var diagnostics = RunGenerator(WarningDtoFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"
                                                && diagnostic.GetMessage().Contains("Request.readOnly", StringComparison.Ordinal)),
                "141C-11: get-only request properties produce FOXSERVICE007 warning");
            Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"
                                                && diagnostic.GetMessage().Contains("Request.ignored", StringComparison.Ordinal)),
                "141C-12: ignored DTO members produce FOXSERVICE007 warning");
            Check(diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
                "141C-13: warning-only DTO fixture still emits no service-blocking errors");
        }

        private static void VerifySharedDtoTypeRules()
        {
            Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task"),
                "141C-13a: shared DTO rules reject non-generic Task");
            Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task<System.Int32>"),
                "141C-13b: shared DTO rules reject Roslyn generic Task display names");
            Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task`1[[System.Int32, System.Private.CoreLib]]"),
                "141C-13c: shared DTO rules reject reflection generic Task display names");
        }

        private static void VerifyValidationWiringAndReleaseMetadata()
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var releases = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var generatorProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj");
            var playerGenerator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");

            Check(project.Contains("FoxServiceDtoSerializationAnalyzerValidation.cs", StringComparison.Ordinal),
                "141C-14: runtime test project includes FoxService DTO validation");
            Check(registry.Contains("--phase141c", StringComparison.Ordinal)
                  && registry.Contains("FoxServiceDtoSerializationAnalyzerValidation.Validate", StringComparison.Ordinal),
                "141C-15: validation registry wires --phase141c");
            Check(releases.Contains("FOXSERVICE007", StringComparison.Ordinal)
                  && releases.Contains("FOXSERVICE008", StringComparison.Ordinal),
                "141C-16: analyzer release metadata lists DTO warning and cycle diagnostics");
            Check(generatorProject.Contains("FoxServiceDtoValidation", StringComparison.Ordinal),
                "141C-17: source generator project includes shared DTO validation helpers");
            Check(playerGenerator.Contains("ValidateServiceDtoType", StringComparison.Ordinal)
                  && playerGenerator.Contains("FOXSERVICE008", StringComparison.Ordinal),
                "141C-18: Player fallback validates service DTOs before source emission");
            var generatorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(generatorSource.Contains("validatedTypes", StringComparison.Ordinal)
                  && playerGenerator.Contains("validatedTypes", StringComparison.Ordinal),
                "141C-19: DTO walkers memoize already validated type graphs");
        }

        private static bool HasDiagnostic(IEnumerable<Diagnostic> diagnostics, string id, string pathFragment, string typeFragment)
        {
            return diagnostics.Any(diagnostic =>
            {
                var message = diagnostic.GetMessage();
                return diagnostic.Id == id
                       && message.Contains(pathFragment, StringComparison.Ordinal)
                       && message.Contains(typeFragment, StringComparison.Ordinal);
            });
        }

        private static GeneratorDriverRunResult RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141CFoxServiceDtoFixture",
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
                throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES host data is required for Phase141C Roslyn reference resolution.");

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

        private static string ValidDtoFixtureSource()
            => @"
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141C
{
    public enum Mode { Idle, Run }

    public sealed class NestedPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double? Z { get; set; }
    }

    public sealed class ValidRequest
    {
        public string Name { get; set; }
        public Mode Mode;
        public DateTime Timestamp { get; set; }
        public Guid CorrelationId { get; set; }
        public TimeSpan Duration { get; set; }
        public NestedPose Pose { get; set; }
        public List<NestedPose> History { get; set; }
        public IReadOnlyList<int> Samples { get; set; }
        public Dictionary<string, NestedPose> NamedPoses { get; set; }
        public int[] Counts { get; set; }
    }

    public sealed class ValidResponse
    {
        public bool Ok { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
    }

    public partial class ValidServices
    {
        [FoxService(""/phase141c/valid"")]
        private ValidResponse Valid(ValidRequest request)
        {
            return new ValidResponse { Ok = request != null };
        }
    }
}
";

        private static string InvalidDtoFixtureSource()
            => @"
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141C
{
    public sealed class BadUnityRequest
    {
        public UnityEngine.Transform transform { get; set; }
    }

    public sealed class BadUnityResponse
    {
        public UnityEngine.MonoBehaviour owner { get; set; }
    }

    public sealed class DelegateRequest
    {
        public Action callback { get; set; }
    }

    public sealed class ObjectRequest
    {
        public object payload { get; set; }
    }

    public sealed class BadDictionaryRequest
    {
        public Dictionary<int, string> lookup { get; set; }
    }

    public class BadInheritedRequestBase
    {
        public UnityEngine.GameObject inheritedObject { get; set; }
    }

    public sealed class BadInheritedRequest : BadInheritedRequestBase
    {
        public string name { get; set; }
    }

    public sealed class BadTaskMemberRequest
    {
        public System.Threading.Tasks.Task<int> delayed { get; set; }
    }

    public sealed class RecursiveNode
    {
        public RecursiveChild next { get; set; }
    }

    public sealed class RecursiveChild
    {
        public RecursiveNode parent { get; set; }
    }

    public sealed class OkResponse
    {
        public bool ok { get; set; }
    }

    public partial class InvalidServices
    {
        [FoxService(""/phase141c/bad_unity_request"")]
        private OkResponse BadUnityRequest(BadUnityRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_unity_response"")]
        private BadUnityResponse BadUnityResponse(OkResponse request) => new BadUnityResponse();

        [FoxService(""/phase141c/bad_delegate"")]
        private OkResponse BadDelegate(DelegateRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_object"")]
        private OkResponse BadObject(ObjectRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_dictionary"")]
        private OkResponse BadDictionary(BadDictionaryRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_inherited"")]
        private OkResponse BadInherited(BadInheritedRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_task_member"")]
        private OkResponse BadTaskMember(BadTaskMemberRequest request) => new OkResponse();

        [FoxService(""/phase141c/recursive"")]
        private OkResponse Recursive(RecursiveNode request) => new OkResponse();
    }
}
";

        private static string WarningDtoFixtureSource()
            => @"
using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141C
{
    public sealed class WarningRequest
    {
        public string readOnly { get { return ""value""; } }

        [JsonIgnore]
        public string ignored { get; set; }
    }

    public sealed class WarningResponse
    {
        public bool ok { get; set; }
    }

    public partial class WarningServices
    {
        [FoxService(""/phase141c/warnings"", Type = ""Phase141C.Warning"", RequestSchemaName = ""Phase141C.WarningRequest"", ResponseSchemaName = ""Phase141C.WarningResponse"")]
        private WarningResponse Warnings(WarningRequest request) => new WarningResponse();
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
                throw new DirectoryNotFoundException("Could not find repository root for Phase141C validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
