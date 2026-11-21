// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Own temporary fixtures for the Windows handle-based OpenH264 safe-file tests.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed partial class OpenH264WindowsSafeFileTests
    {
        /// <summary>Creates an isolated local fixture below the repository build directory.</summary>
        private static Fixture CreateFixture(string name)
        {
            var root = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-safe-file-" + name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            return new Fixture(root, target);
        }

        /// <summary>Creates the directory symlink required by the reparse-point fixture.</summary>
        private static void CreateDirectoryLinkOrSkip(string link, string target)
        {
            Assert.True(
                CreateSymbolicLink(link, target, 0x1 | 0x2),
                "The Windows symlink fixture could not create a directory symlink.");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(
            string symbolicFileName,
            string targetFileName,
            int flags);

        /// <summary>Owns and removes a temporary safe-file fixture.</summary>
        private sealed class Fixture : IDisposable
        {
            internal Fixture(string root, string target)
            {
                Root = root;
                Target = target;
            }

            internal string Root { get; }
            internal string Target { get; }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
        }
    }
}
