using System;

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
            VerifyGenerationModelAvoidsRedundantSorts();
            VerifyCanonicalTypesFlowToEmitter();
            VerifySourceFileWriteSkipsLengthMismatches();
            VerifyRegistry();

            Console.WriteLine("Phase 164-22: " + _passed + " checks passed.\n");
        }

        private static void VerifyTopicMetadataHasherIsReused()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var hash = PhaseValidationSourceHelpers.SourceMethod(source, "private static string Sha256Hex");

            Check(source.Contains("private static readonly object Sha256Gate = new();", StringComparison.Ordinal)
                  && source.Contains("private static readonly SHA256 SharedSha256 = SHA256.Create();", StringComparison.Ordinal),
                "164-22A-1: topic metadata emitter keeps one reusable SHA256 instance");
            Check(hash.Contains("lock (Sha256Gate)", StringComparison.Ordinal)
                  && hash.Contains("SharedSha256.ComputeHash(bytes)", StringComparison.Ordinal)
                  && !hash.Contains("using var sha = SHA256.Create();", StringComparison.Ordinal),
                "164-22A-2: topic fingerprinting avoids per-topic SHA256 allocation");
        }

        private static void VerifyGenerationModelAvoidsRedundantSorts()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var fromMembers = PhaseValidationSourceHelpers.SourceMethod(source, "public static FoxRunGenerationModel FromMembers");
            var copyTypes = PhaseValidationSourceHelpers.SourceMethod(source, "private static IReadOnlyList<FoxRunGenerationType> CopyTypes");
            var publicTypeCtor = PhaseValidationSourceHelpers.SourceMethod(source, "public FoxRunGenerationType(string ns");
            var internalTypeCtor = PhaseValidationSourceHelpers.SourceMethod(source, "internal FoxRunGenerationType(string ns");

            Check(fromMembers.Contains("typesAlreadySortedAndCopied: true", StringComparison.Ordinal)
                  && source.Contains("private FoxRunGenerationModel(", StringComparison.Ordinal)
                  && source.Contains("typesAlreadySortedAndCopied", StringComparison.Ordinal),
                "164-22B-1: FromMembers uses an internal already-sorted model path");
            Check(copyTypes.Contains("membersAlreadySorted: true", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<FoxRunGenerationType> ToReadOnlyTypes", StringComparison.Ordinal),
                "164-22B-2: model copy path preserves sorted type/member order without re-sorting");
            Check(publicTypeCtor.Contains("membersAlreadySorted: false", StringComparison.Ordinal)
                  && internalTypeCtor.Contains("membersAlreadySorted", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<FoxRunGenerationMember> SortMembers", StringComparison.Ordinal)
                  && source.Contains("private static IReadOnlyList<FoxRunGenerationMember> CopyMembers", StringComparison.Ordinal),
                "164-22B-3: public generation type constructor still sorts defensively");
        }

        private static void VerifyCanonicalTypesFlowToEmitter()
        {
            var model = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModel.cs");
            var emitter = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/FoxgloveSourceEmitter.cs");
            var metadata = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/TopicMetadataEmitter.cs");
            var toTopicMember = PhaseValidationSourceHelpers.SourceMethod(model, "public FoxgloveSourceEmitter.TopicMember ToTopicMember");
            var canonicalShape = PhaseValidationSourceHelpers.SourceMethod(metadata, "private static string CanonicalTopicShape");

            Check(emitter.Contains("public readonly string CanonicalType;", StringComparison.Ordinal)
                  && toTopicMember.Contains("CanonicalType);", StringComparison.Ordinal),
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
                  && write.Contains("File.ReadAllBytes(path).SequenceEqual(bytes)", StringComparison.Ordinal),
                "164-22D-1: generated file equality check reads existing bytes only after a length match");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-22\"", StringComparison.Ordinal), "164-22E-1: validation registry exposes Phase164-22");
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
