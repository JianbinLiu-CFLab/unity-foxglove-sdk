// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 163-51 review closure for DataLoader and R2FU setup validations.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase163_51Validation
    {
        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            var loader = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoader.cs");
            Check(loader.Contains("var registry = GetDecodeRegistry(options);", StringComparison.Ordinal)
                  && loader.Contains("var decodedMessages = new List<McapDecodedMessage>();", StringComparison.Ordinal)
                  && loader.Contains("return decodedMessages;", StringComparison.Ordinal),
                "163-51A-1: decoded iterator is eager and reuses the cached decode registry");

            var phase116 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase116Validation.cs");
            Check(phase116.Contains("MethodCache.Clear();", StringComparison.Ordinal)
                  && phase116.Contains("MemberCache.Clear();", StringComparison.Ordinal),
                "163-51B-1: Phase116 reflection caches are reset per validation run");
            Check(phase116.Contains("AppDomain.CurrentDomain.GetAssemblies()", StringComparison.Ordinal)
                  && !phase116.Contains("Type.GetType(name + \", FoxgloveSdk.Tests\")", StringComparison.Ordinal),
                "163-51B-2: Phase116 resolves DataLoader types from loaded assemblies");
            Check(phase116.Contains("TypeReferencesUnityEngine", StringComparison.Ordinal)
                  && !phase116.Contains("dto.Assembly.GetReferencedAssemblies()", StringComparison.Ordinal),
                "163-51B-3: Phase116 checks DTO member types instead of assembly-wide UnityEngine references");

            var decodeRegistry = Read("Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDecodeRegistry.cs");
            Check(decodeRegistry.Contains("BuiltInFactories = CreateBuiltInFactoriesLazy();", StringComparison.Ordinal)
                  && decodeRegistry.Contains("GetBuiltInFactories()", StringComparison.Ordinal),
                "163-51C-1: decode registry resets built-in factory discovery on Unity runtime load");

            var phase120 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase120Validation.cs");
            Check(phase120.Contains("git commit provenance unavailable", StringComparison.Ordinal)
                  && phase120.Contains("commitResult.ExitCode", StringComparison.Ordinal),
                "163-51D-1: Phase120 report records missing git provenance as a limitation");

            Check(RepoRootValidation("R2fuActiveRuntimeSelectorValidation.cs")
                  && RepoRootValidation("R2fuHumbleRuntimePackageValidation.cs")
                  && RepoRootValidation("R2fuJazzyRuntimeRefreshValidation.cs")
                  && RepoRootValidation("R2fuLyricalRuntimePackageValidation.cs"),
                "163-51E-1: R2FU validations use shared repo-root discovery");

            var phase125 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase125Validation.cs");
            Check(phase125.Contains("DeserializerCount >= 41", StringComparison.Ordinal)
                  && phase125.Contains("Entries.Count == Ros2CdrDeserializerRegistry.DeserializerCount", StringComparison.Ordinal),
                "163-51F-1: Phase125 uses a schema-count floor plus registry parity");

            var phase120b = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase120BValidation.cs");
            Check(phase120b.Contains("BuildSparseIndexedBackfillFixtureWithOlderChunk", StringComparison.Ordinal)
                  && phase120b.Contains("Crc32Helper.Compute(oldRaw)", StringComparison.Ordinal),
                "163-51F-2: Phase120B early-stop fixture no longer depends on a bad CRC");

            var phase124 = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase124Validation.cs");
            Check(phase124.Contains("124-D2", StringComparison.Ordinal)
                  && phase124.Contains("124-D3", StringComparison.Ordinal),
                "163-51G-1: Phase124 covers decoded iterator cache reuse and disposed fail-fast behavior");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase163_51Validation.cs", StringComparison.Ordinal)
                  && registry.Contains("--phase163-51", StringComparison.Ordinal)
                  && registry.Contains("Phase163_51Validation.Validate", StringComparison.Ordinal),
                "163-51H-1: validation registry exposes --phase163-51");

            Console.WriteLine($"Phase 163-51: {_passed} DataLoader/R2FU validation checks passed.");
        }

        private static bool RepoRootValidation(string fileName)
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);
            return source.Contains("Phase16Validation.FindRepoRoot()", StringComparison.Ordinal)
                   && !source.Contains("AppContext.BaseDirectory, \"..\", \"..\", \"..\", \"..\"", StringComparison.Ordinal);
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
