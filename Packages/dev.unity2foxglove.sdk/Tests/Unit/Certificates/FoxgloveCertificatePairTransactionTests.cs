// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Certificates
// Purpose: Verify certificate pair staging and rollback behavior.

using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace FoxgloveSdk.UnitTests.Certificates
{
    public sealed class FoxgloveCertificatePairTransactionTests
    {
        [Fact]
        public void GeneratorStagesBothBackendsBeforePublishingEitherArtifact()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs");

            Assert.Contains("FoxgloveCertificatePairTransaction.Begin", source, StringComparison.Ordinal);
            Assert.Contains("transaction.PfxTempPath", source, StringComparison.Ordinal);
            Assert.Contains("transaction.RootCaTempPath", source, StringComparison.Ordinal);
            Assert.Contains("transaction.Commit()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("WritePkcs12(context.PfxPath", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Quote(context.PfxPath)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Quote(context.RootCaPath)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllText(context.RootCaPath", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ProductionGeneratorIsCompiledAndHasAValidUnityMetaIdentity()
        {
            var project = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj");
            Assert.Contains(
                "../../Editor/Certificates/FoxgloveLocalDevCertificateGenerator.cs",
                project,
                StringComparison.Ordinal);

            var meta = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Certificates/FoxgloveCertificatePairTransaction.cs.meta");
            var guidLine = meta.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Single(line => line.StartsWith("guid:", StringComparison.Ordinal));
            var guid = guidLine.Substring("guid:".Length).Trim();
            Assert.Matches("^[0-9a-fA-F]{32}$", guid);
            Assert.Contains("MonoImporter:", meta, StringComparison.Ordinal);
        }

        [Fact]
        public void CommitReplacesBothArtifactsFromStagedFiles()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            File.WriteAllText(rootPath, "generation-A-root", Encoding.ASCII);

            try
            {
                using (var transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath))
                {
                    File.WriteAllText(transaction.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                    File.WriteAllText(transaction.RootCaTempPath, "generation-B-root", Encoding.ASCII);
                    transaction.Commit();
                }

                Assert.Equal("generation-B-pfx", File.ReadAllText(pfxPath));
                Assert.Equal("generation-B-root", File.ReadAllText(rootPath));
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
                Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void IncompleteGenerationLeavesExistingPairUntouched()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            File.WriteAllText(rootPath, "generation-A-root", Encoding.ASCII);

            try
            {
                using (var transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath))
                {
                    File.WriteAllText(transaction.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                    var error = Assert.Throws<InvalidOperationException>(() => transaction.Commit());
                    Assert.Contains("root certificate artifact is missing", error.Message, StringComparison.Ordinal);
                }

                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
                Assert.Equal("generation-A-root", File.ReadAllText(rootPath));
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
                Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void FailedCommitWithReadOnlyInstallKeepsRollbackRetryableAndDisposeIdempotent()
        {
            // Windows preserves the read-only attribute across a move and rejects
            // deletion of that destination. The directory-shaped root destination
            // then forces a failure after the PFX has been installed, exercising
            // the partial-commit rollback path without reflection or test hooks.
            if (!OperatingSystem.IsWindows())
                return;

            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            Directory.CreateDirectory(rootPath);

            FoxgloveCertificatePairTransaction transaction = null;
            try
            {
                transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                File.WriteAllText(transaction.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                File.WriteAllText(transaction.RootCaTempPath, "generation-B-root", Encoding.ASCII);
                File.SetAttributes(transaction.PfxTempPath, FileAttributes.ReadOnly);

                var commitError = Record.Exception(() => transaction.Commit());
                Assert.IsType<IOException>(commitError);

                // The first cleanup pass must be harmless even while the
                // simulated read-only destination still blocks deletion.
                Assert.Null(Record.Exception(() => transaction.Dispose()));

                // Release the simulated read-only destination before the
                // second cleanup pass. The transaction remains retryable until
                // the installed artifact and its backup have both settled.
                if (File.Exists(pfxPath))
                    File.SetAttributes(pfxPath, FileAttributes.Normal);

                Assert.Null(Record.Exception(() => transaction.Dispose()));
                Assert.Null(Record.Exception(() => transaction.Dispose()));

                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
                Assert.True(Directory.Exists(rootPath));
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
                Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            }
            finally
            {
                if (File.Exists(pfxPath))
                    File.SetAttributes(pfxPath, FileAttributes.Normal);
                if (transaction != null)
                    Record.Exception(() => transaction.Dispose());
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void FailedCommitRestoresInstalledArtifactBeforeReturningToCaller()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            Directory.CreateDirectory(rootPath);

            FoxgloveCertificatePairTransaction transaction = null;
            try
            {
                transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                File.WriteAllText(transaction.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                File.WriteAllText(transaction.RootCaTempPath, "generation-B-root", Encoding.ASCII);

                var commitError = Record.Exception(() => transaction.Commit());
                Assert.IsType<IOException>(commitError);
                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
                Assert.True(Directory.Exists(rootPath));
                Assert.Empty(Directory.GetFiles(directory, "*.bak"));
            }
            finally
            {
                if (transaction != null)
                    Record.Exception(() => transaction.Dispose());
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void FailedCommitRestoresBothOriginalArtifactsBeforeReturning()
        {
            // A non-sharing handle on the second staged file makes its install
            // fail after both originals have moved to the rollback journal.
            if (!OperatingSystem.IsWindows())
                return;

            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            File.WriteAllText(rootPath, "generation-A-root", Encoding.ASCII);

            FoxgloveCertificatePairTransaction transaction = null;
            FileStream rootTempLock = null;
            try
            {
                transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                File.WriteAllText(transaction.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                File.WriteAllText(transaction.RootCaTempPath, "generation-B-root", Encoding.ASCII);
                rootTempLock = new FileStream(
                    transaction.RootCaTempPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);

                var commitError = Record.Exception(() => transaction.Commit());
                Assert.IsType<IOException>(commitError);
                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
                Assert.Equal("generation-A-root", File.ReadAllText(rootPath));
                Assert.Empty(Directory.GetFiles(directory, "*.bak"));

                rootTempLock.Dispose();
                rootTempLock = null;
            }
            finally
            {
                rootTempLock?.Dispose();
                if (transaction != null)
                    Record.Exception(() => transaction.Dispose());
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "foxglove-cert-pair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
