// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SchemaEvidence
// Purpose: Resolves project-configured schema evidence output paths.

using System;
using System.IO;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Central Editor path resolver for current schema evidence artifacts.
    /// </summary>
    internal static class Unity2FoxgloveSchemaEvidencePaths
    {
        internal const string DefaultCurrentEvidenceRoot = "Assets/Generated";

        public static string CurrentEvidenceRootProjectRelative
        {
            get
            {
                if (TryNormalizeAssetsRoot(
                        Unity2FoxgloveSchemaEvidenceSettings.CurrentEvidenceRoot,
                        out var normalized,
                        out _))
                    return normalized;

                return DefaultCurrentEvidenceRoot;
            }
        }

        public static string ResolveCurrentEvidenceRoot()
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, CurrentEvidenceRootProjectRelative));
        }

        public static string ResolveFoxRunOutputDirectory()
        {
            return Path.Combine(ResolveCurrentEvidenceRoot(), "FoxRun");
        }

        public static string ResolveUnity2FoxgloveOutputDirectory()
        {
            return Path.Combine(ResolveCurrentEvidenceRoot(), "Unity2Foxglove");
        }

        public static bool TryNormalizeAssetsRoot(string path, out string normalized, out string error)
        {
            normalized = DefaultCurrentEvidenceRoot;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
                return true;

            var candidate = path.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(candidate))
            {
                var fullCandidate = Path.GetFullPath(candidate);
                var project = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
                if (!fullCandidate.StartsWith(project, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Schema evidence root must be inside this Unity project.";
                    return false;
                }

                candidate = fullCandidate.Substring(project.Length).Replace('\\', '/');
            }

            if (candidate.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Assets";
                return true;
            }

            if (!candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Schema evidence root must be an Assets-relative path, for example Assets/Generated.";
                return false;
            }

            normalized = "Assets" + candidate.Substring("Assets".Length).TrimEnd('/');
            return true;
        }

        private static string ProjectRoot
        {
            get
            {
                var assets = Application.dataPath;
                var parent = Directory.GetParent(assets);
                return parent == null ? Directory.GetCurrentDirectory() : parent.FullName;
            }
        }
    }
}
