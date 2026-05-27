// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 113 FoxRun runtime schema info and drift gate validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase113Validation
    {
        private const string ExpectedGlobalFixtureHash = "9a0f11b37e2893c60aadd6edddf6b83cae27407041c8a5dc413579ead7a1d58e";
        private const string ExpectedFoxRunFixtureHash = "653e287d1f7a491f75b5995affcf182dad9ec594c12ec2535428cab55dd1814d";
        private const string SchemaWriterPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs";
        private const string CodeGeneratorPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs";
        private const string BuildPreprocessPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs";
        private const string PlayModeHookPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 113: FoxRun SchemaInfo Runtime Registry And Drift Gate ===");
            _passed = 0;

            VerifyGeneratedSourceFixture();
            VerifyRuntimeRegistryBehavior();
            VerifyWriterFilesAndMeta();
            VerifySourceBoundariesAndWiring();
            VerifyDocs();

            Console.WriteLine($"Phase 113: {_passed} checks passed.");
        }

        private static void VerifyGeneratedSourceFixture()
        {
            var manifest = FixtureManifest();
            var source = FoxRunSchemaInfoWriter.GenerateSource(manifest);
            var verification = FoxRunSchemaInfoWriter.VerifyGeneratedInfo(manifest, source);

            Check(verification.IsValid
                  && verification.ActualGlobalManifestHash == ExpectedGlobalFixtureHash
                  && verification.ActualFoxRunManifestHash == ExpectedFoxRunFixtureHash,
                "113-A1: generated schema info constants match fixture manifest hashes"
                + $" (expected global {ExpectedGlobalFixtureHash}, expected foxrun {ExpectedFoxRunFixtureHash}, "
                + $"actual global {verification.ActualGlobalManifestHash}, actual foxrun {verification.ActualFoxRunManifestHash})");

            Check(source.Contains("public const int TypeCount = 1;", StringComparison.Ordinal)
                  && source.Contains("public const int ContractCount = 1;", StringComparison.Ordinal)
                  && source.Contains("public const int FieldCount = 1;", StringComparison.Ordinal)
                  && source.Contains("FoxRunSchemaInfoRegistry.RegisterGenerated", StringComparison.Ordinal),
                "113-A2: generated schema info includes counts and registration call");

            Check(HasBalancedGeneratedSourceDelimiters(source)
                  && !NormalizeNewlines(source).Contains("}),\n", StringComparison.Ordinal),
                "113-A2b: generated schema info has balanced C# initializer delimiters");

            Check(!source.Contains("#if !UNITY_EDITOR", StringComparison.Ordinal)
                  && source.Contains("RuntimeInitializeOnLoadMethod", StringComparison.Ordinal),
                "113-A3: generated schema info is visible to Editor Play Mode and Player runtime");

            foreach (var token in new[] { "generatedAtUtc", "Library/", "MCAP", "replay", "protobuf", "ROS2", "typed-publisher" })
            {
                Check(!source.Contains(token, StringComparison.OrdinalIgnoreCase),
                    "113-A4: generated schema info avoids out-of-scope token: " + token);
            }
        }

        private static void VerifyRuntimeRegistryBehavior()
        {
            var manifest = FixtureManifest();
            var first = ToRuntimeInfo(manifest);
            var sameHash = ToRuntimeInfo(manifest);
            var conflicting = new FoxRunSchemaManifestInfo(
                first.ManifestVersion,
                first.PackageName,
                first.GeneratorName,
                first.GeneratorMajorVersion,
                "0000000000000000000000000000000000000000000000000000000000000000",
                first.FoxRunManifestHash,
                first.Types);

            FoxRunSchemaInfoRegistry.ClearForTests();
            Check(!FoxRunSchemaInfoRegistry.HasGeneratedSchemaInfo
                  && !FoxRunSchemaInfoRegistry.HasConflict
                  && FoxRunSchemaInfoRegistry.Current == null,
                "113-B1: registry starts empty after test clear");

            FoxRunSchemaInfoRegistry.RegisterGenerated(first);
            Check(FoxRunSchemaInfoRegistry.HasGeneratedSchemaInfo
                  && FoxRunSchemaInfoRegistry.Current == first
                  && FoxRunSchemaInfoRegistry.Current.GlobalManifestHash == ExpectedGlobalFixtureHash
                  && FoxRunSchemaInfoRegistry.Current.TypeCount == 1
                  && FoxRunSchemaInfoRegistry.Current.ContractCount == 1
                  && FoxRunSchemaInfoRegistry.Current.FieldCount == 1,
                "113-B2: registry exposes manifest hash and schema metadata without reflection");

            FoxRunSchemaInfoRegistry.RegisterGenerated(sameHash);
            Check(!FoxRunSchemaInfoRegistry.HasConflict
                  && FoxRunSchemaInfoRegistry.Current == first,
                "113-B3: same-hash duplicate registration preserves the first manifest");

            FoxRunSchemaInfoRegistry.RegisterGenerated(conflicting);
            Check(FoxRunSchemaInfoRegistry.HasConflict
                  && FoxRunSchemaInfoRegistry.Current == first
                  && FoxRunSchemaInfoRegistry.ConflictingHash == conflicting.GlobalManifestHash
                  && FoxRunSchemaInfoRegistry.ConflictMessage.Contains("different manifest hash", StringComparison.Ordinal),
                "113-B4: different-hash registration records conflict without replacing current manifest");

            FoxRunSchemaInfoRegistry.ClearForTests();
        }

        private static void VerifyWriterFilesAndMeta()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase113-" + Guid.NewGuid().ToString("N"));
            try
            {
                var manifest = FixtureManifest();
                var verification = FoxRunSchemaInfoWriter.WriteGeneratedInfoFiles(tempRoot, manifest);
                var sourcePath = Path.Combine(tempRoot, FoxRunSchemaInfoWriter.SchemaInfoFileName);
                var metaPath = Path.Combine(tempRoot, FoxRunSchemaInfoWriter.SchemaInfoMetaFileName);
                var sourceBytes = File.ReadAllBytes(sourcePath);

                Check(verification.IsValid
                      && File.Exists(sourcePath)
                      && File.Exists(metaPath)
                      && !sourceBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
                    "113-C1: writer emits schema info source and stable Unity meta without BOM");

                var firstMeta = File.ReadAllText(metaPath);
                Check(firstMeta.Contains("fileFormatVersion: 2", StringComparison.Ordinal)
                      && firstMeta.Contains("guid:", StringComparison.Ordinal),
                    "113-C2: writer creates a valid minimal Unity meta file");

                const string customMeta = "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n";
                File.WriteAllText(metaPath, customMeta);
                FoxRunSchemaInfoWriter.WriteGeneratedInfoFiles(tempRoot, manifest);
                Check(File.ReadAllText(metaPath) == customMeta,
                    "113-C3: writer preserves an existing schema info meta file");
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
        }

        private static void VerifySourceBoundariesAndWiring()
        {
            foreach (var path in new[]
            {
                SchemaWriterPath,
                CodeGeneratorPath,
                BuildPreprocessPath,
                PlayModeHookPath,
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaManifestInfo.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaTypeInfo.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaContractInfo.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaFieldInfo.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs"
            })
            {
                Check(RepoFileExists(path), "113-D1: schema info source file exists: " + path);
            }

            var writer = ReadRepoText(SchemaWriterPath);
            foreach (var token in new[] { "FoxRunManifestHasher", "WriteCanonical", "SHA256", "generatedAtUtc", "MCAP", "replay", "protobuf", "ROS2", "typed-publisher" })
            {
                Check(!writer.Contains(token, StringComparison.OrdinalIgnoreCase),
                    "113-D2: schema writer avoids second hash path or out-of-scope token: " + token);
            }

            var codegen = ReadRepoText(CodeGeneratorPath);
            Check(codegen.Contains("FoxRunSchemaInfoWriter.WriteGeneratedInfoFiles", StringComparison.Ordinal)
                  && codegen.Contains("VerifyGeneratedSchemaInfoFiles", StringComparison.Ordinal),
                "113-D3: code generator writes and verifies schema info from manifest objects");

            var playModeHook = ReadRepoText(PlayModeHookPath);
            Check(playModeHook.Contains("GenerateManifestFilesOnly", StringComparison.Ordinal)
                  && codegen.Contains("GenerateManifestAndSchemaInfoFilesOnly", StringComparison.Ordinal)
                  && !playModeHook.Contains("GenerateSourceFiles()", StringComparison.Ordinal),
                "113-D4: Editor Play Mode refresh writes manifest and schema info without physical publisher fallback files");

            var buildPreprocess = ReadRepoText(BuildPreprocessPath);
            Check(buildPreprocess.Contains("GenerateSourceFiles", StringComparison.Ordinal)
                  && buildPreprocess.Contains("VerifyGeneratedSchemaInfoFiles", StringComparison.Ordinal)
                  && buildPreprocess.IndexOf("GenerateSourceFiles", StringComparison.Ordinal) <
                  buildPreprocess.IndexOf("VerifyGeneratedSchemaInfoFiles", StringComparison.Ordinal),
                "113-D5: build drift gate verifies schema info after regeneration");

            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Attributes/FoxRunAttribute.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveLogHub.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter.cs"
            })
            {
                Check(!ReadRepoText(path).Contains("FoxRunSchemaInfoRegistry", StringComparison.Ordinal),
                    "113-D6: existing FoxRun runtime contract file does not depend on schema registry: " + path);
            }

            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaInfoRegistry.cs");
            Check(registry.Contains("RuntimeInitializeLoadType.SubsystemRegistration", StringComparison.Ordinal)
                  && registry.Contains("ResetForRuntimeLoad", StringComparison.Ordinal)
                  && registry.Contains("ResetState", StringComparison.Ordinal),
                "113-D6b: schema registry resets static state for each Unity runtime load");

            Check(playModeHook.Contains("FoxRunSchemaInfo.g.cs changed before Play Mode", StringComparison.Ordinal)
                  && playModeHook.Contains("EditorApplication.isPlaying = false", StringComparison.Ordinal)
                  && playModeHook.Contains("ForceSynchronousImport", StringComparison.Ordinal),
                "113-D6c: Play Mode refresh cancels once when generated schema info source changes");

            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase113Validation.cs", StringComparison.Ordinal)
                  && project.Contains("Runtime/Components/FoxRun/FoxRunSchema*.cs", StringComparison.Ordinal)
                  && project.Contains("FoxRunSchemaInfoWriter.cs", StringComparison.Ordinal),
                "113-D7: runtime validation project compiles schema registry, writer, and Phase113 tests");
            Check(validationRegistry.Contains("--phase113", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase113Validation.Validate", StringComparison.Ordinal),
                "113-D8: validation registry dispatches --phase113 and full validation includes Phase113");
        }

        private static void VerifyDocs()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md",
                "docs/research-shared-emitter-architecture.md"
            })
            {
                var doc = ReadRepoText(path);
                Check(doc.Contains("runtime schema info", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("manifest hash", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("Editor Play Mode", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("MCAP", StringComparison.OrdinalIgnoreCase),
                    "113-E1: docs describe runtime schema info as manifest-hash evidence for later MCAP/replay use: " + path);
            }
        }

        private static FoxRunCanonicalManifest FixtureManifest()
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Demo",
                    "RobotState",
                    "_batteryLevel",
                    "field",
                    "System.Single",
                    true,
                    false,
                    "",
                    "/phase112/battery",
                    10f,
                    "",
                    1,
                    0.001f,
                    0f)
            });
        }

        private static FoxRunSchemaManifestInfo ToRuntimeInfo(FoxRunCanonicalManifest manifest)
        {
            return new FoxRunSchemaManifestInfo(
                manifest.ManifestVersion,
                manifest.Package,
                manifest.Generator.Name,
                manifest.Generator.MajorVersion,
                manifest.GlobalManifestHash,
                manifest.Sections.FoxRun.ManifestHash,
                manifest.Sections.FoxRun.Types.Select(type =>
                    new FoxRunSchemaTypeInfo(
                        type.DeclaringType,
                        type.Contracts.Select(contract =>
                            new FoxRunSchemaContractInfo(
                                contract.DeclaringType,
                                contract.Topic,
                                contract.SchemaName,
                                contract.Encoding,
                                contract.ContractHash,
                                contract.BindingHash,
                                contract.PolicyHash,
                                contract.Policy.Mode,
                                contract.Policy.RateHz,
                                contract.Policy.ChangeEpsilon,
                                contract.Policy.ForceIntervalSeconds,
                                contract.Fields.Select(field =>
                                    new FoxRunSchemaFieldInfo(
                                        field.JsonName,
                                        field.MemberName,
                                        field.MemberKind,
                                        field.Type,
                                        field.Nullable,
                                        field.Array)).ToList())).ToList())).ToList());
        }

        private static bool RepoFileExists(string relativePath)
        {
            return File.Exists(RepoPath(relativePath));
        }

        private static bool HasBalancedGeneratedSourceDelimiters(string source)
        {
            var paren = 0;
            var brace = 0;
            var bracket = 0;
            var inString = false;
            var inVerbatimString = false;
            var inChar = false;
            var inLineComment = false;
            var inBlockComment = false;
            var escaped = false;

            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (c == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (inVerbatimString)
                {
                    if (c == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (c == '"')
                        inVerbatimString = false;
                    continue;
                }

                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;
                    continue;
                }

                if (inChar)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '\'')
                        inChar = false;
                    continue;
                }

                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (c == '@' && next == '"')
                {
                    inVerbatimString = true;
                    i++;
                    continue;
                }

                if (c == '$' && next == '@' && i + 2 < source.Length && source[i + 2] == '"')
                {
                    inVerbatimString = true;
                    i += 2;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '\'')
                {
                    inChar = true;
                    continue;
                }

                switch (c)
                {
                    case '(': paren++; break;
                    case ')': paren--; break;
                    case '{': brace++; break;
                    case '}': brace--; break;
                    case '[': bracket++; break;
                    case ']': bracket--; break;
                }

                if (paren < 0 || brace < 0 || bracket < 0)
                    return false;
            }

            return !inString
                   && !inVerbatimString
                   && !inChar
                   && !inBlockComment
                   && paren == 0
                   && brace == 0
                   && bracket == 0;
        }

        private static string NormalizeNewlines(string value)
            => value.Replace("\r\n", "\n").Replace("\r", "\n");

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase113 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
        {
            return Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase113 validation.");
            return root;
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
