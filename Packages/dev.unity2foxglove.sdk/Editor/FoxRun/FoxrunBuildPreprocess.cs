// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun
// Purpose: IPreprocessBuildWithReport hook - generates physical .g.cs
// fallback files for [FoxRun] annotated classes before IL2CPP Player build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Before Player build, generates real .g.cs files for [FoxRun] annotated classes
    /// so IL2CPP has the IFoxgloveLogSource implementation without relying on Roslyn analyzer.
    /// </summary>
    public class FoxrunBuildPreprocess : IPreprocessBuildWithReport
    {
        /// <summary>
        /// Runs early (before most other build callbacks) so generated source
        /// files are present for IL2CPP compilation.
        /// </summary>
        public int callbackOrder => -100;

        /// <summary>
        /// Generates physical <c>_FoxRun.g.cs</c> fallback files and a
        /// <c>FoxRun_link.xml</c> preservation file for IL2CPP builds,
        /// then forces a synchronous asset database refresh.
        ///
        /// If <c>[FoxRun]</c> types are detected but preservation cannot be
        /// guaranteed, the build is stopped with a clear error to prevent
        /// IL2CPP stripping from silently removing published topics.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[FoxrunBuildPreprocess] Generating FoxRun source files...");
            List<string> files;
            try
            {
                files = FoxrunCodeGenerator.GenerateSourceFiles();
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "[FoxRun] source generation failed.\n" +
                    "The build was stopped because physical FoxRun fallback source files\n" +
                    "could not be generated before the Player build.\n\n" +
                    "Details:\n" +
                    "  - Failed at: generate-source\n" +
                    $"  - Reason: {ex.GetType().Name}: {ex.Message}\n");
            }

            if (files.Count > 0)
            {
                var names = string.Join(", ", files);
                Debug.Log($"[FoxrunBuildPreprocess] Generated {files.Count} file(s): {names}");
            }

            try
            {
                var verification = FoxrunCodeGenerator.VerifyGeneratedSchemaInfoFiles();
                Debug.Log("[FoxrunBuildPreprocess] Verified FoxRun schema info after regeneration: " +
                          verification.ActualGlobalManifestHash);
                var aggregate = Unity2FoxgloveSchemaManifestGenerator.GenerateArtifacts();
                Debug.Log("[FoxrunBuildPreprocess] Generated SDK schema manifest aggregate: " +
                          aggregate.SdkSchemaManifestHash);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "[FoxRun] schema info or SDK schema manifest gate failed.\n" +
                    "The build was stopped after regeneration because the generated\n" +
                    "FoxRunSchemaInfo.g.cs file does not match the canonical manifest,\n" +
                    "or the SDK schema manifest aggregate could not be written.\n\n" +
                    "Details:\n" +
                    "  - Failed at: verify-schema-info-or-schema-manifest\n" +
                    $"  - Reason: {ex.GetType().Name}: {ex.Message}\n");
            }

            // Collect [FoxRun] types for IL2CPP preservation even when no file
            // changed on disk. Discovery happens in the Editor build step; the
            // generated Player code still publishes without runtime reflection.
            var linkPath = Path.Combine(Application.dataPath, "FoxRun_link.xml");
            EnsureFoxRunLinkXml(linkPath);
        }

        /// <summary>
        /// Scans for <c>[FoxRun]</c> types and generates (or deletes)
        /// <c>Assets/FoxRun_link.xml</c>.
        ///
        /// Three branches:
        /// <list type="bullet">
        ///   <item>0 types found - delete stale link.xml, log info, continue</item>
        ///   <item>N types found - generate link.xml, validate N entries, continue</item>
        ///   <item>Scan/validation failure - <c>BuildFailedException</c></item>
        /// </list>
        /// </summary>
        static void EnsureFoxRunLinkXml(string linkPath)
        {
            List<(string AsmName, string Ns, string ClassName)> types;
            try
            {
                types = FoxrunCodeGenerator.CollectFoxRunTypes();
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "[FoxRun] IL2CPP preservation failed.\n" +
                    "The build was stopped because [FoxRun] types were detected but\n" +
                    "Assets/FoxRun_link.xml could not be generated.\n" +
                    "This prevents a Player build where FoxRun topics silently\n" +
                    "disappear after IL2CPP stripping.\n\n" +
                    "Details:\n" +
                    "  - Failed at: scan\n" +
                    $"  - Reason: {ex.GetType().Name}: {ex.Message}\n");
            }

            if (types.Count == 0)
            {
                Debug.Log("[FoxrunBuildPreprocess] No [FoxRun] types found.");
                if (File.Exists(linkPath))
                {
                    File.Delete(linkPath);
                    Debug.Log("[FoxrunBuildPreprocess] Removed stale FoxRun_link.xml.");
                }
                return;
            }

            // Generate link.xml for the N detected types.
            string linkXml;
            try
            {
                linkXml = FoxrunCodeGenerator.EmitLinkXml(types);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "[FoxRun] IL2CPP preservation failed.\n" +
                    "The build was stopped because [FoxRun] types were detected but\n" +
                    "Assets/FoxRun_link.xml could not be generated.\n" +
                    "This prevents a Player build where FoxRun topics silently\n" +
                    "disappear after IL2CPP stripping.\n\n" +
                    "Details:\n" +
                    $"  - Detected types: {types.Count}\n" +
                    "  - Failed at: generate\n" +
                    $"  - Reason: {ex.GetType().Name}: {ex.Message}\n");
            }

            // Validate every detected type appears in the output.
            foreach (var (asm, ns, cn) in types)
            {
                var full = string.IsNullOrEmpty(ns) ? cn : $"{ns}.{cn}";
                if (!linkXml.Contains($"fullname=\"{full}\""))
                {
                    throw new BuildFailedException(
                        "[FoxRun] IL2CPP preservation failed.\n" +
                        "The build was stopped because [FoxRun] types were detected but\n" +
                        "Assets/FoxRun_link.xml validation failed (missing type entry).\n" +
                        "This prevents a Player build where FoxRun topics silently\n" +
                        "disappear after IL2CPP stripping.\n\n" +
                        "Details:\n" +
                        $"  - Detected types: {types.Count}\n" +
                        "  - Failed at: validate\n" +
                        $"  - Missing type: {full}\n");
                }
            }

            try
            {
                File.WriteAllText(linkPath, linkXml);
            }
            catch (Exception ex)
            {
                throw new BuildFailedException(
                    "[FoxRun] IL2CPP preservation failed.\n" +
                    "The build was stopped because [FoxRun] types were detected but\n" +
                    "Assets/FoxRun_link.xml could not be written to disk.\n" +
                    "This prevents a Player build where FoxRun topics silently\n" +
                    "disappear after IL2CPP stripping.\n\n" +
                    "Details:\n" +
                    $"  - Detected types: {types.Count}\n" +
                    "  - Failed at: write\n" +
                    $"  - Reason: {ex.GetType().Name}: {ex.Message}\n");
            }

            Debug.Log($"[FoxrunBuildPreprocess] Wrote FoxRun_link.xml with {types.Count} type(s)");
        }
    }
}
