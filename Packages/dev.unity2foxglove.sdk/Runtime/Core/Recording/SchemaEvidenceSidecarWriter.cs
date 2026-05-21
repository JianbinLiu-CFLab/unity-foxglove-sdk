// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Recording
// Purpose: Writes schema evidence sidecars beside MCAP recordings.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Result returned after attempting to write a schema evidence sidecar.
    /// </summary>
    public sealed class SchemaEvidenceSidecarResult
    {
        public SchemaEvidenceSidecarResult(
            bool success,
            bool complete,
            string sidecarDirectory,
            IReadOnlyList<string> warnings)
        {
            Success = success;
            Complete = complete;
            SidecarDirectory = sidecarDirectory ?? string.Empty;
            Warnings = warnings ?? Array.Empty<string>();
        }

        /// <summary>True when the sidecar operation should allow the caller to continue.</summary>
        public bool Success { get; }

        /// <summary>True when every expected evidence artifact was present and copied.</summary>
        public bool Complete { get; }

        /// <summary>Directory written beside the MCAP recording.</summary>
        public string SidecarDirectory { get; }

        /// <summary>Warnings describing missing or partially copied evidence.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>
    /// Copies current schema evidence beside an MCAP file and writes a compact
    /// index that ties the evidence snapshot to that recording.
    /// </summary>
    public static class SchemaEvidenceSidecarWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly string[] FoxRunFiles =
        {
            "foxrun.manifest.json",
            "foxrun.manifest.hash",
            "foxrun.manifest.report.json",
            "FoxRunSchemaInfo.g.cs"
        };

        private static readonly string[] Unity2FoxgloveFiles =
        {
            "unity2foxglove.schema-manifest.json",
            "unity2foxglove.schema-manifest.hash",
            "unity2foxglove.schema-manifest.report.json"
        };

        /// <summary>
        /// Writes a <c>.schema</c> directory beside the supplied MCAP path.
        /// </summary>
        /// <param name="mcapPath">Target MCAP recording path.</param>
        /// <param name="currentEvidenceRoot">Current evidence root, normally <c>Assets/Generated</c>.</param>
        /// <param name="identityMode">Identity mode active for the recording.</param>
        /// <param name="requireComplete">When true, missing evidence makes the result unsuccessful.</param>
        /// <returns>Sidecar write result with completeness and warning details.</returns>
        public static SchemaEvidenceSidecarResult WriteSidecar(
            string mcapPath,
            string currentEvidenceRoot,
            SchemaIdentityMode identityMode,
            bool requireComplete)
        {
            var warnings = new List<string>();
            var sidecarDirectory = string.IsNullOrWhiteSpace(mcapPath)
                ? string.Empty
                : Path.ChangeExtension(Path.GetFullPath(mcapPath), ".schema");

            try
            {
                if (string.IsNullOrWhiteSpace(mcapPath))
                {
                    warnings.Add("MCAP path is empty; schema evidence sidecar was not written.");
                    return new SchemaEvidenceSidecarResult(false, false, sidecarDirectory, warnings);
                }

                if (string.IsNullOrWhiteSpace(currentEvidenceRoot))
                    warnings.Add("Current schema evidence root is empty.");

                if (Directory.Exists(sidecarDirectory))
                    Directory.Delete(sidecarDirectory, recursive: true);
                Directory.CreateDirectory(sidecarDirectory);

                var fullEvidenceRoot = string.IsNullOrWhiteSpace(currentEvidenceRoot)
                    ? string.Empty
                    : Path.GetFullPath(currentEvidenceRoot);

                CopyGroup(fullEvidenceRoot, sidecarDirectory, "FoxRun", FoxRunFiles, warnings);
                CopyGroup(fullEvidenceRoot, sidecarDirectory, "Unity2Foxglove", Unity2FoxgloveFiles, warnings);

                var complete = warnings.Count == 0;
                WriteIndex(
                    sidecarDirectory,
                    mcapPath,
                    identityMode,
                    complete,
                    ReadHash(Path.Combine(fullEvidenceRoot, "FoxRun", "foxrun.manifest.hash")),
                    ReadHash(Path.Combine(fullEvidenceRoot, "Unity2Foxglove", "unity2foxglove.schema-manifest.hash")),
                    warnings);

                return new SchemaEvidenceSidecarResult(!requireComplete || complete, complete, sidecarDirectory, warnings);
            }
            catch (Exception ex)
            {
                warnings.Add("Failed to write schema evidence sidecar: " + ex.Message);
                return new SchemaEvidenceSidecarResult(false, false, sidecarDirectory, warnings);
            }
        }

        private static void CopyGroup(
            string sourceRoot,
            string sidecarRoot,
            string groupName,
            IReadOnlyList<string> files,
            List<string> warnings)
        {
            var sourceDirectory = Path.Combine(sourceRoot ?? string.Empty, groupName);
            var targetDirectory = Path.Combine(sidecarRoot, groupName);
            Directory.CreateDirectory(targetDirectory);

            foreach (var fileName in files)
            {
                var sourcePath = Path.Combine(sourceDirectory, fileName);
                if (!File.Exists(sourcePath))
                {
                    warnings.Add("Missing schema evidence file: " + Path.Combine(groupName, fileName));
                    continue;
                }

                File.Copy(sourcePath, Path.Combine(targetDirectory, fileName), overwrite: true);
            }
        }

        private static string ReadHash(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            return File.ReadAllText(path, Utf8NoBom).Trim();
        }

        private static void WriteIndex(
            string sidecarDirectory,
            string mcapPath,
            SchemaIdentityMode identityMode,
            bool complete,
            string foxRunHash,
            string sdkSchemaHash,
            IReadOnlyList<string> warnings)
        {
            var json = new JObject
            {
                ["version"] = 1,
                ["mcapFile"] = Path.GetFileName(mcapPath),
                ["identityMode"] = identityMode.ToString(),
                ["complete"] = complete,
                ["foxRun"] = new JObject
                {
                    ["globalManifestHash"] = foxRunHash ?? string.Empty,
                    ["directory"] = "FoxRun"
                },
                ["unity2Foxglove"] = new JObject
                {
                    ["sdkSchemaManifestHash"] = sdkSchemaHash ?? string.Empty,
                    ["directory"] = "Unity2Foxglove"
                },
                ["warnings"] = new JArray(warnings ?? Array.Empty<string>())
            };

            File.WriteAllText(
                Path.Combine(sidecarDirectory, "schema-evidence.json"),
                JsonConvert.SerializeObject(json, Formatting.None),
                Utf8NoBom);
        }
    }
}
