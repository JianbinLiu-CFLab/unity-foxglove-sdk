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
    /// <summary>
    /// Validation type for Phase115BValidation.
    /// </summary>
    public static partial class Phase115BValidation
    {
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
                    0.001f)
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
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase115B file: " + relativePath, path);
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
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
