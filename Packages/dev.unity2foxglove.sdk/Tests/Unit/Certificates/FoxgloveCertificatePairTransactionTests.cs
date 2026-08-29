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

        private static string CreateDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "foxglove-cert-pair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
