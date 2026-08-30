using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_49Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-49 Tests ---");
            _passed = 0;

            VerifySchemaRegistryCachesGeneratedSchemas();
            VerifySchemaInfoWriterCountsInSinglePass();
            VerifyGenerationValidatorMaterializesTopicGroups();
            VerifyRoslynReferenceCacheAndHasherOptimizations();
            VerifyRegistry();

            Console.WriteLine("Phase 164-49: " + _passed + " checks passed.\n");
        }

        private static void VerifySchemaRegistryCachesGeneratedSchemas()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs");
            var register = SourceMethod(source, "public static void RegisterGeneratedSchemas(ISchemaRegistry registry)");
            var helper = SourceMethod(source, "private static string GetOrBuildGeneratedSchema(FoxRunSchemaContractInfo contract)");
            var reset = SourceMethod(source, "private static void ResetState()");

            Check(source.Contains("GeneratedSchemaCache", StringComparison.Ordinal)
                  && register.Contains("Content = GetOrBuildGeneratedSchema(contract)", StringComparison.Ordinal)
                  && helper.Contains("GeneratedSchemaCache.TryGetValue", StringComparison.Ordinal)
                  && helper.Contains("FoxRunJsonSchemaBuilder.Build(contract)", StringComparison.Ordinal),
                "164-49A-1: generated FoxRun JSON schemas are cached by contract identity");
            Check(reset.Contains("GeneratedSchemaCache.Clear()", StringComparison.Ordinal),
                "164-49A-2: generated schema cache clears with runtime/test registry state");
        }

        private static void VerifySchemaInfoWriterCountsInSinglePass()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            var generate = SourceMethod(source, "public static string GenerateSource(FoxRunCanonicalManifest manifest)");
            var verify = SourceMethod(source, "public static FoxRunSchemaInfoVerification VerifyGeneratedInfo(");
            var count = SourceMethod(source, "private static ManifestCounts CountManifest(FoxRunCanonicalManifest manifest)");

            Check(!source.Contains("using System.Linq;", StringComparison.Ordinal)
                  && generate.Contains("var counts = CountManifest(manifest);", StringComparison.Ordinal)
                  && verify.Contains("var counts = CountManifest(manifest);", StringComparison.Ordinal)
                  && !generate.Contains(".Sum(", StringComparison.Ordinal)
                  && !verify.Contains(".Sum(", StringComparison.Ordinal),
                "164-49B-1: schema-info source generation reuses one manifest count pass");
            Check(count.Contains("foreach (var type in types)", StringComparison.Ordinal)
                  && count.Contains("foreach (var contract in type.Contracts)", StringComparison.Ordinal),
                "164-49B-2: manifest count helper walks contracts and fields without nested LINQ");
            Check(!source.Contains("private static string StringLiteral(", StringComparison.Ordinal)
                  && source.Contains("AppendStringLiteral(sb, value)", StringComparison.Ordinal),
                "164-49B-3: string literal emission appends directly to the caller builder");
        }

        private static void VerifyGenerationValidatorMaterializesTopicGroups()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            // Keep the unique validator source as the semantic anchor. Its
            // parameter list is intentionally allowed to evolve from List to
            // ICollection while the materialization invariant remains fixed.
            var validate = source;

            Check(validate.Contains("var members = group.ToList();", StringComparison.Ordinal)
                  && validate.Contains("members.Select(member => member.SchemaName)", StringComparison.Ordinal)
                  && validate.Contains("members.Any(", StringComparison.Ordinal)
                  && validate.Contains("member.IsAggregateMember", StringComparison.Ordinal)
                  && validate.Contains("var first = members[0];", StringComparison.Ordinal),
                "164-49C-1: topic-group validation materializes each group once before repeated checks");
        }

        private static void VerifyRoslynReferenceCacheAndHasherOptimizations()
        {
            var polish = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceEditorSchemaPolishValidation.cs");
            var hasher = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunManifest/FoxRunManifestHasher.cs");
            var sha = SourceMethod(hasher, "public static string Sha256Hex(string canonicalJson)");

            Check(polish.Contains("Lazy<MetadataReference[]> CachedReferences", StringComparison.Ordinal)
                  && polish.Contains("private static MetadataReference[] References()")
                  && polish.Contains("=> CachedReferences.Value", StringComparison.Ordinal)
                  && polish.Contains("private static MetadataReference[] CreateReferences()", StringComparison.Ordinal),
                "164-49D-1: FoxService schema polish validation caches Roslyn references");
            Check(sha.Contains("var chars = new char[hash.Length * 2];", StringComparison.Ordinal)
                  && sha.Contains("LowerHex[value >> 4]", StringComparison.Ordinal)
                  && !sha.Contains("b.ToString(\"x2\")", StringComparison.Ordinal)
                  && !sha.Contains("new StringBuilder(hash.Length * 2)", StringComparison.Ordinal),
                "164-49D-2: FoxRun manifest hashing avoids per-byte string formatting");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-49\"", StringComparison.Ordinal), "164-49E-1: validation registry exposes Phase164-49");
            Check(project.Contains("Phase164_49Validation.cs", StringComparison.Ordinal), "164-49E-2: runtime validation project compiles Phase164-49");
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
