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
    /// <summary>
    /// Validation type for Phase114Validation.
    /// </summary>
    public static partial class Phase114Validation
    {
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
            => ToRuntimeInfo(FixtureManifest(), ExpectedGlobalFixtureHash);

        private static FoxRunSchemaManifestInfo MismatchedRuntimeInfo()
            => ToRuntimeInfo(FixtureManifest(), MismatchedHash);

        private static FoxRunCanonicalManifest CreateFixtureManifest()
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
                    0.001f)
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
                                contract.Policy.Hz,
                                contract.Policy.Tolerance,
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
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase114 validation.");
            _repoRoot = root;
            return _repoRoot;
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

            /// <summary>
            /// Validation method for LogWarning.
            /// </summary>
            /// <param name="message">Diagnostic message recorded by the test double.</param>
            public void LogWarning(string message)
            {
                LastWarning = message ?? string.Empty;
            }

            /// <summary>
            /// Validation method for LogError.
            /// </summary>
            /// <param name="message">Diagnostic message recorded by the test double.</param>
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
