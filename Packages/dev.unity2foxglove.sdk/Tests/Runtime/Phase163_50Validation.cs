// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-50 review closure for generator/service validation hardening.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_50Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            var phase111f = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase111FValidation.cs");
            Check(!phase111f.Contains("oldLifecycle", StringComparison.Ordinal)
                  && phase111f.Contains("&& currentLifecycle", StringComparison.Ordinal),
                "163-50A-1: Phase111F requires the current R2FU lifecycle pattern");

            var typeNames = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceDtoValidation/FoxServiceDtoTypeNames.cs");
            var phase141d = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceDtoSerializationAnalyzerValidation.cs");
            Check(typeNames.Contains("NormalizeGenericContractName", StringComparison.Ordinal)
                  && typeNames.Contains("System.Collections.Generic.List`1", StringComparison.Ordinal)
                  && typeNames.Contains("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal)
                  && phase141d.Contains("141D-11b", StringComparison.Ordinal),
                "163-50A-2: FoxService DTO type-name helpers accept reflection generic names");

            var phase100 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase100Validation.cs");
            Check(phase100.Contains("FindMethodSignature(source, methodName)", StringComparison.Ordinal)
                  && phase100.Contains("PhaseValidationSourceHelpers.SourceMethod(source.Substring(signatureIndex), methodName)", StringComparison.Ordinal)
                  && !phase100.Contains("if (source[i] == '{') depth++", StringComparison.Ordinal),
                "163-50B-1: Phase100 method extraction uses string/comment-aware source scanning");

            var phase113 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase113Validation.cs");
            Check(phase113.Contains("var value = $\\\"value={x}\\\"", StringComparison.Ordinal)
                  && phase113.Contains("if (c == '$' && next == '\"')", StringComparison.Ordinal),
                "163-50B-2: Phase113 delimiter scan covers plain interpolated strings");

            var phase141f = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceDtoGraphWalkerConvergenceValidation.cs");
            Check(phase141f.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                  && !phase141f.Contains("AppContext.BaseDirectory, \"..\", \"..\", \"..\", \"..\"", StringComparison.Ordinal),
                "163-50C-1: Phase141F reads repository files from the discovered repo root");

            var phase108 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase108Validation.cs");
            Check(phase108.Contains("ParametersMatch(candidate.GetParameters(), args)", StringComparison.Ordinal)
                  && phase108.Contains("ParameterInfo[] parameters", StringComparison.Ordinal),
                "163-50C-2: Phase108 generic reflection helper resolves overloads by parameter types");

            var phase112 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase112Validation.cs");
            Check(phase112.Contains("TryDeleteDirectory(tempRoot)", StringComparison.Ordinal)
                  && phase112.Contains("Failed to delete temporary directory", StringComparison.Ordinal),
                "163-50D-1: Phase112 cleanup does not mask original validation failures");

            var phase107 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs");
            Check(phase107.Contains("107-A8a", StringComparison.Ordinal)
                  && phase107.Contains("107-A8h", StringComparison.Ordinal),
                "163-50D-2: Phase107 optional-editor boundary reports individual failing conditions");

            var phase115b = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase115BValidation.cs");
            var sidecarWriter = Read("Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/SchemaEvidenceSidecarWriter.cs");
            var manifestWriter = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestWriter.cs");
            Check(phase115b.Contains("115B-C2b", StringComparison.Ordinal)
                  && sidecarWriter.Contains("[\"globalManifestHash\"] = foxRunHash ?? string.Empty", StringComparison.Ordinal)
                  && manifestWriter.Contains("ManifestHashFileName), manifest.GlobalManifestHash + \"\\n\")", StringComparison.Ordinal),
                "163-50E-1: Phase115B locks sidecar globalManifestHash to the FoxRun global hash file");

            var phase141e = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxServiceEditorSchemaPolishValidation.cs");
            Check(phase141e.Contains("141E-3b", StringComparison.Ordinal)
                  && phase141e.Contains("generated service descriptor arguments use regular string literals", StringComparison.Ordinal),
                "163-50E-2: Phase141E documents descriptor string literal parsing assumptions");

            var members = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxServiceDtoValidation/FoxServiceDtoReflectionMembers.cs");
            Check(members.Contains("token > 0 ? token : int.MaxValue", StringComparison.Ordinal)
                  && members.Contains("NotSupportedException", StringComparison.Ordinal),
                "163-50F-1: reflection member ordering handles unavailable metadata tokens");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_50Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-50", StringComparison.Ordinal)
                  && registry.Contains("Phase163_50Validation.Validate", StringComparison.Ordinal),
                "163-50G-1: validation registry exposes --phase163-50");

            Console.WriteLine($"Phase 163-50: {_passed} generator/service validation checks passed.");
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidDataException("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
