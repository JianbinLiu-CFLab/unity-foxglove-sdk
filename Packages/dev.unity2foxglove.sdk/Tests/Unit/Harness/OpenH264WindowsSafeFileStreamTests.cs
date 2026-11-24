// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Exercise stream and replacement behavior in the Windows safe-file layer.

using System;
using System.IO;
using System.Threading;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class OpenH264WindowsSafeFileStreamTests
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
                    Assert.True(root.TryCreateProtectedFile("stream.bin", out var file, out var fileReason), fileReason);
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
