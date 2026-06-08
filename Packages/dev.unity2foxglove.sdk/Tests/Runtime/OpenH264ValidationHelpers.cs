// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared OpenH264 validation helpers.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class OpenH264ValidationHelpers
    {
        public static bool HasCommittedOpenH264BinaryArtifacts()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var roots = new[]
            {
                Path.Combine(root, "Packages"),
                Path.Combine(root, "Unity2Foxglove", "Assets")
            };
            var extensions = new[] { ".dll", ".exe", ".lib", ".so", ".dylib" };
            foreach (var searchRoot in roots.Where(Directory.Exists))
            {
                foreach (var file in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(file);
                    if (name.IndexOf("openh264", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
    }
}
