// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Exercise OpenH264 install-root reparse-point rejection.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class OpenH264InstallLocationTests
    {
        [WindowsSymlinkFact]
        public void RejectsAReparsePointAtTheInstallRoot()
        {
            var fixture = CreateFixture("root-link");
            try
            {
                CreateDirectoryLinkOrSkip(fixture.Link, fixture.Target);

                Assert.False(
                    OpenH264InstallLocation.IsAllowedInstallRoot(fixture.Link, out var reason));
                Assert.Contains("symlink or junction", reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        public void RejectsAReparsePointAtTheVersionDirectory()
        {
            var fixture = CreateFixture("version-link");
            var versionDirectory = OpenH264InstallLocation.GetVersionedDirectory(fixture.Root);
            try
            {
                CreateDirectoryLinkOrSkip(versionDirectory, fixture.Target);

                Assert.False(
                    OpenH264InstallLocation.IsAllowedInstallRoot(fixture.Root, out var reason));
                Assert.Contains("versioned directory", reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        public void RejectsAReparsePointBelowTheVersionDirectory()
        {
            var fixture = CreateFixture("descendant-link");
            var versionDirectory = OpenH264InstallLocation.GetVersionedDirectory(fixture.Root);
            var descendant = Path.Combine(versionDirectory, "nested");
            try
            {
                Directory.CreateDirectory(versionDirectory);
                CreateDirectoryLinkOrSkip(descendant, fixture.Target);

                Assert.False(
                    OpenH264InstallLocation.IsAllowedInstallRoot(fixture.Root, out var reason));
                Assert.Contains("versioned directory", reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Opens the verified root while the installer root lock is held.</summary>
        public void SafeRootOpensWhileInstallRootLockIsHeld()
        {
            var fixture = CreateFixture("root-lock-compatibility");
            try
            {
                Assert.True(
                    OpenH264InstallLocation.TryAcquireInstallRootLock(
                        fixture.Root,
                        out var installLock,
                        out var lockReason),
                    lockReason);
                using (installLock)
                {
                    Assert.True(
                        OpenH264WindowsSafeDirectory.TryOpenExisting(
                            fixture.Root,
                            out var root,
                            out var rootReason),
                        rootReason);
                    root.Dispose();
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        public void LocksTheInstallDirectoriesAgainstReplacement()
        {
            var fixture = CreateFixture("directory-lock");
            var versionDirectory = OpenH264InstallLocation.GetVersionedDirectory(fixture.Root);
            var replacement = versionDirectory + ".moved";
            Directory.Delete(fixture.Root, recursive: true);
            try
            {
                Assert.True(
                    OpenH264InstallLocation.TryAcquireInstallRootLock(
                        fixture.Root,
                        out var rootLock,
                        out var reason),
                    reason);
                using (rootLock)
                {
                    Directory.CreateDirectory(versionDirectory);
                    var probe = Path.Combine(versionDirectory, "write-probe.txt");
                    var movedProbe = probe + ".moved";
                    File.WriteAllText(probe, "probe");
                    Assert.Equal("probe", File.ReadAllText(probe));
                    File.Move(probe, movedProbe);
                    File.Delete(movedProbe);
                    var blocked = false;
                    try
                    {
                        Directory.Move(versionDirectory, replacement);
                    }
                    catch (IOException)
                    {
                        blocked = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        blocked = true;
                    }

                    Assert.True(blocked);
                }
            }
            finally
            {
                if (Directory.Exists(replacement))
                    Directory.Delete(replacement, recursive: true);
                fixture.Dispose();
            }
        }

        [WindowsSymlinkFact]
        public void AcceptsExtendedWindowsPathWhenFinalPathUsesDevicePrefix()
        {
            var fixture = CreateFixture("extended-path");
            var extendedRoot = @"\\?\" + fixture.Root;
            try
            {
                Assert.True(
                    OpenH264InstallLocation.TryAcquireInstallRootLock(
                        extendedRoot,
                        out var rootLock,
                        out var reason),
                    reason);
                using (rootLock)
                {
                }
            }
            finally
            {
                fixture.Dispose();
            }
        }

        private static Fixture CreateFixture(string name)
        {
            var root = Path.Combine(
                TestSources.RepoRoot,
                "build",
                "tmp",
                "openh264-location-" + name + "-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(root, "outside");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(target);
            return new Fixture(root, target, link);
        }

        private static void CreateDirectoryLinkOrSkip(string link, string target)
        {
            Assert.True(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && CreateSymbolicLink(link, target, 0x1 | 0x2),
                "The Windows-only fixture could not create a directory symlink.");
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(
            string symbolicFileName,
            string targetFileName,
            int flags);

        private sealed class Fixture : IDisposable
        {
            public Fixture(string root, string target, string link)
            {
                Root = root;
                Target = target;
                Link = link;
            }

            public string Root { get; }
            public string Target { get; }
            public string Link { get; }

            public void Dispose()
            {
                if (Directory.Exists(Link))
                    Directory.Delete(Link);
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
        }
    }

}
