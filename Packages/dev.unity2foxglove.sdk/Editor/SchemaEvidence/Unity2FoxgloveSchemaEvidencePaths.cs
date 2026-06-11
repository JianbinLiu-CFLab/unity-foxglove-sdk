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
        private static readonly string CachedProjectRoot = ResolveProjectRoot();

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
            var isRooted = Path.IsPathRooted(candidate);
            if (!isRooted
                && !candidate.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                && !candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Schema evidence root must be an Assets-relative path, for example Assets/Generated.";
                return false;
            }

            var fullCandidate = isRooted
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(ProjectRoot, candidate));
            var assetsRoot = ProjectAssetsRoot;
            if (!IsSameOrChildPath(fullCandidate, assetsRoot))
            {
                error = "Schema evidence root must stay inside Assets.";
                return false;
            }

            if (PathsEqual(fullCandidate, assetsRoot))
            {
                normalized = "Assets";
                return true;
            }

            var assetsRootPrefix = assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                   + Path.DirectorySeparatorChar;
            normalized = "Assets/" + fullCandidate.Substring(assetsRootPrefix.Length)
                .Replace('\\', '/')
                .TrimEnd('/');
            return true;
        }

        private static string ProjectAssetsRoot
            => Path.GetFullPath(Path.Combine(ProjectRoot, "Assets"));

        private static bool IsSameOrChildPath(string candidate, string parent)
        {
            var normalizedCandidate = NormalizeFullPath(candidate);
            var normalizedParent = NormalizeFullPath(parent);
            return normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
                   || normalizedCandidate.StartsWith(
                       normalizedParent + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string left, string right)
        {
            return NormalizeFullPath(left).Equals(NormalizeFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFullPath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ProjectRoot => CachedProjectRoot;

        private static string ResolveProjectRoot()
        {
            var assets = Application.dataPath;
            var parent = Directory.GetParent(assets);
            return parent == null ? Directory.GetCurrentDirectory() : parent.FullName;
        }
    }
}
