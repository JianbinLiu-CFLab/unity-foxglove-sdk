// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Exercise the non-replacing rename used by OpenH264 pair publication.

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    /// <summary>Exercises the non-replacing rename used by OpenH264 pair publication.</summary>
    public sealed class OpenH264WindowsSafeFileNoReplaceTests
    {
        [WindowsSafeFileFact]
        /// <summary>Refuses to overwrite an existing destination and leaves both files unchanged.</summary>
        public void RenameToWithoutReplaceRejectsExistingDestination()
        {
            var root = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-safe-file-rename-no-replace-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sourcePath = Path.Combine(root, "child", "source.txt");
            var targetPath = Path.Combine(root, "child", "target.txt");
            try
            {
                Assert.True(
                    OpenH264WindowsSafeDirectory.TryOpenExisting(root, out var safeRoot, out var rootReason),
                    rootReason);
                using (safeRoot)
                {
                    Assert.True(safeRoot.TryCreateDirectory("child", out var child, out var childReason), childReason);
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
                    }

                    Assert.True(safeRoot.TryOpenRenameDirectory("child", out var renameDirectory, out var renameReason), renameReason);
                    using (renameDirectory)
                    {
                        Assert.True(safeRoot.TryOpenFile("child", "source.txt", out var source, out var openReason), openReason);
                        using (source)
                        {
                            Assert.False(source.RenameTo(renameDirectory, "target.txt", replaceExisting: false, out var renameError));
                            Assert.False(string.IsNullOrEmpty(renameError));
                        }
                    }
                }

                Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(sourcePath));
                Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(targetPath));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
