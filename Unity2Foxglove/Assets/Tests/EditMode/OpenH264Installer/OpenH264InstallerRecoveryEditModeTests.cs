// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Require the OpenH264 installer to leave a previous install transaction intact when its recovery fails.

using System;
using System.IO;
using NUnit.Framework;
using Unity.FoxgloveSDK.Editor;
using UnityEngine;

namespace Unity2Foxglove.Tests.EditMode.OpenH264Installer
{
    /// <summary>Exercises the installer's handling of an unrecoverable previous install transaction.</summary>
    public sealed class OpenH264InstallerRecoveryEditModeTests
    {
        private const string JournalFileName = "openh264-install.journal";

        [Test]
        [Category("OpenH264Installer")]
        public void FailedRecoveryOfAPreviousTransactionKeepsJournalAndStaging()
        {
            var repoRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            var root = Path.Combine(repoRoot, "build", "tmp", "openh264-installer-recovery-" + Guid.NewGuid().ToString("N"));
            try
            {
                var version = OpenH264InstallLocation.GetVersionedDirectory(root);
                Directory.CreateDirectory(version);
                var journal = Path.Combine(version, JournalFileName);
                const string malformedJournal = "state=Unknown\n";
                File.WriteAllText(journal, malformedJournal);
                var staging = new[]
                {
                    Path.Combine(version, OpenH264OfficialBinaryManifest.AssetName + ".download"),
                    OpenH264InstallLocation.GetFinalDllPath(root) + ".tmp",
                    OpenH264InstallLocation.GetFinalHelperPath(root) + ".tmp"
                };
                foreach (var path in staging)
                    File.WriteAllText(path, "previous transaction");

                var result = OpenH264OfficialBinaryInstaller.Install(root);

                Assert.That(result.Success, Is.False);
                StringAssert.Contains("could not recover the previous install transaction", result.ErrorMessage);
                Assert.That(File.ReadAllText(journal), Is.EqualTo(malformedJournal));
                foreach (var path in staging)
                    Assert.That(File.Exists(path), Is.True, "Staging file was removed after a failed recovery: " + path);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
