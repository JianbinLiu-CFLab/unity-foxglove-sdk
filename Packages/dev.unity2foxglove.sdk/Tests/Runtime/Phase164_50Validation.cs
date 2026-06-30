using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_50Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-50 Tests ---");
            _passed = 0;

            VerifyFoxServiceValidationReferenceCaches();
            VerifyBuildFallbackReflectionCaches();
            VerifyReflectionMemberScanAvoidsLinqAllocations();
            VerifySchemaBuilderUsesCompactMemoKeys();
            VerifyRegistry();

            Console.WriteLine("Phase 164-50: " + _passed + " checks passed.\n");
        }

        private static void VerifyFoxServiceValidationReferenceCaches()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceDeclarativeRpcValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceDtoSerializationAnalyzerValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceEditorSchemaPolishValidation.cs",
                "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceDtoGraphWalkerConvergenceValidation.cs"
            })
            {
                var source = Read(path);
                Check(source.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", StringComparison.Ordinal)
                      && source.Contains("private static MetadataReference[] References()", StringComparison.Ordinal)
                      && source.Contains("=> CachedReferences.Value", StringComparison.Ordinal)
                      && source.Contains("private static MetadataReference[] CreateReferences()", StringComparison.Ordinal),
                    "164-50A-1: " + path + " caches Roslyn metadata references");
            }
        }

        private static void VerifyBuildFallbackReflectionCaches()
        {
            var generator = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var byRefLike = SourceMethod(generator, "private static bool IsByRefLike(Type type)");

            Check(generator.Contains("private static readonly PropertyInfo IsByRefLikeProperty", StringComparison.Ordinal)
                  && byRefLike.Contains("IsByRefLikeProperty != null", StringComparison.Ordinal)
                  && byRefLike.Contains("(bool)IsByRefLikeProperty.GetValue(type)", StringComparison.Ordinal)
                  && !byRefLike.Contains("typeof(Type).GetProperty", StringComparison.Ordinal),
                "164-50B-1: build fallback caches Type.IsByRefLike reflection lookup");
        }

        private static void VerifyReflectionMemberScanAvoidsLinqAllocations()
        {
            var members = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceDtoValidation/FoxServiceDtoReflectionMembers.cs");
            var serializable = SourceMethod(members, "public static IEnumerable<MemberInfo> SerializableMembers(Type type)");
            var ignored = SourceMethod(members, "public static bool IsIgnored(MemberInfo member)");

            Check(!members.Contains("using System.Linq;", StringComparison.Ordinal)
                  && serializable.Contains("var members = current.GetMembers(flags);", StringComparison.Ordinal)
                  && serializable.Contains("Array.Sort(members, CompareMemberOrder);", StringComparison.Ordinal)
                  && !serializable.Contains(".OrderBy(", StringComparison.Ordinal),
                "164-50C-1: reflection DTO member scan sorts GetMembers array in place");
            Check(ignored.Contains("foreach (var attribute in member.GetCustomAttributes(false))", StringComparison.Ordinal)
                  && !ignored.Contains(".Any(", StringComparison.Ordinal),
                "164-50C-2: ignored-member detection scans attributes without LINQ closures");
            Check(members.Contains("foreach (var attribute in member.GetCustomAttributes(false))", StringComparison.Ordinal),
                "164-50C-3: DTO member attribute reads use non-inherited custom attributes");
        }

        private static void VerifySchemaBuilderUsesCompactMemoKeys()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceSchema/FoxServiceSchemaReflectionBuilder.cs");
            var build = SourceMethod(source, "private static FoxServiceSchemaModel Build(");
            var fullTypeName = SourceMethod(source, "private static string FullTypeName(Type type)");

            Check(build.Contains("var typeKey = FullTypeName(type);", StringComparison.Ordinal)
                  && !build.Contains("AssemblyQualifiedName", StringComparison.Ordinal),
                "164-50D-1: reflection schema builder uses compact full-name memo keys");
            Check(fullTypeName.Contains("(type.FullName ?? type.Name).Replace('+', '.')", StringComparison.Ordinal),
                "164-50D-2: reflection schema memo key matches dot-notation DTO naming");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-50\"", StringComparison.Ordinal), "164-50E-1: validation registry exposes Phase164-50");
            Check(project.Contains("Phase164_50Validation.cs", StringComparison.Ordinal), "164-50E-2: runtime validation project compiles Phase164-50");
        }

        private static string SourceMethod(string source, string signature)
            => PhaseValidationSourceHelpers.SourceMethod(source, signature);

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
