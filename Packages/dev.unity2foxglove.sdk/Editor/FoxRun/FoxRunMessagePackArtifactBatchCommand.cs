// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: Controlled Unity batch entrypoint for Phase185 generated artifact evidence.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunMessagePackArtifactBatchCommand
    {
        private const string EvidenceFileName = "phase185-generator-evidence.json";

        public static void GenerateControlledArtifacts()
        {
            try
            {
                var evidenceRoot = ResolveEvidenceRoot();
                Directory.CreateDirectory(evidenceRoot);

                var generatedFiles = FoxrunCodeGenerator.GenerateSourceFiles(
                    out var manifest,
                    out var foxRunTypes);
                var verification = FoxrunCodeGenerator.VerifyGeneratedSchemaInfoFiles(manifest);
                var aggregate = Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts(manifest);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                var evidence = new JObject
                {
                    ["version"] = 1,
                    ["verdict"] = "PASS",
                    ["generator"] = "FoxrunCodeGenerator",
                    ["manifestVersion"] = manifest.ManifestVersion,
                    ["manifestHash"] = verification.ActualGlobalManifestHash,
                    ["sdkSchemaManifestHash"] = aggregate.SdkSchemaManifestHash,
                    ["generatedFileCount"] = generatedFiles.Count,
                    ["discoveredTypeCount"] = foxRunTypes.Count,
                    ["generatedFiles"] = new JArray(
                        generatedFiles
                            .OrderBy(value => value, StringComparer.Ordinal)
                            .Take(256))
                };
                var evidencePath = Path.Combine(evidenceRoot, EvidenceFileName);
                File.WriteAllText(
                    evidencePath,
                    evidence.ToString(Formatting.Indented) + Environment.NewLine);
                Debug.Log(
                    "PHASE185_BATCH_GENERATOR_PASS "
                    + "files=" + generatedFiles.Count
                    + " types=" + foxRunTypes.Count
                    + " evidence=" + evidencePath);
            }
            catch (Exception exception)
            {
                Debug.LogError("PHASE185_BATCH_GENERATOR_FAIL " + exception);
                throw;
            }
        }

        private static string ResolveEvidenceRoot()
        {
            var configured = Environment.GetEnvironmentVariable("PHASE185_EVIDENCE_ROOT");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("PHASE185_EVIDENCE_ROOT is required.");

            var repositoryRoot = FindRepositoryRoot();
            var fullPath = Path.GetFullPath(configured);
            var buildRoot = ComparablePath(
                    Path.Combine(repositoryRoot, "build"))
                + Path.DirectorySeparatorChar;
            var comparablePath = ComparablePath(fullPath);
            if (!comparablePath.StartsWith(
                    buildRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Phase185 evidence must remain below the repository build directory. "
                    + "buildRoot='" + buildRoot + "', "
                    + "evidenceRoot='" + comparablePath + "'.");
            }

            return fullPath;
        }

        private static string ComparablePath(string path)
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            const string extendedUncPrefix = @"\\?\UNC\";
            const string extendedPrefix = @"\\?\";
            if (fullPath.StartsWith(
                    extendedUncPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + fullPath.Substring(
                    extendedUncPrefix.Length);
            }

            return fullPath.StartsWith(
                    extendedPrefix,
                    StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(extendedPrefix.Length)
                : fullPath;
        }

        private static string FindRepositoryRoot()
        {
            var candidates = new[]
            {
                Environment.CurrentDirectory,
                Application.dataPath
            };
            foreach (var candidate in candidates)
            {
                var directory = new DirectoryInfo(
                    Path.GetFullPath(candidate));
                while (directory != null)
                {
                    var gitMarker = Path.Combine(
                        directory.FullName,
                        ".git");
                    if ((Directory.Exists(gitMarker)
                         || File.Exists(gitMarker))
                        && File.Exists(Path.Combine(
                            directory.FullName,
                            "Packages",
                            "dev.unity2foxglove.sdk",
                            "package.json")))
                    {
                        return directory.FullName;
                    }

                    directory = directory.Parent;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }
    }
}
#endif
