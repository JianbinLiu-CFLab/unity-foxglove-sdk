using System;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_22Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-22 Tests ---");
            _passed = 0;

            VerifyTopicMetadataHasherIsReused();
            VerifyGenerationModelOrderingContract();
            VerifyCanonicalTypesFlowToEmitter();
            VerifySourceFileWriteSkipsLengthMismatches();
            VerifyRegistry();

            Console.WriteLine("Phase 164-22: " + _passed + " checks passed.\n");
        }

        private static void VerifyTopicMetadataHasherIsReused()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var hash = PhaseValidationSourceHelpers.SourceMethod(source, "public static string Sha256Hex(string value)");

            Check(source.Contains("private static readonly object Sha256Gate = new();", StringComparison.Ordinal)
                  && source.Contains("private static readonly SHA256 SharedSha256 = SHA256.Create();", StringComparison.Ordinal),
                "164-22A-1: topic metadata emitter keeps one reusable SHA256 instance");
            Check(hash.Contains("lock (Sha256Gate)", StringComparison.Ordinal)
                  && hash.Contains("SharedSha256.ComputeHash(bytes)", StringComparison.Ordinal)
                  && !hash.Contains("using var sha = SHA256.Create();", StringComparison.Ordinal),
                "164-22A-2: topic fingerprinting avoids per-topic SHA256 allocation");
        }

        private static void VerifyGenerationModelOrderingContract()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var fromMembers = PhaseValidationSourceHelpers.SourceMethod(source, "public static FoxRunGenerationModel FromMembers");
            var copyTypes = PhaseValidationSourceHelpers.SourceMethod(source, "private static IReadOnlyList<FoxRunGenerationType> CopyTypes");
            var type = PhaseValidationSourceHelpers.SourceType(source, "FoxRunGenerationType");

            // The old 164-22 fast-path assertion described an optimization
            // that was reverted before this validation was promoted. Keep the
            // current defensive-ordering contract explicit instead of hiding
            // the inversion behind the original check identifiers.
            Check(fromMembers.Contains(".GroupBy(", StringComparison.Ordinal)
                  && fromMembers.Contains(".OrderBy(", StringComparison.Ordinal)
                  && fromMembers.Contains("new FoxRunGenerationType(", StringComparison.Ordinal)
                  && fromMembers.Contains("return new FoxRunGenerationModel(types)", StringComparison.Ordinal),
                "164-22B-1 (current defensive ordering): FromMembers groups and orders members before model construction");
            Check(copyTypes.Contains(".OrderBy(type => type.DeclaringType", StringComparison.Ordinal)
                  && copyTypes.Contains("new FoxRunGenerationType(", StringComparison.Ordinal)
                  && copyTypes.Contains(".AsReadOnly()", StringComparison.Ordinal)
                  && source.Contains("Types = CopyTypes(types)", StringComparison.Ordinal),
                "164-22B-2 (current defensive ordering): model copy path preserves deterministic type order and read-only ownership");
            Check(type.Contains("public FoxRunGenerationType(", StringComparison.Ordinal)
                  && type.Contains(".OrderBy(member => member.Topic", StringComparison.Ordinal)
                  && type.Contains(".ThenBy(member => member.MemberName", StringComparison.Ordinal)
                  && type.Contains(".ThenBy(member => member.SchemaName", StringComparison.Ordinal)
                  && type.Contains(".ThenBy(member => member.CanonicalType", StringComparison.Ordinal)
                  && type.Contains(".AsReadOnly()", StringComparison.Ordinal),
                "164-22B-3 (current defensive ordering): public generation type constructor sorts defensively");
        }

        private static void VerifyCanonicalTypesFlowToEmitter()
        {
            var model = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var emitter = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
            var metadata = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var toTopicMember = PhaseValidationSourceHelpers.SourceMethod(model, "public FoxgloveSourceEmitter.TopicMember ToTopicMember");
            var canonicalShape = PhaseValidationSourceHelpers.SourceMethod(metadata, "internal static string CanonicalTopicShape");

            Check(emitter.Contains("public readonly string CanonicalType;", StringComparison.Ordinal)
                  && toTopicMember.Contains("CanonicalType,", StringComparison.Ordinal),
                "164-22C-1: TopicMember carries canonical type from generation model");
            Check(canonicalShape.Contains("field.CanonicalType", StringComparison.Ordinal)
                  && !canonicalShape.Contains("NormalizeTypeName(field.TypeName)", StringComparison.Ordinal),
                "164-22C-2: topic shape fingerprinting uses cached canonical type");
        }

        private static void VerifySourceFileWriteSkipsLengthMismatches()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            var write = PhaseValidationSourceHelpers.SourceMethod(source, "private static bool WriteSourceFileIfChanged");

            Check(write.Contains("var existing = new FileInfo(path);", StringComparison.Ordinal)
                  && write.Contains("existing.Length == bytes.Length", StringComparison.Ordinal)
                  && write.Contains("FileContentEquals(path, bytes)", StringComparison.Ordinal)
                  && !write.Contains("File.ReadAllBytes(path).SequenceEqual(bytes)", StringComparison.Ordinal),
                "164-22D-1: generated file equality check streams existing bytes only after a length match");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-22\"", StringComparison.Ordinal)
                  && PhaseValidationRegistry.DefaultValidations(false)
                      .Any(item => item.Flag == "--phase164-22"),
                "164-22E-1: validation registry executes Phase164-22 in the default lane");
            Check(project.Contains("Phase164_22Validation.cs", StringComparison.Ordinal), "164-22E-2: runtime validation project compiles Phase164-22");
        }

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
