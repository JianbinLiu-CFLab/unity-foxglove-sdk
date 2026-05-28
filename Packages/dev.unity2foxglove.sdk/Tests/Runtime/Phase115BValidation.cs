// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 115B schema evidence identity UX validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase115BValidation
    {
        private const string ExpectedFoxRunFixtureHash = "653e287d1f7a491f75b5995affcf182dad9ec594c12ec2535428cab55dd1814d";
        private const string MismatchedHash = "0000000000000000000000000000000000000000000000000000000000000000";
        private const string RuntimeIdentityModePath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SchemaIdentityMode.cs";
        private const string SidecarWriterPath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/SchemaEvidenceSidecarWriter.cs";
        private const string ReplayControllerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs";
        private const string RuntimePath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.cs";
        private const string ManagerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
        private const string ManagerSetupPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs";
        private const string ManagerServerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs";
        private const string ManagerEditorPath = "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";
        private const string EditorSettingsPath = "Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidenceSettings.cs";
        private const string EditorPathsPath = "Packages/dev.unity2foxglove.sdk/Editor/SchemaEvidence/Unity2FoxgloveSchemaEvidencePaths.cs";
        private const string FoxRunGeneratorPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs";
        private const string FoxRunPlayHookPath = "Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunManifestPlayModeHook.cs";
        private const string AggregateGeneratorPath = "Packages/dev.unity2foxglove.sdk/Editor/SchemaManifest/Unity2FoxgloveSchemaManifestGenerator.cs";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 115B: Schema Evidence Identity UX ===");
            _passed = 0;

            VerifyIdentityModeEnumsAndManagerDefaults();
            VerifyReplayPolicyModes();
            VerifySchemaEvidenceSidecarWriter();
            VerifySourceWiring();
            VerifyDocs();

            Console.WriteLine($"Phase 115B: {_passed} checks passed.");
        }

        private static void VerifyIdentityModeEnumsAndManagerDefaults()
        {
            Check((int)SchemaIdentityModeSource.ProjectSettings == 0
                  && (int)SchemaIdentityModeSource.Override == 1,
                "115B-A1: identity mode source enum preserves serialized values");

            Check((int)SchemaIdentityMode.Off == 0
                  && (int)SchemaIdentityMode.Warn == 1
                  && (int)SchemaIdentityMode.Strict == 2,
                "115B-A2: identity mode enum preserves Off/Warn/Strict serialized values");

            var manager = ReadRepoText(ManagerPath);
            Check(manager.Contains("_identityModeSource", StringComparison.Ordinal)
                  && manager.Contains("_identityModeOverride", StringComparison.Ordinal)
                  && manager.Contains("EffectiveSchemaIdentityMode", StringComparison.Ordinal)
                  && manager.Contains("SchemaIdentityMode.Off", StringComparison.Ordinal),
                "115B-A3: FoxgloveManager exposes identity mode source, override, and Off default");
        }

        private static void VerifyReplayPolicyModes()
        {
            var current = FixtureRuntimeInfo(ExpectedFoxRunFixtureHash);
            FoxRunSchemaMcapMetadata.TryCreateJson(current, out var matchingJson);
            FoxRunSchemaMcapMetadata.TryCreateJson(FixtureRuntimeInfo(MismatchedHash), out var mismatchedJson);
            var matchingPath = CreateTempMcapWithSchemaMetadata(matchingJson, "matching");
            var mismatchPath = CreateTempMcapWithSchemaMetadata(mismatchedJson, "mismatch");

            try
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                FoxRunSchemaInfoRegistry.RegisterGenerated(current);

                var legacyLogger = new CaptureLogger();
                using (var legacy = new ReplayController(legacyLogger))
                {
                    legacy.Enable(mismatchPath, new PlaybackClock(new FixedClock(0)), recordingEnabled: false);
                    Check(!legacy.IsEnabled
                          && legacy.LastEnableBlockedBySchemaMismatch
                          && legacy.LastEnableFailureMessage.Contains("Replay blocked.", StringComparison.Ordinal),
                        "115B-B1: existing ReplayController.Enable overload remains strict-compatible");
                }

                var offLogger = new CaptureLogger();
                using (var off = new ReplayController(offLogger))
                {
                    off.Enable(
                        mismatchPath,
                        new PlaybackClock(new FixedClock(0)),
                        recordingEnabled: false,
                        identityMode: SchemaIdentityMode.Off);
                    Check(off.IsEnabled
                          && !off.LastEnableHadSchemaMismatch
                          && !off.LastEnableBlockedBySchemaMismatch
                          && string.IsNullOrEmpty(offLogger.LastWarning)
                          && string.IsNullOrEmpty(offLogger.LastError),
                        "115B-B2: Off mode skips schema identity checks and loads mismatched replay");
                }

                var warnLogger = new CaptureLogger();
                using (var warn = new ReplayController(warnLogger))
                {
                    warn.Enable(
                        mismatchPath,
                        new PlaybackClock(new FixedClock(0)),
                        recordingEnabled: false,
                        identityMode: SchemaIdentityMode.Warn);
                    Check(warn.IsEnabled
                          && warn.LastEnableHadSchemaMismatch
                          && !warn.LastEnableBlockedBySchemaMismatch
                          && warnLogger.LastWarning.Contains("FoxRun replay schema mismatch.", StringComparison.Ordinal)
                          && warnLogger.LastWarning.Contains("Recorded: 000000000000", StringComparison.Ordinal)
                          && warnLogger.LastWarning.Contains("Current:  653e287d1f7a", StringComparison.Ordinal)
                          && warnLogger.LastWarning.Contains("will continue", StringComparison.OrdinalIgnoreCase)
                          && !warnLogger.LastWarning.Contains("Replay blocked.", StringComparison.Ordinal),
                        "115B-B3: Warn mode reports mismatch but continues replay load");
                }

                var strictLogger = new CaptureLogger();
                using (var strict = new ReplayController(strictLogger))
                {
                    strict.Enable(
                        mismatchPath,
                        new PlaybackClock(new FixedClock(0)),
                        recordingEnabled: false,
                        identityMode: SchemaIdentityMode.Strict);
                    Check(!strict.IsEnabled
                          && strict.LastEnableHadSchemaMismatch
                          && strict.LastEnableBlockedBySchemaMismatch
                          && strictLogger.LastError.Contains("Replay blocked.", StringComparison.Ordinal),
                        "115B-B4: Strict mode blocks mismatched replay load");
                }

                var matchingLogger = new CaptureLogger();
                using (var matching = new ReplayController(matchingLogger))
                {
                    matching.Enable(
                        matchingPath,
                        new PlaybackClock(new FixedClock(0)),
                        recordingEnabled: false,
                        identityMode: SchemaIdentityMode.Warn);
                    Check(matching.IsEnabled
                          && !matching.LastEnableHadSchemaMismatch
                          && string.IsNullOrEmpty(matchingLogger.LastError),
                        "115B-B5: matching replay loads cleanly under policy-aware overloads");
                }
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                TryDelete(matchingPath);
                TryDelete(mismatchPath);
            }
        }

        private static void VerifySchemaEvidenceSidecarWriter()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase115b-" + Guid.NewGuid().ToString("N"));
            var currentRoot = Path.Combine(tempRoot, "Generated");
            var mcapPath = Path.Combine(tempRoot, "schema_acceptance_20260521_135001.mcap");

            try
            {
                Directory.CreateDirectory(tempRoot);
                CreateMinimalMcap(mcapPath);
                CreateCurrentEvidenceFixture(currentRoot);

                var warnResult = SchemaEvidenceSidecarWriter.WriteSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Warn,
                    requireComplete: false);

                var sidecarRoot = Path.ChangeExtension(mcapPath, ".schema");
                var indexPath = Path.Combine(sidecarRoot, "schema-evidence.json");
                Check(warnResult.Success
                      && warnResult.Complete
                      && Directory.Exists(sidecarRoot)
                      && File.Exists(indexPath)
                      && File.Exists(Path.Combine(sidecarRoot, "FoxRun", "foxrun.manifest.json"))
                      && File.Exists(Path.Combine(sidecarRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.json")),
                    "115B-C1: recording sidecar copies current FoxRun and aggregate evidence beside the MCAP file");

                var index = JObject.Parse(File.ReadAllText(indexPath, Encoding.UTF8));
                Check((int)index["version"] == 1
                      && (string)index["mcapFile"] == Path.GetFileName(mcapPath)
                      && (string)index["identityMode"] == "Warn"
                      && (bool)index["complete"]
                      && (string)index["foxRun"]["globalManifestHash"] == ExpectedFoxRunFixtureHash
                      && (string)index["unity2Foxglove"]["sdkSchemaManifestHash"] == MismatchedHash,
                    "115B-C2: sidecar index records MCAP name, identity mode, completeness, and both evidence hashes");

                Directory.Delete(sidecarRoot, recursive: true);
                File.Delete(Path.Combine(currentRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.hash"));
                var incompleteWarn = SchemaEvidenceSidecarWriter.WriteSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Warn,
                    requireComplete: false);
                Check(incompleteWarn.Success
                      && !incompleteWarn.Complete
                      && incompleteWarn.Warnings.Any(w => w.Contains("unity2foxglove.schema-manifest.hash", StringComparison.Ordinal)),
                    "115B-C3: Warn mode records incomplete evidence as warning-only");

                Directory.Delete(sidecarRoot, recursive: true);
                var incompleteStrict = SchemaEvidenceSidecarWriter.WriteSidecar(
                    mcapPath,
                    currentRoot,
                    SchemaIdentityMode.Strict,
                    requireComplete: true);
                Check(!incompleteStrict.Success
                      && !incompleteStrict.Complete
                      && incompleteStrict.Warnings.Any(w => w.Contains("unity2foxglove.schema-manifest.hash", StringComparison.Ordinal)),
                    "115B-C4: Strict mode reports incomplete recording evidence as startup-blocking");
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static void VerifySourceWiring()
        {
            foreach (var path in new[]
            {
                RuntimeIdentityModePath,
                SidecarWriterPath,
                ReplayControllerPath,
                RuntimePath,
                ManagerPath,
                ManagerSetupPath,
                ManagerServerPath,
                EditorSettingsPath,
                EditorPathsPath,
                ManagerEditorPath
            })
            {
                Check(RepoFileExists(path), "115B-D1: required source file exists: " + path);
            }

            var replay = ReadRepoText(ReplayControllerPath);
            Check(replay.Contains("identityMode = SchemaIdentityMode.Strict", StringComparison.Ordinal)
                  && replay.Contains("SchemaIdentityMode.Off", StringComparison.Ordinal)
                  && replay.Contains("SchemaIdentityMode.Warn", StringComparison.Ordinal)
                  && replay.Contains("LastEnableHadSchemaMismatch", StringComparison.Ordinal),
                "115B-D2: ReplayController exposes policy-aware Off/Warn/Strict schema guard behavior");

            var runtime = ReadRepoText(RuntimePath);
            Check(runtime.Contains("ReplayStartHadSchemaMismatch", StringComparison.Ordinal)
                  && runtime.Contains("EnableReplay(string filePath, SchemaIdentityMode identityMode)", StringComparison.Ordinal),
                "115B-D3: FoxgloveRuntime forwards replay identity policy and mismatch status");

            var setup = ReadRepoText(ManagerSetupPath);
            var server = ReadRepoText(ManagerServerPath);
            Check(setup.Contains("private bool SetupRecording()", StringComparison.Ordinal)
                  && server.Contains("if (!SetupRecording())", StringComparison.Ordinal)
                  && setup.Contains("SchemaEvidenceSidecarWriter.StageSidecar", StringComparison.Ordinal)
                  && setup.Contains("SchemaEvidenceSidecarWriter.PublishStagedSidecar", StringComparison.Ordinal)
                  && setup.Contains("EffectiveSchemaIdentityMode", StringComparison.Ordinal)
                  && setup.Contains("ReplayStartHadSchemaMismatch", StringComparison.Ordinal)
                  && setup.Contains("mixed replay/live data", StringComparison.Ordinal),
                "115B-D4: Manager startup prepares evidence sidecars and preserves Warn-mode live/replay diagnostics");

            var paths = ReadRepoText(EditorPathsPath);
            Check(paths.Contains("DefaultCurrentEvidenceRoot = \"Assets/Generated\"", StringComparison.Ordinal)
                  && paths.Contains("ResolveFoxRunOutputDirectory", StringComparison.Ordinal)
                  && paths.Contains("ResolveUnity2FoxgloveOutputDirectory", StringComparison.Ordinal),
                "115B-D5: Editor resolver owns the current schema evidence root and generated output directories");

            Check(ReadRepoText(FoxRunGeneratorPath).Contains("ResolveFoxRunOutputDirectory", StringComparison.Ordinal)
                  && ReadRepoText(FoxRunPlayHookPath).Contains("ResolveFoxRunOutputDirectory", StringComparison.Ordinal)
                  && ReadRepoText(AggregateGeneratorPath).Contains("ResolveUnity2FoxgloveOutputDirectory", StringComparison.Ordinal),
                "115B-D6: FoxRun and aggregate generation share the schema evidence path resolver");

            var editor = ReadRepoText(ManagerEditorPath);
            var mcapEditor = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.Mcap.cs");
            Check(mcapEditor.Contains("Schema Evidence (Advanced)", StringComparison.Ordinal)
                  && mcapEditor.Contains("_identityModeSource", StringComparison.Ordinal)
                  && mcapEditor.Contains("_identityModeOverride", StringComparison.Ordinal)
                  && mcapEditor.Contains("Apply Project Defaults", StringComparison.Ordinal)
                  && mcapEditor.Contains("Refresh Evidence Now", StringComparison.Ordinal)
                  && mcapEditor.Contains("Edit Project Settings", StringComparison.Ordinal)
                  && mcapEditor.Contains("Open Current Evidence", StringComparison.Ordinal)
                  && mcapEditor.Contains("Copy Hash", StringComparison.Ordinal),
                "115B-D7: Manager Inspector surfaces identity mode and current evidence controls near recording/replay");

            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase115BValidation.cs", StringComparison.Ordinal)
                  && validationRegistry.Contains("--phase115b", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase115BValidation.Validate", StringComparison.Ordinal),
                "115B-D8: runtime validation project compiles Phase115B tests and validation registry dispatches them");
        }

        private static void VerifyDocs()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/13_Schema_Coverage.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/08_MCAP录制回放.md",
                "docs/research-shared-emitter-architecture.md"
            })
            {
                var doc = ReadRepoText(path);
                Check(doc.Contains("Schema Evidence", StringComparison.Ordinal)
                      && doc.Contains("Off", StringComparison.Ordinal)
                      && doc.Contains("Warn", StringComparison.Ordinal)
                      && doc.Contains("Strict", StringComparison.Ordinal)
                      && doc.Contains(".schema", StringComparison.Ordinal)
                      && doc.Contains("Unity2Foxglove", StringComparison.Ordinal),
                    "115B-E1: docs explain schema evidence path, sidecars, and Off/Warn/Strict identity modes: " + path);
            }
        }

        private static void CreateCurrentEvidenceFixture(string currentRoot)
        {
            var foxRun = Path.Combine(currentRoot, "FoxRun");
            var aggregate = Path.Combine(currentRoot, "Unity2Foxglove");
            Directory.CreateDirectory(foxRun);
            Directory.CreateDirectory(aggregate);

            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.hash"), ExpectedFoxRunFixtureHash + "\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(foxRun, "foxrun.manifest.report.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(foxRun, "FoxRunSchemaInfo.g.cs"), "// schema info\n", Encoding.UTF8);

            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.json"), "{}", Encoding.UTF8);
            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.hash"), MismatchedHash + "\n", Encoding.UTF8);
            File.WriteAllText(Path.Combine(aggregate, "unity2foxglove.schema-manifest.report.json"), "{}", Encoding.UTF8);
        }

        private static string CreateTempMcapWithSchemaMetadata(string metadataJson, string label)
        {
            var path = Path.Combine(Path.GetTempPath(), "unity2foxglove-phase115b-" + label + "-" + Guid.NewGuid().ToString("N") + ".mcap");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(1, "/phase115b/" + label, "json", "", "", "");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{}"));
                if (metadataJson != null)
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, metadataJson);
                recorder.Close();
            }

            return path;
        }

        private static void CreateMinimalMcap(string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(1, "/phase115b/sidecar", "json", "", "", "");
                recorder.WriteMessage(1, 1, Encoding.UTF8.GetBytes("{}"));
                recorder.Close();
            }
        }

        private static FoxRunSchemaManifestInfo FixtureRuntimeInfo(string globalHash)
        {
            var manifest = FoxRunManifestBuilder.Build(new[]
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

            return new FoxRunSchemaManifestInfo(
                manifest.ManifestVersion,
                manifest.Package,
                manifest.Generator.Name,
                manifest.Generator.MajorVersion,
                globalHash,
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
            => File.Exists(RepoPath(relativePath));

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase115B file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase115B validation.");
            return root;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for temporary validation files.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temporary validation directories.
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private sealed class CaptureLogger : IFoxgloveLogger
        {
            public string LastWarning { get; private set; } = string.Empty;
            public string LastError { get; private set; } = string.Empty;

            public void LogWarning(string message)
            {
                LastWarning = message ?? string.Empty;
            }

            public void LogError(string message)
            {
                LastError = message ?? string.Empty;
            }
        }

        private sealed class FixedClock : IFoxgloveClock
        {
            public FixedClock(ulong nowNs)
            {
                NowNs = nowNs;
            }

            public ulong NowNs { get; }
        }
    }
}
