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
    public sealed class OpenH264WindowsSafeFileTests
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
