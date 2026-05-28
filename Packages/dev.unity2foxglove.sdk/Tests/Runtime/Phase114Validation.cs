// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 114 FoxRun MCAP metadata and replay schema guard validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase114Validation
    {
        private const string ExpectedFixtureHash = "653e287d1f7a491f75b5995affcf182dad9ec594c12ec2535428cab55dd1814d";
        private const string MismatchedHash = "0000000000000000000000000000000000000000000000000000000000000000";
        private const string MetadataRuntimePath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunSchemaMcapMetadata.cs";
        private const string RecordingControllerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Recording/RecordingController.cs";
        private const string ReplayControllerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs";
        private const string ReplayEnginePath = "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Replay/McapReplayEngine.cs";
        private const string ManagerRuntimePath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.cs";
        private const string ManagerServerPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs";
        private const string ManagerSetupPath = "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Setup.cs";
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 114: FoxRun MCAP Metadata And Replay Schema Guard ===");
            _passed = 0;

            VerifyMetadataJsonShape();
            VerifyGuardStates();
            VerifyMcapMetadataRoundtrip();
            VerifyRecordingControllerWritesMetadata();
            VerifyReplayControllerGuardBehavior();
            VerifySourceBoundariesAndWiring();
            VerifyDocs();

            Console.WriteLine($"Phase 114: {_passed} checks passed.");
        }

        private static void VerifyMetadataJsonShape()
        {
            var current = FixtureRuntimeInfo();
            Check(FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json),
                "114-A1: runtime schema info serializes to an MCAP metadata JSON value");

            var parsed = JObject.Parse(json);
            Check((int)parsed["schemaMetadataVersion"] == 1
                  && (int)parsed["manifestVersion"] == current.ManifestVersion
                  && (string)parsed["generatorVersion"] == "1.0.0"
                  && (int)parsed["generatorMajorVersion"] == 1,
                "114-A2: metadata JSON includes schema and generator versions");

            Check((string)parsed["globalManifestHash"] == ExpectedFixtureHash
                  && (string)parsed["manifestHash"] == ExpectedFixtureHash
                  && (int)parsed["typeCount"] == 1
                  && (int)parsed["contractCount"] == 1
                  && (int)parsed["fieldCount"] == 1,
                "114-A3: metadata JSON includes manifest hashes and counts");

            var contract = (JObject)parsed["contracts"][0];
            Check((string)contract["topic"] == "/phase112/battery"
                  && (string)contract["schemaName"] == string.Empty
                  && (string)contract["encoding"] == "json"
                  && !string.IsNullOrWhiteSpace((string)contract["contractHash"])
                  && !string.IsNullOrWhiteSpace((string)contract["bindingHash"])
                  && !string.IsNullOrWhiteSpace((string)contract["policyHash"]),
                "114-A4: metadata JSON carries compact contract diagnostic hashes");

            foreach (var forbidden in new[] { "generatedAtUtc", "filePath", "Library/", "AbsolutePath" })
            {
                Check(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    "114-A5: metadata JSON avoids non-canonical runtime-only token: " + forbidden);
            }

            Check(FoxRunSchemaMcapMetadata.TryParseJson(json, out var record, out var error)
                  && string.IsNullOrEmpty(error)
                  && record.GlobalManifestHash == ExpectedFixtureHash
                  && record.Contracts.Count == 1,
                "114-A6: metadata JSON parses back into the runtime record DTO");
        }

        private static void VerifyGuardStates()
        {
            var current = FixtureRuntimeInfo();
            FoxRunSchemaMcapMetadata.TryCreateJson(current, out var matchingJson);
            FoxRunSchemaMcapMetadata.TryCreateJson(MismatchedRuntimeInfo(), out var mismatchedJson);

            var match = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(matchingJson, current);
            Check(match.State == FoxRunReplaySchemaGuardState.Match && !match.IsBlocking,
                "114-B1: matching global manifest hash allows replay");

            var missingRecorded = FoxRunSchemaMcapMetadata.CreateMissingRecordedResult();
            Check(missingRecorded.State == FoxRunReplaySchemaGuardState.MissingRecorded && !missingRecorded.IsBlocking,
                "114-B2: missing recorded metadata is warning-only");

            var missingCurrent = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(matchingJson, null);
            Check(missingCurrent.State == FoxRunReplaySchemaGuardState.MissingCurrent && !missingCurrent.IsBlocking,
                "114-B3: missing current schema info is warning-only");

            var malformed = FoxRunSchemaMcapMetadata.EvaluateRecordedJson("{\"schemaMetadataVersion\":1}", current);
            Check(malformed.State == FoxRunReplaySchemaGuardState.MalformedRecorded && !malformed.IsBlocking,
                "114-B4: malformed recorded metadata is warning-only");

            var mismatch = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(mismatchedJson, current);
            Check(mismatch.State == FoxRunReplaySchemaGuardState.Mismatch
                  && mismatch.IsBlocking
                  && mismatch.Message.Contains("FoxRun replay schema mismatch.", StringComparison.Ordinal)
                  && mismatch.Message.Contains("Recorded: 000000000000", StringComparison.Ordinal)
                  && mismatch.Message.Contains("Current:  653e287d1f7a", StringComparison.Ordinal),
                "114-B5: global manifest hash mismatch blocks replay with short-hash diagnostics");

            var editedDiagnostic = JObject.Parse(matchingJson);
            editedDiagnostic["manifestHash"] = MismatchedHash;
            var sameGlobalDifferentDiagnostic = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(
                editedDiagnostic.ToString(Newtonsoft.Json.Formatting.None),
                current);
            Check(sameGlobalDifferentDiagnostic.State == FoxRunReplaySchemaGuardState.Match,
                "114-B6: replay guard compares only globalManifestHash, not diagnostic fields");
        }

        private static void VerifyMcapMetadataRoundtrip()
        {
            var current = FixtureRuntimeInfo();
            FoxRunSchemaMcapMetadata.TryCreateJson(current, out var json);
            using var ms = new MemoryStream();

            using (var recorder = new McapRecorder(ms, leaveOpen: true))
            {
                recorder.AddChannel(1, "/phase114/a", "json", "", "", "");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{}"));
                recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, json);
                recorder.Close();
            }

            ms.Position = 0;
            using var indexed = new McapIndexedReader(ms, leaveOpen: true);
            var metadataIndex = indexed.MetadataIndexes.Single(x => x.Name == FoxRunSchemaMcapMetadata.MetadataName);
            var metadata = indexed.ReadMetadata(metadataIndex);

            Check(metadata.Name == FoxRunSchemaMcapMetadata.MetadataName
                  && metadata.Metadata.TryGetValue("value", out var value)
                  && value == json,
                "114-C1: MCAP metadata record roundtrips through summary metadata index");
        }

        private static void VerifyRecordingControllerWritesMetadata()
        {
            var path = TempMcapPath("recording");
            var logger = new CaptureLogger();
            FoxRunSchemaInfoRegistry.ClearForTests();
            FoxRunSchemaInfoRegistry.RegisterGenerated(FixtureRuntimeInfo());

            try
            {
                var parameters = new FoxgloveParameterStore();
                parameters.Register("/phase114/enabled", JToken.FromObject(true), "bool", writable: true);
                using var session = new FoxgloveSession(
                    "phase114-recording",
                    new FakeTransport(),
                    paramStore: parameters,
                    logger: logger);
                using var controller = new RecordingController(logger);
                controller.Enable(path);
                controller.AttachToSession(new PlaybackClock(new FixedClock(123)), parameters, session);
                controller.DetachFromSession();

                using var indexed = McapIndexedReader.OpenRead(path);
                var names = indexed.MetadataIndexes.Select(x => x.Name).ToList();
                Check(names.Contains("foxglove.parameters.snapshot")
                      && names.Contains(FoxRunSchemaMcapMetadata.MetadataName),
                    "114-D1: RecordingController writes parameter snapshot and FoxRun schema metadata");

                var metadataIndex = indexed.MetadataIndexes.Single(x => x.Name == FoxRunSchemaMcapMetadata.MetadataName);
                var metadata = indexed.ReadMetadata(metadataIndex);
                Check(metadata.Metadata.TryGetValue("value", out var value)
                      && value.Contains(ExpectedFixtureHash, StringComparison.Ordinal),
                    "114-D2: recorded FoxRun schema metadata includes current global manifest hash");
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                TryDelete(path);
            }
        }

        private static void VerifyReplayControllerGuardBehavior()
        {
            var current = FixtureRuntimeInfo();
            FoxRunSchemaMcapMetadata.TryCreateJson(current, out var matchingJson);
            FoxRunSchemaMcapMetadata.TryCreateJson(MismatchedRuntimeInfo(), out var mismatchedJson);

            var matchingPath = CreateTempMcapWithSchemaMetadata(matchingJson, "matching");
            var mismatchPath = CreateTempMcapWithSchemaMetadata(mismatchedJson, "mismatch");
            var missingRecordedPath = CreateTempMcapWithSchemaMetadata(null, "missing");
            try
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                FoxRunSchemaInfoRegistry.RegisterGenerated(current);

                var matchLogger = new CaptureLogger();
                using (var matchController = new ReplayController(matchLogger))
                {
                    matchController.Enable(matchingPath, new PlaybackClock(new FixedClock(0)), recordingEnabled: false);
                    Check(matchController.IsEnabled && string.IsNullOrEmpty(matchLogger.LastError),
                        "114-E1: ReplayController enables replay when recorded and current schema hashes match");
                }

                var mismatchLogger = new CaptureLogger();
                using (var mismatchController = new ReplayController(mismatchLogger))
                {
                    mismatchController.Enable(mismatchPath, new PlaybackClock(new FixedClock(0)), recordingEnabled: false);
                    Check(!mismatchController.IsEnabled
                          && mismatchController.LastEnableBlockedBySchemaMismatch
                          && mismatchController.LastEnableFailureMessage.Contains("FoxRun replay schema mismatch.", StringComparison.Ordinal)
                          && mismatchLogger.LastError.Contains("FoxRun replay schema mismatch.", StringComparison.Ordinal)
                          && mismatchLogger.LastError.Contains("Replay blocked.", StringComparison.Ordinal),
                        "114-E2: ReplayController blocks replay when schema hashes mismatch");
                }

                var missingLogger = new CaptureLogger();
                using (var missingController = new ReplayController(missingLogger))
                {
                    missingController.Enable(missingRecordedPath, new PlaybackClock(new FixedClock(0)), recordingEnabled: false);
                    Check(missingController.IsEnabled
                          && missingLogger.LastWarning.Contains("does not contain FoxRun schema metadata", StringComparison.Ordinal),
                        "114-E3: ReplayController allows missing recorded metadata with a warning");
                }

                using var engine = new McapReplayEngine();
                engine.Load(matchingPath);
                var metadata = engine.FindMetadata(FoxRunSchemaMcapMetadata.MetadataName);
                Check(metadata != null
                      && metadata.Metadata.TryGetValue("value", out var value)
                      && value == matchingJson,
                    "114-E4: McapReplayEngine exposes named metadata for pre-playback guards");
            }
            finally
            {
                FoxRunSchemaInfoRegistry.ClearForTests();
                TryDelete(matchingPath);
                TryDelete(mismatchPath);
                TryDelete(missingRecordedPath);
            }
        }

        private static void VerifySourceBoundariesAndWiring()
        {
            foreach (var path in new[]
            {
                MetadataRuntimePath,
                MetadataRuntimePath + ".meta",
                RecordingControllerPath,
                ReplayControllerPath,
                ReplayEnginePath,
                ManagerRuntimePath,
                ManagerServerPath,
                ManagerSetupPath
            })
            {
                Check(RepoFileExists(path), "114-F1: required source file exists: " + path);
            }

            var metadataSource = ReadRepoText(MetadataRuntimePath);
            foreach (var forbidden in new[] { "McapRecorder", "McapReplayEngine", "ReplayController", "RecordingController", "generatedAtUtc" })
            {
                Check(!metadataSource.Contains(forbidden, StringComparison.Ordinal),
                    "114-F2: metadata helper remains pure runtime schema logic, avoids: " + forbidden);
            }

            var recording = ReadRepoText(RecordingControllerPath);
            Check(recording.Contains("TryWriteFoxRunSchemaMetadata(recorder)", StringComparison.Ordinal)
                  && recording.Contains("FoxRunSchemaMcapMetadata.MetadataName", StringComparison.Ordinal)
                  && recording.Contains("foxglove.parameters.snapshot", StringComparison.Ordinal),
                "114-F3: recording path writes FoxRun schema metadata alongside existing parameter snapshot metadata");

            var replay = ReadRepoText(ReplayControllerPath);
            var loadIndex = replay.IndexOf("_replayEngine.Load(filePath)", StringComparison.Ordinal);
            var guardIndex = replay.IndexOf("ReplaySchemaGuard.Evaluate(_replayEngine)", StringComparison.Ordinal);
            var playIndex = replay.IndexOf("_replayEngine.Play()", StringComparison.Ordinal);
            Check(loadIndex >= 0 && guardIndex > loadIndex && playIndex > guardIndex,
                "114-F4: replay guard runs after Load and before Play");
            Check(replay.Contains("throw new InvalidDataException(schemaGuard.Message)", StringComparison.Ordinal),
                "114-F5: replay guard blocks mismatch by failing replay load");

            var validateStart = replay.IndexOf("private static void ValidateReplayFileForLoad", StringComparison.Ordinal);
            var validateEnd = replay.IndexOf("private static void ReadExactReplayMagic", StringComparison.Ordinal);
            var foundValidateBody = validateStart >= 0 && validateEnd > validateStart;
            var validateBody = foundValidateBody ? replay.Substring(validateStart, validateEnd - validateStart) : string.Empty;
            Check(foundValidateBody
                  && !validateBody.Contains("FoxRun", StringComparison.Ordinal)
                  && !validateBody.Contains(FoxRunSchemaMcapMetadata.MetadataName, StringComparison.Ordinal),
                "114-F6: file-shape validation remains free of FoxRun schema metadata policy");

            var engine = ReadRepoText(ReplayEnginePath);
            Check(engine.Contains("FindMetadata(string name)", StringComparison.Ordinal)
                  && engine.Contains("ReadMetadataAt(index.Offset)", StringComparison.Ordinal),
                "114-F7: replay engine can read named summary metadata records");

            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var validationRegistry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(project.Contains("Phase114Validation.cs", StringComparison.Ordinal),
                "114-F8: runtime validation project compiles Phase114 tests");
            Check(validationRegistry.Contains("--phase114", StringComparison.Ordinal)
                  && validationRegistry.Contains("Phase114Validation.Validate", StringComparison.Ordinal),
                "114-F9: validation registry dispatches --phase114 and full validation includes Phase114");

            var manager = ReadRepoText(ManagerRuntimePath);
            var server = ReadRepoText(ManagerServerPath);
            var setup = ReadRepoText(ManagerSetupPath);
            var validateIndex = server.IndexOf("ValidateTransportConfiguration()", StringComparison.Ordinal);
            var ensureIndex = server.IndexOf("EnsureRuntimeCreated()", StringComparison.Ordinal);
            var assetRootIndex = server.IndexOf("RegisterAssetRoots()", StringComparison.Ordinal);
            Check(manager.Contains("private void EnsureRuntimeCreated()", StringComparison.Ordinal)
                  && manager.Contains("new Core.FoxgloveRuntime(transport", StringComparison.Ordinal)
                  && ensureIndex > validateIndex
                  && assetRootIndex > ensureIndex,
                "114-F10: StartServer ensures runtime exists before registering asset roots or replay setup");
            Check(setup.Contains("private bool SetupReplay()", StringComparison.Ordinal)
                  && server.Contains("if (!SetupReplay())", StringComparison.Ordinal)
                  && server.Contains("return;", StringComparison.Ordinal)
                  && setup.Contains("ReplayStartBlockedBySchemaMismatch", StringComparison.Ordinal)
                  && !Regex.IsMatch(
                      setup,
                      @"ReplayStartBlockedBySchemaMismatch[\s\S]{0,700}RestoreLivePublishers\s*\(\s*\)\s*;[\s\S]{0,200}return\s*;",
                      RegexOptions.CultureInvariant),
                "114-F11: confirmed replay schema mismatch hard-blocks Manager startup without live fallback");
        }

        private static void VerifyDocs()
        {
            foreach (var path in new[]
            {
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/08_MCAP_Recording_and_Replay.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/zh/07_FoxRun自动发布.md",
                "docs/research-shared-emitter-architecture.md"
            })
            {
                var doc = ReadRepoText(path);
                Check(doc.Contains(FoxRunSchemaMcapMetadata.MetadataName, StringComparison.Ordinal)
                      && doc.Contains("globalManifestHash", StringComparison.Ordinal)
                      && doc.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
                      && doc.Contains("replay", StringComparison.OrdinalIgnoreCase),
                    "114-G1: docs describe MCAP FoxRun schema metadata and replay mismatch guard: " + path);
            }
        }

        private static string CreateTempMcapWithSchemaMetadata(string metadataJson, string label)
        {
            var path = TempMcapPath(label);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var recorder = new McapRecorder(fs))
            {
                recorder.AddChannel(1, "/phase114/" + label, "json", "", "", "");
                recorder.WriteMessage(1, 10, Encoding.UTF8.GetBytes("{}"));
                if (metadataJson != null)
                    recorder.WriteMetadata(FoxRunSchemaMcapMetadata.MetadataName, metadataJson);
                recorder.Close();
            }

            return path;
        }

        private static string TempMcapPath(string label)
            => Path.Combine(Path.GetTempPath(), "unity2foxglove-phase114-" + label + "-" + Guid.NewGuid().ToString("N") + ".mcap");

        private static FoxRunSchemaManifestInfo FixtureRuntimeInfo()
            => ToRuntimeInfo(FixtureManifest(), ExpectedFixtureHash);

        private static FoxRunSchemaManifestInfo MismatchedRuntimeInfo()
            => ToRuntimeInfo(FixtureManifest(), MismatchedHash);

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

        private static FoxRunSchemaManifestInfo ToRuntimeInfo(FoxRunCanonicalManifest manifest, string globalHash)
        {
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
                throw new FileNotFoundException("Missing required Phase114 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase114 validation.");
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
                // Best-effort cleanup for temp MCAP files used by validation tests.
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
