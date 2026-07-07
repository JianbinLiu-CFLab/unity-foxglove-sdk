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
        private static readonly Lazy<MetadataReference[]> CachedReferences = new Lazy<MetadataReference[]>(CreateReferences);

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 141C Tests ---");
            var run = new ValidationRun();

            VerifyValidDtoShapesStillGenerate(run);
            VerifyUnsupportedDtoDiagnostics(run);
            VerifyWarningDtoDiagnostics(run);
            VerifySharedDtoTypeRules(run);
            VerifyPhase141DPolicyMatrix(run);
            VerifyValidationWiringAndReleaseMetadata(run);

            Console.WriteLine("Phase 141C: " + run.PassCount + " checks passed.\n");
        }

        private static void VerifyValidDtoShapesStillGenerate(ValidationRun run)
        {
            var result = RunGenerator(ValidDtoFixtureSource());
            var diagnostics = result.Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();
            run.Check(diagnostics.Length == 1 && diagnostics[0].Id == "FOXSERVICE006",
                "141C-1: valid DTO fixture only reports default schema metadata warning");

            var generated = GeneratedFoxServiceSource(result);
            run.Check(generated.Contains("new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor(\"/phase141c/valid\"", StringComparison.Ordinal),
                "141C-2: valid DTO service still emits a descriptor");
            run.Check(generated.Contains("requestToken.ToObject<global::Phase141C.ValidRequest>()", StringComparison.Ordinal),
                "141C-3: valid DTO request still emits direct JToken deserialization");
            run.Check(generated.Contains("var response = Valid(request);", StringComparison.Ordinal)
                  && !generated.Contains("MethodInfo.Invoke", StringComparison.Ordinal),
                "141C-4: valid DTO service still emits direct method invocation");
        }

        private static void VerifyUnsupportedDtoDiagnostics(ValidationRun run)
        {
            var diagnostics = RunGenerator(InvalidDtoFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.transform", "UnityEngine.Transform"),
                "141C-5: request DTO rejects nested Unity Transform members with path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE004", "Response.owner", "UnityEngine.MonoBehaviour"),
                "141C-6: response DTO rejects nested MonoBehaviour members with path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.callback", "System.Action"),
                "141C-7: request DTO rejects delegate members with path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.payload", "object"),
                "141C-8: request DTO rejects object members with path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.lookup", "Dictionary<int"),
                "141C-9: request DTO rejects dictionaries with non-string keys");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE008", "Request.next.parent", "RecursiveNode"),
                "141C-10: recursive DTO graphs report FOXSERVICE008 with nested path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.inheritedObject", "UnityEngine.GameObject"),
                "141C-10a: request DTO rejects inherited Unity object members with path");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.delayed", "Task<int>"),
                "141C-10b: request DTO rejects task-like members with path");
        }

        private static void VerifyWarningDtoDiagnostics(ValidationRun run)
        {
            var diagnostics = RunGenerator(WarningDtoFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            run.Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"
                                                && diagnostic.GetMessage().Contains("Request.readOnly", StringComparison.Ordinal)),
                "141C-11: get-only request properties produce FOXSERVICE007 warning");
            run.Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"
                                                && diagnostic.GetMessage().Contains("Request.ignored", StringComparison.Ordinal)),
                "141C-12: ignored DTO members produce FOXSERVICE007 warning");
            run.Check(diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
                "141C-13: warning-only DTO fixture still emits no service-blocking errors");
        }

        private static void VerifySharedDtoTypeRules(ValidationRun run)
        {
            run.Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task"),
                "141C-13a: shared DTO rules reject non-generic Task");
            run.Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task<System.Int32>"),
                "141C-13b: shared DTO rules reject Roslyn generic Task display names");
            run.Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsTaskLike("System.Threading.Tasks.Task`1[[System.Int32, System.Private.CoreLib]]"),
                "141C-13c: shared DTO rules reject reflection generic Task display names");
        }

        private static void VerifyPhase141DPolicyMatrix(ValidationRun run)
        {
            var diagnostics = RunGenerator(Phase141DPolicyMatrixFixtureSource()).Diagnostics
                .Where(diagnostic => diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();

            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE003", "hashSet", "HashSet"),
                "141D-1: Roslyn accepts HashSet<T> request DTO members");
            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE003", "collection", "ICollection"),
                "141D-2: Roslyn accepts ICollection<T> request DTO members");
            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE004", "readOnlyNumbers", "IReadOnlyCollection"),
                "141D-3: Roslyn accepts IReadOnlyCollection<T> response DTO members");
            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE003", "queue", "Queue"),
                "141D-3a: Roslyn accepts Queue<T> request DTO members");
            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE003", "stack", "Stack"),
                "141D-3b: Roslyn accepts Stack<T> request DTO members");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE003", "Request.sequence", "IEnumerable"),
                "141D-4: Roslyn rejects interface-only IEnumerable<T> request DTO members");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE007", "Request.readOnlyScalar", "string"),
                "141D-5: Roslyn warns for get-only scalar DTO properties");
            run.Check(!HasDiagnostic(diagnostics, "FOXSERVICE007", "Request.tags", "List"),
                "141D-6: Roslyn does not warn for get-only mutable collection DTO properties");
            run.Check(HasDiagnostic(diagnostics, "FOXSERVICE007", "Request.readonlyValue", "int"),
                "141D-7: Roslyn warns for public readonly DTO fields");
            run.Check(diagnostics.Any(diagnostic => diagnostic.Id == "FOXSERVICE009"
                                                && diagnostic.GetMessage().Contains("Request.next", StringComparison.Ordinal)),
                "141D-8: Roslyn reports deep non-recursive DTO graphs as FOXSERVICE009 warning");

            var reflectionOk = Unity.FoxgloveSDK.Editor.FoxServiceDtoReflectionValidator.Validate(
                typeof(Phase141DReflectionAcceptedRequest),
                Unity.FoxgloveSDK.Editor.FoxServiceDtoSide.Request,
                "/phase141d/reflection-ok");
            run.Check(reflectionOk.Count == 0,
                "141D-9: reflection validator accepts collection DTO matrix shapes");

            var reflectionBad = Unity.FoxgloveSDK.Editor.FoxServiceDtoReflectionValidator.Validate(
                typeof(Phase141DReflectionRejectedRequest),
                Unity.FoxgloveSDK.Editor.FoxServiceDtoSide.Request,
                "/phase141d/reflection-bad");
            run.Check(reflectionBad.Any(diagnostic => diagnostic.Id == "FOXSERVICE003"
                                                  && diagnostic.Path.Contains("sequence", StringComparison.Ordinal)),
                "141D-10: reflection validator rejects interface-only IEnumerable<T> request members");
            run.Check(reflectionBad.Any(diagnostic => diagnostic.Id == "FOXSERVICE007"
                                                  && diagnostic.Path.Contains("readOnlyScalar", StringComparison.Ordinal)),
                "141D-11: reflection validator reports structured warning diagnostics");
            run.Check(reflectionBad.All(diagnostic => diagnostic.ServiceName == "/phase141d/reflection-bad"
                                                   && diagnostic.Target.Contains("/phase141d/reflection-bad", StringComparison.Ordinal)),
                "141D-11a: reflection validator preserves service name on returned diagnostics");
            run.Check(Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsListContract("System.Collections.Generic.List<T>")
                  && Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsListContract(typeof(List<>).FullName)
                  && Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsDictionaryContract("System.Collections.Generic.Dictionary<TKey, TValue>")
                  && Unity.FoxgloveSDK.Editor.FoxServiceDtoTypeNames.IsDictionaryContract(typeof(Dictionary<,>).FullName),
                "141D-11b: DTO type-name helpers accept Roslyn and reflection generic contract names");

            var reflectionWarningMemo = Unity.FoxgloveSDK.Editor.FoxServiceDtoReflectionValidator.Validate(
                typeof(Phase141DSharedWarningReferences),
                Unity.FoxgloveSDK.Editor.FoxServiceDtoSide.Request,
                "/phase141d/reflection-warning-memo");
            run.Check(reflectionWarningMemo.Count(diagnostic => diagnostic.Id == "FOXSERVICE007") == 1,
                "141D-11c: reflection validator memoizes warning-only shared DTO types");
        }

        private static void VerifyValidationWiringAndReleaseMetadata(ValidationRun run)
        {
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var releases = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var generatorProject = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj");
            var playerValidator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunServiceValidator.cs");

            run.Check(project.Contains("FoxServiceDtoSerializationAnalyzerValidation.cs", StringComparison.Ordinal),
                "141C-14: runtime test project includes FoxService DTO validation");
            run.Check(registry.Contains("--phase141c", StringComparison.Ordinal)
                  && registry.Contains("FoxServiceDtoSerializationAnalyzerValidation.Validate", StringComparison.Ordinal),
                "141C-15: validation registry wires --phase141c");
            run.Check(releases.Contains("FOXSERVICE007", StringComparison.Ordinal)
                  && releases.Contains("FOXSERVICE008", StringComparison.Ordinal),
                "141C-16: analyzer release metadata lists DTO warning and cycle diagnostics");
            run.Check(releases.Contains("FOXSERVICE009", StringComparison.Ordinal),
                "141D-12: analyzer release metadata lists DTO depth-limit diagnostic");
            run.Check(generatorProject.Contains("FoxServiceDtoValidation", StringComparison.Ordinal),
                "141C-17: source generator project includes shared DTO validation helpers");
            run.Check(playerValidator.Contains("ValidateServiceDtoType", StringComparison.Ordinal)
                  && playerValidator.Contains("FoxServiceDtoReflectionValidator.Validate", StringComparison.Ordinal),
                "141C-18: Player fallback validates service DTOs before source emission");
            run.Check(playerValidator.Contains("FoxServiceDtoReflectionValidator", StringComparison.Ordinal),
                "141D-13: Player fallback uses structured reflection DTO validator");
            var generatorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            var reflectionValidator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxServiceDtoReflectionValidator.cs");
            run.Check(generatorSource.Contains("validatedTypes", StringComparison.Ordinal)
                  && reflectionValidator.Contains("validatedTypes", StringComparison.Ordinal),
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
                source + UnityScriptingStubSource(),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            var compilation = CSharpCompilation.Create(
                "Phase141CFoxServiceDtoFixture",
                new[] { syntaxTree },
                References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new FoxgloveLogSourceGenerator().AsSourceGenerator() },
                parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp9));
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generatorDiagnostics);
            var errors = outputCompilation.GetDiagnostics()
                .Concat(generatorDiagnostics)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                     && !diagnostic.Id.StartsWith("FOXSERVICE", StringComparison.Ordinal))
                .ToArray();
            if (errors.Length != 0)
            {
                var formatted = string.Join(Environment.NewLine, errors.Select(diagnostic => diagnostic.ToString()));
                throw new InvalidOperationException("Phase141C fixture compilation or generator errors:" + Environment.NewLine + formatted);
            }

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
            => CachedReferences.Value;

        private static MetadataReference[] CreateReferences()
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

        private static string UnityScriptingStubSource()
            => @"
