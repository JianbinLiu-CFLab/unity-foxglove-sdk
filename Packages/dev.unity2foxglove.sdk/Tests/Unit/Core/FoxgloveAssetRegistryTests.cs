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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(
            string symbolicFileName,
            string targetFileName,
            int flags);

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int Symlink(string target, string linkPath);
    }
}
