// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared repository path helpers for runtime validation.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class RepositoryPathHelpers
    {
        public static string FindRequiredRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(directory.FullName, ".git"))
                    || File.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not find repository root for source validation from " + AppContext.BaseDirectory);
        }

        public static string RepoPath(string relativePath)
        {
            var root = FindRequiredRepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing repository file: " + relativePath, path);
            return path;
        }

        internal static bool IsPythonFacadePath(string normalized)
        {
            if (!normalized.StartsWith("Scripts/", StringComparison.Ordinal)
                || !normalized.EndsWith(".py", StringComparison.Ordinal))
                return false;

            var facadePath = RepoPath(normalized);
            var packagePath = Path.Combine(
                Path.GetDirectoryName(facadePath)
                    ?? throw new DirectoryNotFoundException(facadePath),
                Path.GetFileNameWithoutExtension(facadePath));
            return Directory.Exists(packagePath)
                || Directory.Exists(Path.Combine(
                    Path.GetDirectoryName(facadePath)
                        ?? throw new DirectoryNotFoundException(facadePath),
                    "Decomposed",
                    Path.GetFileNameWithoutExtension(facadePath)));
        }
    }
}
