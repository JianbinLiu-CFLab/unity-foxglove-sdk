// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Compose the OpenH264 installer source surface.

using System;
using System.IO;
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
        {
            var root = FindRepositoryRoot();
            var builder = new StringBuilder();
            foreach (var relativePath in RelativePaths)
            {
                var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing OpenH264 installer source: " + path, path);
                if (builder.Length > 0)
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
