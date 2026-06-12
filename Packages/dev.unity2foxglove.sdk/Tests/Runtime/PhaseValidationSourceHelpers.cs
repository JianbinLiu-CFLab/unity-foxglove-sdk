// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationSourceHelpers
    {
        public static string ReadCameraPublisherSources()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var dir = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Schemas",
                "Proto",
                "Publishers");
            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException("Camera publisher directory was not found.");

            var files = Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static bool SourceMethodContains(string source, string methodName, string needle)
            => SourceMethod(source, methodName).Contains(needle);

        public static string SourceMethod(string source, string methodName)
        {
            var start = source.IndexOf(methodName, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var braceStart = source.IndexOf('{', start);
            if (braceStart < 0)
                return string.Empty;

            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            return source.Substring(start);
        }
    }
}
