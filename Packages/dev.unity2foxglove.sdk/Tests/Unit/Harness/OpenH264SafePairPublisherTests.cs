// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Exercise journaled OpenH264 pair publication, interruption and recovery on Windows.

using System;
using System.IO;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class OpenH264SafePairPublisherTests
    {
        private const string Dll = "openh264.dll";
        private const string Helper = "openh264-helper.exe";
        private const string StagedDll = Dll + ".tmp";
        private const string StagedHelper = Helper + ".tmp";
        private const string Download = "openh264.dll.bz2.download";
        private static readonly string[] Staging = { Download, StagedDll, StagedHelper };

        [WindowsSafeFileFact]
        /// <summary>Replaces an existing pair and leaves no backup or journal.</summary>
        public void PublishReplacesExistingPairAndClearsJournal()
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                fixture.Publish();

                fixture.AssertPair("new");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Installs a pair into an empty version directory.</summary>
        public void PublishInstallsFreshPair()
        {
            using (var fixture = new Fixture(withOriginals: false))
            {
                fixture.Publish();

                fixture.AssertPair("new");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>An interruption after the backup intent keeps the old pair.</summary>
        public void InterruptAfterBackupsMovedKeepsOldPair()
            => AssertInterruptKeepsOldPair(OpenH264InstallJournalState.BackupsMoved);

        [WindowsSafeFileFact]
        /// <summary>A failure after the DLL publish intent rolls the DLL back.</summary>
        public void DllPublishedThenHelperFailedKeepsOldPair()
            => AssertInterruptKeepsOldPair(OpenH264InstallJournalState.HelperPublished);

        [WindowsSafeFileFact]
        /// <summary>An interruption after the DLL publish intent keeps the old pair.</summary>
        public void InterruptAfterDllPublishedKeepsOldPair()
            => AssertInterruptKeepsOldPair(OpenH264InstallJournalState.DllPublished);

        [WindowsSafeFileFact]
        /// <summary>An interruption after commit keeps the new pair and finishes cleanup.</summary>
        public void InterruptAfterCommittedKeepsNewPair()
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                Assert.Throws<InvalidOperationException>(() => fixture.Publish(Interrupt(OpenH264InstallJournalState.Committed)));

                fixture.AssertPair("new");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>A failed fresh install removes the partially published DLL.</summary>
        public void InterruptedFreshInstallLeavesNoPair()
        {
            using (var fixture = new Fixture(withOriginals: false))
            {
                Assert.Throws<InvalidOperationException>(() => fixture.Publish(Interrupt(OpenH264InstallJournalState.HelperPublished)));

                Assert.False(File.Exists(fixture.PathOf(Dll)));
                Assert.False(File.Exists(fixture.PathOf(Helper)));
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>A backup cleanup failure after commit keeps the new pair and recovers on the next run.</summary>
        public void HelperPublishedThenBackupCleanupFailedRecoversLater()
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                FileStream heldBackup = null;
                try
                {
                    fixture.Publish(state =>
                    {
                        if (state == OpenH264InstallJournalState.Committed)
                            heldBackup = new FileStream(fixture.PathOf(Helper + ".bak"), FileMode.Open, FileAccess.Read, FileShare.Read);
                    });

                    fixture.AssertPair("new");
                    Assert.True(File.Exists(fixture.PathOf(OpenH264InstallJournal.FileName)));
                }
                finally
                {
                    heldBackup?.Dispose();
                }

                Assert.True(fixture.Recover(out var reason), reason);
                fixture.AssertPair("new");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Recovery after a crash between the DLL and helper renames restores the old pair idempotently.</summary>
        public void RecoverRollsBackCrashAfterDllPublished()
        {
            using (var fixture = new Fixture(withOriginals: false))
            {
                fixture.WriteArtifact(Dll, "new");
                fixture.WriteArtifact(Dll + ".bak", "old");
                fixture.WriteArtifact(Helper + ".bak", "old");
                fixture.WriteArtifact(StagedHelper, "new");
                fixture.WriteJournal(OpenH264InstallJournalState.DllPublished, dllOriginal: true, helperOriginal: true);

                Assert.True(fixture.Recover(out var reason), reason);
                Assert.True(fixture.Recover(out reason), reason);

                fixture.AssertPair("old");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>Recovery before any backup only removes staged files.</summary>
        public void RecoverRemovesStagingBeforeBackups()
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                fixture.WriteArtifact(Download, "asset");
                fixture.WriteJournal(OpenH264InstallJournalState.Decompressed, dllOriginal: false, helperOriginal: false);

                Assert.True(fixture.Recover(out var reason), reason);

                fixture.AssertPair("old");
                fixture.AssertClean();
            }
        }

        [WindowsSafeFileFact]
        /// <summary>A directory in place of a backup file fails recovery closed and keeps the journal.</summary>
        public void RecoverFailsClosedOnDirectoryBackup()
        {
            using (var fixture = new Fixture(withOriginals: false))
            {
                Directory.CreateDirectory(fixture.PathOf(Dll + ".bak"));
                fixture.WriteJournal(OpenH264InstallJournalState.BackupsMoved, dllOriginal: true, helperOriginal: false);

                Assert.False(fixture.Recover(out _));
                Assert.True(File.Exists(fixture.PathOf(OpenH264InstallJournal.FileName)));
            }
        }

        [WindowsSafeFileFact]
        /// <summary>A malformed journal fails recovery closed.</summary>
        public void RecoverRejectsMalformedJournal()
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                fixture.WriteArtifact(OpenH264InstallJournal.FileName, "state=5\ndll_original=1\nhelper_original=1\n");

                Assert.False(fixture.Recover(out var reason));
                Assert.Contains("malformed", reason, StringComparison.OrdinalIgnoreCase);
                fixture.AssertPair("old");
            }
        }

        /// <summary>Interrupts publication after the given journal state is recorded and asserts the old pair.</summary>
        private static void AssertInterruptKeepsOldPair(OpenH264InstallJournalState state)
        {
            using (var fixture = new Fixture(withOriginals: true))
            {
                Assert.Throws<InvalidOperationException>(() => fixture.Publish(Interrupt(state)));

                fixture.AssertPair("old");
                fixture.AssertClean();
            }
        }

        /// <summary>Returns a hook that throws once the given state has been journaled.</summary>
        private static Action<OpenH264InstallJournalState> Interrupt(OpenH264InstallJournalState target)
            => state =>
            {
                if (state == target)
                    throw new InvalidOperationException("Injected interruption at " + state + ".");
            };

        /// <summary>Owns a locked version directory with optional original artifacts and staged files.</summary>
        private sealed class Fixture : IDisposable
        {
            private readonly string _root;
            private readonly OpenH264WindowsSafeDirectory _safeRoot;
            private readonly OpenH264WindowsSafeDirectory _version;

            internal Fixture(bool withOriginals)
            {
                _root = Path.Combine(
                    TestSources.RepoRoot,
                    "build",
                    "tmp",
                    "openh264-pair-publisher-" + Guid.NewGuid().ToString("N"));
                Version = Path.Combine(_root, "version");
                Directory.CreateDirectory(Version);
                if (withOriginals)
                {
                    WriteArtifact(Dll, "old");
                    WriteArtifact(Helper, "old");
                }

                Assert.True(OpenH264WindowsSafeDirectory.TryOpenExisting(_root, out _safeRoot, out var rootReason), rootReason);
                Assert.True(_safeRoot.TryOpenLockedDirectory("version", out _version, out var versionReason), versionReason);
            }

            internal string Version { get; }

            internal string PathOf(string name) => Path.Combine(Version, name);

            internal void WriteArtifact(string name, string text) => File.WriteAllText(PathOf(name), text);

            internal void WriteJournal(OpenH264InstallJournalState state, bool dllOriginal, bool helperOriginal)
                => Assert.True(
                    OpenH264InstallJournal.TryWrite(
                        _version,
                        new OpenH264InstallJournalRecord(state, dllOriginal, helperOriginal),
                        out var reason),
                    reason);

            internal void Publish(Action<OpenH264InstallJournalState> afterJournal = null)
            {
                WriteArtifact(StagedHelper, "new");
                Assert.True(_version.TryCreateProtectedFile(StagedDll, out var stagedDll, out var reason), reason);
                Assert.True(stagedDll.WriteAllBytes(new byte[] { (byte)'n', (byte)'e', (byte)'w' }, out reason), reason);
                using (stagedDll)
                    OpenH264ArtifactPairPublisher.Publish(_version, stagedDll, StagedHelper, Dll, Helper, Staging, afterJournal);
            }

            internal bool Recover(out string reason)
                => OpenH264ArtifactPairPublisher.TryRecover(_version, Dll, Helper, Staging, out reason);

            internal void AssertPair(string content)
            {
                Assert.Equal(content, File.ReadAllText(PathOf(Dll)));
                Assert.Equal(content, File.ReadAllText(PathOf(Helper)));
            }

            internal void AssertClean()
            {
                foreach (var name in new[] { Dll + ".bak", Helper + ".bak", OpenH264InstallJournal.FileName, Download, StagedDll, StagedHelper })
                    Assert.False(File.Exists(PathOf(name)), name);
            }

            public void Dispose()
            {
                _version?.Dispose();
                _safeRoot?.Dispose();
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
        }
    }
}
