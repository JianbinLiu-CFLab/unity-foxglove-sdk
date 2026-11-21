// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Exercise stream and replacement behavior in the Windows safe-file layer.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed partial class OpenH264WindowsSafeFileTests
    {
        [WindowsSafeFileFact]
        /// <summary>Copies a bounded stream into and back out of a safe file handle.</summary>
        public void SafeFileStreamTransferUsesOwnedHandle()
        {
            var fixture = CreateFixture("stream-transfer");
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
                    Assert.True(root.TryCreateFile("stream.bin", out var file, out var fileReason), fileReason);
                    using (file)
                    using (var source = new MemoryStream(new byte[] { 4, 5, 6 }))
                    {
                        Assert.True(file.WriteFromStream(source, 16, out var written, out var writeReason), writeReason);
                        Assert.Equal(3, written);
                        Assert.True(file.ReadAllBytes(16, out var bytes, out var readReason), readReason);
                        Assert.Equal(new byte[] { 4, 5, 6 }, bytes);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Blocks root and version-directory replacement while safe handles are held.</summary>
        public void SafeDirectoryHandlesBlockReplacement()
        {
            var fixture = CreateFixture("safe-directory-lock");
            var versionPath = Path.Combine(fixture.Root, "version");
            var movedVersionPath = versionPath + ".moved";
            var movedRootPath = fixture.Root + ".moved";
            Directory.CreateDirectory(versionPath);
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
                    Assert.True(
                        root.TryOpenLockedDirectory("version", out var version, out var versionReason),
                        versionReason);
                    using (version)
                    {
                        var versionBlocked = false;
                        try
                        {
                            Directory.Move(versionPath, movedVersionPath);
                        }
                        catch (IOException)
                        {
                            versionBlocked = true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            versionBlocked = true;
                        }

                        Assert.True(versionBlocked);

                        var rootBlocked = false;
                        try
                        {
                            Directory.Move(fixture.Root, movedRootPath);
                        }
                        catch (IOException)
                        {
                            rootBlocked = true;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            rootBlocked = true;
                        }

                        Assert.True(rootBlocked);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(movedVersionPath))
                    Directory.Delete(movedVersionPath, recursive: true);
                if (Directory.Exists(movedRootPath))
                    Directory.Delete(movedRootPath, recursive: true);
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Stops safe stream transfer when its cancellation token is already cancelled.</summary>
        public void SafeFileStreamTransferHonorsCancellation()
        {
            var fixture = CreateFixture("stream-cancel");
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
                    Assert.True(root.TryCreateFile("cancel.bin", out var file, out var fileReason), fileReason);
                    using (file)
                    using (var source = new MemoryStream(new byte[] { 1, 2, 3 }))
                    using (var cancellation = new CancellationTokenSource())
                    {
                        cancellation.Cancel();
                        Assert.False(
                            file.WriteFromStream(
                                source,
                                16,
                                cancellation.Token,
                                out _,
                                out var reason));
                        Assert.Contains("cancel", reason, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        /// <summary>Rejects a reparse-point root before opening it.</summary>
        public void RejectsAReparsePointRootBeforeOpeningIt()
        {
            var fixture = CreateFixture("root-reparse");
            var link = Path.Combine(fixture.Root, "link");
            try
            {
                CreateDirectoryLinkOrSkip(link, fixture.Target);
                Assert.False(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(link, out _, out var reason));
                Assert.Contains("reparse", reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        /// <summary>Rejects an install root reached through a reparse-point ancestor.</summary>
        public void RejectsAReparsePointAncestorBeforeOpeningIt()
        {
            var basePath = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-safe-file-ancestor-" + Guid.NewGuid().ToString("N"));
            var actualParent = Path.Combine(basePath, "actual");
            var actualRoot = Path.Combine(actualParent, "root");
            var aliasParent = Path.Combine(basePath, "alias");
            Directory.CreateDirectory(actualRoot);
            try
            {
                CreateDirectoryLinkOrSkip(aliasParent, actualParent);
                Assert.False(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        Path.Combine(aliasParent, "root"),
                        out _,
                        out var reason));
                Assert.Contains("different path", reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(aliasParent))
                    Directory.Delete(aliasParent);
                if (Directory.Exists(basePath))
                    Directory.Delete(basePath, recursive: true);
            }
        }

        [WindowsSymlinkFact]
        /// <summary>Continues using the opened child after its path is replaced by a symlink.</summary>
        public void RelativeHandleSurvivesChildPathReplacement()
        {
            var fixture = CreateFixture("child-replacement");
            var childPath = Path.Combine(fixture.Root, "safe");
            var movedPath = Path.Combine(fixture.Root, "moved");
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
                    Assert.True(root.TryCreateDirectory("safe", out var child, out var childReason), childReason);
                    root.Dispose();
                    using (child)
                    {
                        Directory.Move(childPath, movedPath);
                        CreateDirectoryLinkOrSkip(childPath, fixture.Target);
                        Assert.True(child.TryCreateFile("probe.txt", out var file, out var fileReason), fileReason);
                        using (file)
                        {
                            Assert.True(file.WriteAllBytes(new byte[] { 4 }, out var writeReason), writeReason);
                        }
                    }
                }

                Assert.True(File.Exists(Path.Combine(movedPath, "probe.txt")));
                Assert.False(File.Exists(Path.Combine(fixture.Target, "probe.txt")));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        /// <summary>Rejects a symlink child without opening or mutating its target.</summary>
        public void RejectsSymlinkChildWithoutTouchingTarget()
        {
            var fixture = CreateFixture("child-reparse");
            var link = Path.Combine(fixture.Root, "link");
            try
            {
                CreateDirectoryLinkOrSkip(link, fixture.Target);
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(
                        fixture.Root,
                        out var root,
                        out var rootReason),
                    rootReason);
                using (root)
                {
                    Assert.False(root.TryOpenDirectory("link", out _, out var reason));
                    Assert.Contains("reparse", reason, StringComparison.OrdinalIgnoreCase);
                }

                Assert.True(Directory.Exists(fixture.Target));
                Assert.False(File.Exists(Path.Combine(fixture.Target, "probe.txt")));
            }
            finally
            {
                fixture.Dispose();
            }
        }

    }
}