namespace UnityEngine.Scripting
{
    public sealed class PreserveAttribute : System.Attribute {}
}
";

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

        private static string Phase141DPolicyMatrixFixtureSource()
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

namespace Phase141D
{
    public sealed class CollectionRequest
    {
        public HashSet<string> hashSet { get; set; }
        public ICollection<float> collection { get; set; }
        public Queue<string> queue { get; set; }
        public Stack<string> stack { get; set; }
        public List<string> tags { get; } = new List<string>();
        public string readOnlyScalar { get { return ""value""; } }
        public readonly int readonlyValue;
    }

    public sealed class InterfaceOnlyRequest
    {
        public IEnumerable<int> sequence { get; set; }
    }

    public sealed class CollectionResponse
    {
        public IReadOnlyCollection<double> readOnlyNumbers { get; set; }
        public SortedDictionary<string, int> sorted { get; set; }
    }

    public sealed class Deep00 { public Deep01 next { get; set; } }
    public sealed class Deep01 { public Deep02 next { get; set; } }
    public sealed class Deep02 { public Deep03 next { get; set; } }
    public sealed class Deep03 { public Deep04 next { get; set; } }
    public sealed class Deep04 { public Deep05 next { get; set; } }
    public sealed class Deep05 { public Deep06 next { get; set; } }
    public sealed class Deep06 { public Deep07 next { get; set; } }
    public sealed class Deep07 { public Deep08 next { get; set; } }
    public sealed class Deep08 { public Deep09 next { get; set; } }
    public sealed class Deep09 { public Deep10 next { get; set; } }
    public sealed class Deep10 { public Deep11 next { get; set; } }
    public sealed class Deep11 { public Deep12 next { get; set; } }
    public sealed class Deep12 { public Deep13 next { get; set; } }
    public sealed class Deep13 { public Deep14 next { get; set; } }
    public sealed class Deep14 { public Deep15 next { get; set; } }
    public sealed class Deep15 { public Deep16 next { get; set; } }
    public sealed class Deep16 { public Deep17 next { get; set; } }
    public sealed class Deep17 { public Deep18 next { get; set; } }
    public sealed class Deep18 { public Deep19 next { get; set; } }
    public sealed class Deep19 { public Deep20 next { get; set; } }
    public sealed class Deep20 { public Deep21 next { get; set; } }
    public sealed class Deep21 { public Deep22 next { get; set; } }
    public sealed class Deep22 { public Deep23 next { get; set; } }
    public sealed class Deep23 { public Deep24 next { get; set; } }
    public sealed class Deep24 { public Deep25 next { get; set; } }
    public sealed class Deep25 { public Deep26 next { get; set; } }
    public sealed class Deep26 { public Deep27 next { get; set; } }
    public sealed class Deep27 { public Deep28 next { get; set; } }
    public sealed class Deep28 { public Deep29 next { get; set; } }
    public sealed class Deep29 { public Deep30 next { get; set; } }
    public sealed class Deep30 { public Deep31 next { get; set; } }
    public sealed class Deep31 { public Deep32 next { get; set; } }
    public sealed class Deep32 { public Deep33 next { get; set; } }
    public sealed class Deep33 { public string value { get; set; } }

