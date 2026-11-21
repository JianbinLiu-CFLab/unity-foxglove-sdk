// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Shared Windows symlink capability fixture for OpenH264 tests.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    /// <summary>Runs a test only when the Windows symlink fixture is available.</summary>
    public sealed class WindowsSymlinkFactAttribute : FactAttribute
    {
        /// <summary>Probe Windows directory-symlink capability during test discovery.</summary>
        public WindowsSymlinkFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "OpenH264 reparse-point behavior is a Windows-only fixture.";
                return;
            }

            var root = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-symlink-probe-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            try
            {
                if (!CreateDirectoryLink(link, target))
                    Skip = "Windows symlink creation is unavailable for this runner.";
            }
            finally
            {
                if (Directory.Exists(link))
                    Directory.Delete(link);
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        private static bool CreateDirectoryLink(string link, string target)
            => CreateSymbolicLink(link, target, 0x1 | 0x2);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(
            string symbolicFileName,
            string targetFileName,
            int flags);
    }
}
