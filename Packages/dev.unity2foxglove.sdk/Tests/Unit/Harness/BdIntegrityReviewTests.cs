// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: First-pass regression probes for the combined B/D integrity review.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    /// <summary>
    /// Behavioral and structural probes for the unified B/D integrity repair.
    /// </summary>
    [Trait("Phase", "187")]
    [Trait("Domain", "BD integrity review")]
    public sealed class BdIntegrityReviewTests
    {
        [Fact]
        public void ClientEventsExposeAnEpochStampAndStampedFactory()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ClientEvents.cs");
            Assert.Contains("public readonly ulong Generation", source, StringComparison.Ordinal);
            Assert.Contains("public static ClientEvent Connect(ulong generation", source, StringComparison.Ordinal);
            Assert.Contains("ClientEventGenerationGate.IsCurrent(evt.Generation, generation)", source, StringComparison.Ordinal);
            Assert.True(ClientEventGenerationGate.IsCurrent(7, 7));
            Assert.False(ClientEventGenerationGate.IsCurrent(6, 7));
        }

        [Fact]
        public void RuntimeRejectsRegistryMutationWhileRetiredSessionCleanupIsPending()
        {
            var transport = new NoopTransport();
            var runtime = new FoxgloveRuntime(
                transport,
                new SystemClock(),
                new DefaultSchemaRegistry());
            var retired = new FoxgloveSession("bd-pending", transport);
            var field = typeof(FoxgloveRuntime).GetField(
                "_sessionPendingCleanup", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(runtime, retired);

            try
            {
                Assert.Throws<InvalidOperationException>(() => runtime.RegisterParameter(
                    "bd.pending", JValue.CreateString("value"), "string", writable: false));
                Assert.Throws<InvalidOperationException>(() => runtime.RegisterService(
                    new ServiceDescriptor { Name = "/bd/pending", Type = "bd.Pending" }));
                Assert.Throws<InvalidOperationException>(() => runtime.Parameters.Register(
                    "bd.direct", JValue.CreateString("value"), "string", writable: false));
                Assert.Throws<InvalidOperationException>(() => runtime.Assets.RegisterRoot("", ""));
                Assert.Throws<InvalidOperationException>(() => runtime.Schemas.Register(
                    new SchemaEntry { Name = "bd.Pending", Encoding = "jsonschema", Content = "{}" }));
            }
            finally
            {
                field.SetValue(runtime, null);
                retired.Dispose();
                runtime.Dispose();
            }
        }

        [Fact]
        public void StopServerTeardownHasASeparatePreCleanupRunner()
        {
            var stateType = typeof(FoxgloveRuntime).Assembly.GetType(
                "Unity.FoxgloveSDK.Components.FoxgloveManagerTeardownState");
            Assert.NotNull(stateType);
            var overload = Array.Find(
                stateType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
                method => method.Name == "RunStopServer"
                    && method.GetParameters().Length == 9);
            Assert.NotNull(overload);
        }

        [Fact]
        public void SuccessfulCertificateCommitReachesTerminalCleanupState()
        {
            var directory = Path.Combine(
                Path.GetTempPath(), "bd-cert-terminal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var pfx = Path.Combine(directory, "server.pfx");
            var root = Path.Combine(directory, "root.crt");
            File.WriteAllText(pfx, "old-pfx", Encoding.ASCII);
            File.WriteAllText(root, "old-root", Encoding.ASCII);

            try
            {
                using (var transaction = FoxgloveCertificatePairTransaction.Begin(pfx, root))
                {
                    File.WriteAllText(transaction.PfxTempPath, "new-pfx", Encoding.ASCII);
                    File.WriteAllText(transaction.RootCaTempPath, "new-root", Encoding.ASCII);
                    transaction.Commit();

                    var pending = typeof(FoxgloveCertificatePairTransaction).GetMethod(
                        "HasPendingCleanup", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(pending);
                    Assert.False((bool)pending.Invoke(transaction, null));
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void ReplayEngineExposesPerTickScanObservation()
        {
            var property = typeof(McapReplayEngine).GetProperty(
                "LastTickScannedRecordCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotNull(property);
        }

        private sealed class NoopTransport : IFoxgloveTransport
        {
            public bool IsRunning => false;
            public event Action<uint> OnClientConnected;
            public event Action<uint> OnClientDisconnected;
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) { }
            public void Stop() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void Dispose() { }
        }
    }
}
