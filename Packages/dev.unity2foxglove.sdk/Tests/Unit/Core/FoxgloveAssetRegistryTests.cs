// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.FoxgloveSDK.Core;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Core
{
    public sealed class FoxgloveAssetRegistryTests
    {
        [Fact]
        public void TryReadRejectsHardLinkInsideRegisteredRoot()
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "foxglove-asset-hardlink-" + Guid.NewGuid().ToString("N"));
            var allowed = Path.Combine(temporary, "allowed");
            var outside = Path.Combine(temporary, "outside");
            var source = Path.Combine(outside, "secret.bin");
            var link = Path.Combine(allowed, "leak.bin");
            Directory.CreateDirectory(allowed);
            Directory.CreateDirectory(outside);
            File.WriteAllText(source, "outside");
            try
            {
                Assert.True(TryCreateFileHardLink(link, source));
                var registry = new FoxgloveAssetRegistry();
                registry.RegisterRoot("asset://allowed/", allowed);

                Assert.False(
                    registry.TryRead(
                        "asset://allowed/leak.bin",
                        out var bytes,
                        out var error));
                Assert.Null(bytes);
                Assert.Contains("hard link", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (File.Exists(link))
                    File.Delete(link);
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, recursive: true);
            }
        }

        [Fact]
        public void TryReadAllowsTrustedLinkedRegisteredRoot()
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "foxglove-asset-root-link-" + Guid.NewGuid().ToString("N"));
            var actual = Path.Combine(temporary, "actual");
            var link = Path.Combine(temporary, "linked-root");
            Directory.CreateDirectory(actual);
            File.WriteAllText(Path.Combine(actual, "asset.bin"), "inside");
            try
            {
                Assert.True(TryCreateDirectoryLink(link, actual));
                var registry = new FoxgloveAssetRegistry();
                registry.RegisterRoot("asset://linked/", link);

                Assert.True(
                    registry.TryRead(
                        "asset://linked/asset.bin",
                        out var bytes,
                        out var error),
                    error);
                Assert.Equal("inside", System.Text.Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                if (Directory.Exists(link))
                    Directory.Delete(link);
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, recursive: true);
            }
        }

        [Fact]
        public void TryReadRejectsDirectoryLinkOutsideRegisteredRoot()
        {
            var temporary = Path.Combine(
                Path.GetTempPath(),
                "foxglove-asset-link-" + Guid.NewGuid().ToString("N"));
            var allowed = Path.Combine(temporary, "allowed");
            var outside = Path.Combine(temporary, "outside");
            var link = Path.Combine(allowed, "escape");
            Directory.CreateDirectory(allowed);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.bin"), "outside");
            try
            {
                Assert.True(TryCreateDirectoryLink(link, outside));
                var registry = new FoxgloveAssetRegistry();
                registry.RegisterRoot("asset://allowed/", allowed);

                Assert.False(
                    registry.TryRead(
                        "asset://allowed/escape/secret.bin",
                        out var bytes,
                        out var error));
                Assert.Null(bytes);
                Assert.Contains("reparse", error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(link))
                    Directory.Delete(link);
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, recursive: true);
            }
        }

        private static bool TryCreateDirectoryLink(string link, string target)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CreateSymbolicLink(link, target, 0x1 | 0x2);
            return Symlink(target, link) == 0;
        }

        private static bool TryCreateFileHardLink(string link, string target)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return CreateHardLink(link, target, IntPtr.Zero);
            return Link(target, link) == 0;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(
            string symbolicFileName,
            string targetFileName,
            int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(
            string fileName,
            string existingFileName,
            IntPtr securityAttributes);

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int Symlink(string target, string linkPath);

        [DllImport("libc", EntryPoint = "link", SetLastError = true)]
        private static extern int Link(string target, string linkPath);
    }
}
