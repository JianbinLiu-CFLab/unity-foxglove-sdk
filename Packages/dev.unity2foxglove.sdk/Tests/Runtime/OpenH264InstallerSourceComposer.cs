// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Compose the OpenH264 installer source surface.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class OpenH264InstallerSourceComposer
    {
        private static readonly string[] RelativePaths =
        {
            "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs",
            "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Download.cs",
            "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Helper.cs",
        };

        /// <summary>Reads the installer facade and its registered partial sources.</summary>
        public static string Compose()
            => Compose(FindRepositoryRoot());

        /// <summary>Reads the installer source surface relative to the supplied repository root.</summary>
        public static string Compose(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
                throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));

            var root = Path.GetFullPath(repositoryRoot);
            var facadePath = Path.Combine(root, RelativePaths[0].Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(facadePath))
                throw new FileNotFoundException("Missing OpenH264 installer source: " + facadePath, facadePath);

            var facadeDirectory = Path.GetDirectoryName(facadePath);
            if (string.IsNullOrEmpty(facadeDirectory))
                throw new InvalidDataException("OpenH264 installer source directory is missing.");

            var fragmentPaths = RelativePaths
                .Skip(1)
                .Select(relativePath => Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();

            var expectedFragments = RelativePaths
                .Skip(1)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.Ordinal);
            var actualFragments = Directory.GetFiles(
                    facadeDirectory,
                    "OpenH264OfficialBinaryInstaller.*.cs",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(facadeDirectory, path).Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            if (actualFragments.Count == 0)
                return File.ReadAllText(facadePath);
            if (!expectedFragments.SetEquals(actualFragments))
                throw new InvalidDataException(
                    "OpenH264 installer source set mismatch. Expected: "
                    + string.Join(", ", expectedFragments.OrderBy(name => name, StringComparer.Ordinal))
                    + "; actual: "
                    + string.Join(", ", actualFragments.OrderBy(name => name, StringComparer.Ordinal)));

            var builder = new StringBuilder(File.ReadAllText(facadePath));
            foreach (var path in fragmentPaths)
            {
                builder.Append(Environment.NewLine);
                builder.Append(File.ReadAllText(path));
            }

            return builder.ToString();
        }

        /// <summary>Finds the repository root from the runtime test assembly location.</summary>
        private static string FindRepositoryRoot()
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
                "Could not find repository root for OpenH264 installer source validation.");
        }
    }
}
