// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared repository root locator for source-shape validation checks.

using System;
using System.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>Locates the repository root for runtime validation checks.</summary>
    internal static class TestRepoRootLocator
    {
        private static readonly Lazy<string> RepoRoot = new Lazy<string>(FindRepoRootCore);

        public static string FindRepoRoot()
            => RepoRoot.Value;

        private static string FindRepoRootCore()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (LooksLikeRepoRoot(dir))
                    return dir;

                var parent = Directory.GetParent(dir);
                if (parent == null)
                    break;

                dir = parent.FullName;
            }

            return null;
        }

        private static bool LooksLikeRepoRoot(string dir)
            => File.Exists(Path.Combine(dir, "README.md"))
               && Directory.Exists(Path.Combine(dir, "Unity2Foxglove"))
               && File.Exists(Path.Combine(dir, "Packages", "dev.unity2foxglove.sdk", "package.json"));
    }
}
