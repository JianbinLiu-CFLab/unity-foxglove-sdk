// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Certificates
// Purpose: Verify certificate pair staging and rollback behavior.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        public async Task ConcurrentTransactionsForOnePairAreSerialized()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfxPath, "generation-A-pfx", Encoding.ASCII);
            File.WriteAllText(rootPath, "generation-A-root", Encoding.ASCII);

            FoxgloveCertificatePairTransaction first = null;
            Task secondTask = null;
            var secondEntered = new ManualResetEventSlim(false);
            var secondCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                first = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                File.WriteAllText(first.PfxTempPath, "generation-B-pfx", Encoding.ASCII);
                File.WriteAllText(first.RootCaTempPath, "generation-B-root", Encoding.ASCII);

                secondTask = Task.Run(() =>
                {
                    try
                    {
                        using (var second = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath))
                        {
                            secondEntered.Set();
                            File.WriteAllText(second.PfxTempPath, "generation-C-pfx", Encoding.ASCII);
                            File.WriteAllText(second.RootCaTempPath, "generation-C-root", Encoding.ASCII);
                            second.Commit();
                        }

                        secondCompleted.TrySetResult(true);
                    }
                    catch (Exception exception)
                    {
                        secondCompleted.TrySetException(exception);
                    }
                });

                Assert.False(
                    secondEntered.Wait(TimeSpan.FromMilliseconds(250)),
                    "A second generator must not enter the pair transaction while the first owns it.");

                // Dispose releases the pair gate; the waiting transaction may
                // now enter and publish its complete pair without interleaving
                // the first transaction's rollback journal.
                first.Dispose();
                first = null;
                Assert.True(
                    await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(3))) == secondTask,
                    "The serialized pair transaction did not make progress after the first owner released it.");
                await secondTask;
                Assert.True(await secondCompleted.Task);
                Assert.Equal("generation-C-pfx", File.ReadAllText(pfxPath));
                Assert.Equal("generation-C-root", File.ReadAllText(rootPath));
            }
            finally
            {
                first?.Dispose();
                if (secondTask != null && !secondTask.IsCompleted)
                {
                    // Ensure a failed assertion cannot leave a worker blocked
                    // on the pair gate while the temporary directory is torn
                    // down.
                    await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(3)));
                }
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void DisposeRetriesADeferredRestoreAfterTheConflictingPathIsRemoved()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            FoxgloveCertificatePairTransaction transaction = null;
            try
            {
                transaction = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                var backupPath = (string)typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxBackupPath", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(transaction);
                typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxOriginalMoved", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(transaction, true);
                typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxInstalled", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(transaction, true);
                File.WriteAllText(backupPath, "generation-A-pfx", Encoding.ASCII);
                Directory.CreateDirectory(pfxPath);

                // The first pass must retain the journal when the destination
                // cannot be removed, rather than treating the directory as an
                // absent file and losing the retry latch.
                transaction.Dispose();
                Assert.True(Directory.Exists(pfxPath));
                Assert.True(File.Exists(backupPath));

                Directory.Delete(pfxPath);
                transaction.Dispose();

                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
                Assert.False(File.Exists(backupPath));
                var pending = typeof(FoxgloveCertificatePairTransaction).GetMethod(
                    "HasPendingCleanup", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(pending);
                Assert.False((bool)pending.Invoke(transaction, null));
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
        public async Task PendingCleanupRetainsPairLockUntilRecoveryCompletes()
        {
            var directory = CreateDirectory();
            var pfxPath = Path.Combine(directory, "server.pfx");
            var rootPath = Path.Combine(directory, "root.crt");
            FoxgloveCertificatePairTransaction first = null;
            Task secondTask = null;
            var secondEntered = new ManualResetEventSlim(false);
            try
            {
                first = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath);
                var backupPath = (string)typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxBackupPath", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(first);
                typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxOriginalMoved", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(first, true);
                typeof(FoxgloveCertificatePairTransaction)
                    .GetField("_pfxInstalled", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(first, true);
                File.WriteAllText(backupPath, "generation-A-pfx", Encoding.ASCII);
                Directory.CreateDirectory(pfxPath);

                first.Dispose();
                Assert.True(File.Exists(backupPath));

                secondTask = Task.Run(() =>
                {
                    using (var second = FoxgloveCertificatePairTransaction.Begin(pfxPath, rootPath))
                        secondEntered.Set();
                });
                Assert.False(
                    secondEntered.Wait(TimeSpan.FromMilliseconds(300)),
                    "A pending rollback journal must retain the pair lock until recovery completes.");

                Directory.Delete(pfxPath);
                first.Dispose();
                Assert.True(
                    await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(3))) == secondTask,
                    "The waiting transaction did not proceed after the journal was recovered.");
                await secondTask;
                Assert.Equal("generation-A-pfx", File.ReadAllText(pfxPath));
            }
            finally
            {
                first?.Dispose();
                if (secondTask != null && !secondTask.IsCompleted)
                    await Task.WhenAny(secondTask, Task.Delay(TimeSpan.FromSeconds(3)));
                if (Directory.Exists(directory))
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
