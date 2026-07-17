// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-23 validation for source generator and analyzer behavior.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_23Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-23: Source Generator and Analyzer Behavior ===");
            _passed = 0;

            NullableNumericSinkJsonUsesNumberTokens();
            ConditionDiagnosticsUseStableIds();
            DescriptorCarrierChunksLargeJson();
            UnknownGeneratorDiagnosticsFailClosed();
            ReflectionBuildErrorsAreActionable();
            AnalyzerReleaseTrackingPreservesHistory();
            PhaseWiringIsPresent();

            Console.WriteLine($"Phase 163-23: {_passed} checks passed.");
        }

        private static void NullableNumericSinkJsonUsesNumberTokens()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/PublishDispatchEmitter.cs");
            var golden = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Fixtures/FoxRunGenerationModelFixture_FoxRun.golden.cs");

            Check(source.Contains("TryUnwrapNullableType(type, out var nullableType)", StringComparison.Ordinal)
                  && source.Contains("EmitNullableJsonValueAppend(sb, nullableType, access, pad)", StringComparison.Ordinal),
                "163-23A-1: shared FoxRun emitter unwraps Nullable<T> before sink JSON emission");
            Check(source.Contains("__json.Append({access}.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));", StringComparison.Ordinal),
                "163-23A-2: nullable integral sink JSON uses invariant numeric Value formatting");
            Check(golden.Contains("this._optionalCount.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture)", StringComparison.Ordinal)
                  && !golden.Contains("__AppendFoxRunJsonString(__json, this._optionalCount == null ? null : this._optionalCount.ToString())", StringComparison.Ordinal),
                "163-23A-3: FoxRun golden baseline records nullable integer as JSON number");
        }

        private static void ConditionDiagnosticsUseStableIds()
        {
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();
            var validator = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationModelValidator.cs");
            var invalidUnless = FoxRunGenerationModelValidator.Validate(new FoxRunGenerationModel(new[]
            {
                new FoxRunGenerationType(
                    "Demo",
                    "ConditionalTelemetry",
                    new[]
                    {
                        new FoxRunGenerationMember(
                            "Demo", "ConditionalTelemetry", "_speed", "field", "float",
                            true, false, "", "/phase163/condition", 10f, "",
                            0, 0f, 0f, "UnitTest", 0, "", unless: "not valid")
                    })
            }));

            Check(invalidUnless.Any(diagnostic => diagnostic.Id == "FOXRUN601" && diagnostic.MemberName == "_speed"),
                "163-23B-1: invalid Unless condition names use FOXRUN601 instead of When/boolean diagnostics");
            Check(generator.Contains("TryGetConditionDiagnostic(containingType, topics, out var conditionDiagnosticId)", StringComparison.Ordinal)
                  && generator.Contains("diagnosticId = \"FOXRUN016\";", StringComparison.Ordinal)
                  && generator.Contains("\"FOXRUN601\"", StringComparison.Ordinal)
                  && generator.Contains("SpecialType.System_Boolean", StringComparison.Ordinal),
                "163-23B-2: Roslyn generator validates resolved When/Unless members are bool");
            Check(validator.Contains("FoxRun Unless condition member name is invalid or missing.", StringComparison.Ordinal)
                  && !validator.Contains("Error(\"FOXRUN016\", target, member.MemberName, \"FoxRun Unless condition member name is invalid or missing.\")", StringComparison.Ordinal),
                "163-23B-3: shared validator no longer maps invalid Unless syntax to FOXRUN016");
        }

        private static void DescriptorCarrierChunksLargeJson()
        {
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();

            Check(generator.Contains("if (escaped.Length > 60000)", StringComparison.Ordinal)
                  && generator.Contains("ChunkedDescriptorCarrierSource(escaped)", StringComparison.Ordinal),
                "163-23C-1: descriptor carrier switches to chunked output before the IL string limit");
            Check(generator.Contains("private const string DescriptorJsonPart", StringComparison.Ordinal)
                  && generator.Contains("public static readonly string DescriptorJson = string.Concat", StringComparison.Ordinal),
                "163-23C-2: chunked descriptor carrier keeps per-string constants below the large literal limit");
            Check(generator.Contains("public static readonly string DescriptorJson = \\\"", StringComparison.Ordinal),
                "163-23C-3: small descriptor carrier uses the same readonly API shape");
        }

        private static void UnknownGeneratorDiagnosticsFailClosed()
        {
            var generator = PhaseValidationSourceHelpers.ReadFoxgloveLogSourceGeneratorSources();

            Check(generator.Contains("UnknownFoxRunDiagnostic(string id)", StringComparison.Ordinal)
                  && generator.Contains("UnknownFoxServiceDiagnostic(string id)", StringComparison.Ordinal),
                "163-23D-1: source generator has fallback descriptors for unmapped diagnostics");
            Check(!generator.Contains("throw new ArgumentOutOfRangeException(nameof(id)", StringComparison.Ordinal),
                "163-23D-2: unmapped generator diagnostics no longer crash source generation");
        }

        private static void ReflectionBuildErrorsAreActionable()
        {
            var scanner = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunAssemblyScanner.cs");

            Check(scanner.Contains("CreateInboundTargetNotWritableException", StringComparison.Ordinal)
                  && scanner.Contains("FOXRUN203 Error: ", StringComparison.Ordinal),
                "163-23E-1: reflection build path formats readonly inbound failures as FOXRUN203 errors");
            Check(scanner.Contains("FoxRun inbound \" + memberKind", StringComparison.Ordinal)
                  && scanner.Contains("cannot receive SubscribeOnly or PublishAndSubscribe messages", StringComparison.Ordinal),
                "163-23E-2: FOXRUN203 reflection failure message includes member kind and unsupported shape");
        }

        private static void AnalyzerReleaseTrackingPreservesHistory()
        {
            var shipped = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            var unshipped = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Unshipped.md");

            foreach (var id in new[] { "FOXRUN006", "FOXRUN014", "FOXRUN018" })
            {
                Check(shipped.Contains(id + " | FoxRun |", StringComparison.Ordinal),
                    "163-23F-1: shipped analyzer release notes include " + id);
                Check(!unshipped.Contains(id + " | FoxRun |", StringComparison.Ordinal),
                    "163-23F-2: unshipped analyzer release notes no longer list shipped " + id);
            }

            Check(shipped.Contains("FOXRUN028 | FoxRun |", StringComparison.Ordinal)
                  && unshipped.Contains("FOXRUN203 | FoxRun |", StringComparison.Ordinal)
                  && unshipped.Contains(
                      "FOXRUN028 | FoxRun | Error | Retired; renumbered as FOXRUN203 and permanently reserved.",
                      StringComparison.Ordinal),
                "163-23F-3: analyzer release tracking preserves the retired FOXRUN028 history and records its replacement");
        }

        private static void PhaseWiringIsPresent()
        {
            var csproj = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(csproj.Contains("Phase163_23Validation.cs", StringComparison.Ordinal),
                "163-23G-1: runtime test project compiles Phase163_23Validation");
            Check(registry.Contains("\"--phase163-23\"", StringComparison.Ordinal)
                  && registry.Contains("Phase163_23Validation.Validate", StringComparison.Ordinal),
                "163-23G-2: validation registry exposes --phase163-23");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            return File.ReadAllText(path);
        }

        private static string RepoPath(string relativePath)
        {
            var current = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(current);
                if (parent == null)
                    break;
                current = parent.FullName;
            }

            throw new FileNotFoundException("Could not locate repo file: " + relativePath);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
