// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Exercise the Windows handle-based OpenH264 safe-file layer.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    /// <summary>Runs a test only when the Windows handle fixture is available.</summary>
    public sealed class WindowsSafeFileFactAttribute : FactAttribute
    {
        /// <summary>Skip the fixture on non-Windows hosts.</summary>
        public WindowsSafeFileFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Skip = "OpenH264 safe-file behavior is a Windows-only fixture.";
        }
    }

    /// <summary>Exercises the Windows handle-based OpenH264 safe-file layer.</summary>
    public sealed partial class OpenH264WindowsSafeFileTests
    {
        [WindowsSafeFileFact]
        /// <summary>Creates, writes, renames, and deletes a file through relative handles.</summary>
        public void OpensExistingRootAndUsesRelativeHandlesForFiles()
        {
            var fixture = CreateFixture("relative");
            try
            {
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        fixture.Root,
                        out var root,
                        out var rootReason),
                    rootReason);
                using (root)
                {
                    Assert.True(root.TryCreateDirectory("child", out var child, out var childReason), childReason);
                    using (child)
                    {
                        Assert.True(child.TryCreateFile("probe.txt", out var file, out var fileReason), "child file: " + fileReason);
                        using (file)
                        {
                            Assert.NotEqual(default, file.Identity);
                            Assert.True(
                                file.WriteAllBytes(new byte[] { 1, 2, 3 }, out var writeReason),
                                writeReason);
                        }
                    }

                    Assert.True(root.TryOpenRenameDirectory("child", out var renameDirectory, out var renameReason), renameReason);
                    using (renameDirectory)
                    {
                        Assert.True(root.TryOpenFile("child", "probe.txt", out var source, out var sourceReason), sourceReason);
                        using (source)
                        {
                            Assert.True(source.RenameTo(renameDirectory, "renamed.txt", out var renameError), renameError);
                            Assert.True(source.Delete(out var deleteError), deleteError);
                        }
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Rejects network roots outside the local secure-transaction contract.</summary>
        public void RejectsNetworkStyleRootsForSecureTransactions()
        {
            Assert.False(
                OpenH264WindowsSafeDirectory.TryOpenExisting(
                    @"\\server\share\OpenH264",
                    out _,
                    out var reason));
            Assert.Contains("unsupported", reason, StringComparison.OrdinalIgnoreCase);

            Assert.False(
                OpenH264WindowsSafeDirectory.TryOpenExisting(
                    @"\\?\UNC\server\share\OpenH264",
                    out _,
                    out var extendedUncReason));
            Assert.Contains("unsupported", extendedUncReason, StringComparison.OrdinalIgnoreCase);

            Assert.False(
                OpenH264WindowsSafeDirectory.TryOpenExisting(
                    @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1",
                    out _,
                    out var deviceReason));
            Assert.Contains("unsupported", deviceReason, StringComparison.OrdinalIgnoreCase);
        }

        [WindowsSafeFileFact]
        /// <summary>Accepts a local extended-length root while keeping UNC roots rejected.</summary>
        public void AcceptsLocalExtendedLengthRoot()
        {
            var fixture = CreateFixture("extended-root");
            try
            {
                var extendedRoot = @"\\?\" + fixture.Root;
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        extendedRoot,
                        out var root,
                        out var reason),
                    reason);
                root.Dispose();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Rejects a secure transaction root that does not already exist.</summary>
        public void RejectsMissingRootBeforeSecureTransaction()
        {
            var missing = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-safe-file-missing-" + Guid.NewGuid().ToString("N"));
            Assert.False(
                OpenH264WindowsSafeDirectory.TryOpenExisting(
                    missing,
                    out _,
                    out var reason));
            Assert.Contains("Native open failed", reason, StringComparison.OrdinalIgnoreCase);
        }

        [WindowsSafeFileFact]
        /// <summary>Rejects names that could escape a parent directory handle.</summary>
        public void RejectsPathEscapeNames()
        {
            var fixture = CreateFixture("escape");
            try
            {
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        fixture.Root,
                        out var root,
                        out var rootReason),
                    rootReason);
                using (root)
                {
                    Assert.False(root.TryCreateDirectory("..", out _, out var directoryReason));
                    Assert.Contains("single relative", directoryReason, StringComparison.OrdinalIgnoreCase);
                    Assert.False(root.TryCreateFile(@"..\escape.txt", out _, out var fileReason));
                    Assert.Contains("single relative", fileReason, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Truncates an existing file before writing a shorter payload.</summary>
        public void WriteAllBytesReplacesExistingFileContents()
        {
            var fixture = CreateFixture("truncate");
            var filePath = Path.Combine(fixture.Root, "child", "probe.txt");
            try
            {
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        fixture.Root,
                        out var root,
                        out var rootReason),
                    rootReason);
                using (root)
                {
                    Assert.True(root.TryCreateDirectory("child", out var child, out var childReason), childReason);
                    using (child)
                    {
                        Assert.True(child.TryCreateFile("probe.txt", out var created, out var createReason), createReason);
                        using (created)
                        {
                            Assert.True(created.WriteAllBytes(new byte[] { 1, 2, 3, 4 }, out var writeReason), writeReason);
                        }
                    }

                    Assert.True(root.TryOpenFile("child", "probe.txt", out var existing, out var openReason), openReason);
                    using (existing)
                    {
                        Assert.True(existing.WriteAllBytes(new byte[] { 9 }, out var rewriteReason), rewriteReason);
                    }
                }

                Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(filePath));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Replaces an existing destination during a relative publish rename.</summary>
        public void RenameToReplacesExistingDestination()
        {
            var fixture = CreateFixture("rename-replace");
            var targetPath = Path.Combine(fixture.Root, "child", "target.txt");
            try
            {
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        fixture.Root,
                        out var root,
                        out var rootReason),
                    rootReason);
                using (root)
                {
                    Assert.True(root.TryCreateDirectory("child", out var child, out var childReason), childReason);
                    using (child)
                    {
                        Assert.True(child.TryCreateFile("source.txt", out var sourceFile, out var sourceReason), sourceReason);
                        using (sourceFile)
                        {
                            Assert.True(sourceFile.WriteAllBytes(new byte[] { 7 }, out var sourceWriteReason), sourceWriteReason);
                        }

                        Assert.True(child.TryCreateFile("target.txt", out var targetFile, out var targetReason), targetReason);
                        using (targetFile)
                        {
                            Assert.True(targetFile.WriteAllBytes(new byte[] { 2 }, out var targetWriteReason), targetWriteReason);
                        }

                        child.Dispose();
                        Assert.True(root.TryOpenRenameDirectory("child", out var renameDirectory, out var renameReason), renameReason);
                        using (renameDirectory)
                        {
                            Assert.True(root.TryOpenFile("child", "source.txt", out var source, out var openReason), openReason);
                            using (source)
                            {
                                Assert.True(source.RenameTo(renameDirectory, "target.txt", out var renameError), renameError);
                            }
                        }
                    }
                }

                Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(targetPath));
            }
            finally
            {
                fixture.Dispose();
            }
        }

    }
}
