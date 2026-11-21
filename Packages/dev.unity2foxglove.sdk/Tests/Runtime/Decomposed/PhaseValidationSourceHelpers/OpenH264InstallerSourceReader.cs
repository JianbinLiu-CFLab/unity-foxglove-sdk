// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Compose the OpenH264 installer source surface.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Composes the registered OpenH264 installer partial sources.</summary>
    internal static class OpenH264InstallerSourceReader
    {
        /// <summary>Reads the OpenH264 installer facade and registered partial sources.</summary>
        public static string Read(string root)
        {
            var relativePaths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Download.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Helper.cs",
            };
            var source = new StringBuilder();
            foreach (var relativePath in relativePaths)
            {
                var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing OpenH264 installer source: " + path, path);
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(path));
            }

            return source.ToString();
        }
    }
}
