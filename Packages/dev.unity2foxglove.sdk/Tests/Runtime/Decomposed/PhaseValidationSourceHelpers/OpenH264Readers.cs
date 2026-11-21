// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Read the OpenH264 installer facade and registered partial sources.

using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class PhaseValidationSourceHelpers
    {
        public static string ReadOpenH264InstallerSources(string root)
        {
            var relativePaths = new[]
            {
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Download.cs",
                "Packages/dev.unity2foxglove.sdk/Editor/Publishers/OpenH264OfficialBinaryInstaller.Helper.cs",
            };
            var paths = relativePaths
                .Select(relativePath => Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .ToArray();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("Missing OpenH264 installer source: " + path, path);
            }

            var source = new StringBuilder();
            foreach (var path in paths)
            {
                if (source.Length > 0)
                    source.Append(Environment.NewLine);
                source.Append(File.ReadAllText(path));
            }

            return source.ToString();
        }
    }
}