    public sealed class OkResponse
    {
        public bool ok { get; set; }
    }

    public partial class PolicyMatrixServices
    {
        [FoxService(""/phase141d/collections"")]
        private CollectionResponse Collections(CollectionRequest request) => new CollectionResponse();

        [FoxService(""/phase141d/interface_only"")]
        private OkResponse InterfaceOnly(InterfaceOnlyRequest request) => new OkResponse();

        [FoxService(""/phase141d/deep"")]
        private OkResponse Deep(Deep00 request) => new OkResponse();
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

        private sealed class ValidationRun
        {
            public int PassCount { get; private set; }

            public void Check(bool condition, string label)
            {
                if (!condition)
                    throw new InvalidOperationException("[FAIL] " + label);

                Console.WriteLine("[PASS] " + label);
                PassCount++;
            }
        }

        private sealed class Phase141DReflectionAcceptedRequest
        {
            public HashSet<string> hashSet { get; set; }
            public ICollection<float> collection { get; set; }
            public Queue<string> queue { get; set; }
            public Stack<string> stack { get; set; }
            public List<string> tags { get; } = new List<string>();
        }

        private sealed class Phase141DReflectionRejectedRequest
        {
            public IEnumerable<int> sequence { get; set; }
            public string readOnlyScalar { get { return "value"; } }
        }

        private sealed class Phase141DSharedWarningReferences
        {
            public Phase141DSharedWarningType first { get; set; }
            public Phase141DSharedWarningType second { get; set; }
        }

        private sealed class Phase141DSharedWarningType
        {
            public string value { get { return "value"; } }
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => PhaseValidationSourceHelpers.RepoPath(relativePath);
    }
}
