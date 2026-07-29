// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class Phase173108ReviewTests
    {
        [Fact]
        public void JsonChannelRejectsNullManagerAndDocumentsStaleSessionException()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/Channels/FoxgloveJsonChannel.cs");

            Assert.Contains("throw new ArgumentNullException(nameof(manager))", source);
            Assert.Contains("InvalidOperationException", source);
            Assert.Contains("old server session", source);
        }

        [Fact]
        public void SnapshotPoolDocumentsUnclearedArrayBoundary()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Utilities/VirtualLidarPointSnapshotPool.cs");

            Assert.Contains("not cleared for performance", source);
            Assert.Contains("point count as the only valid readable range", source);
        }

        [Fact]
        public void SourceGeneratorRoslynCommentMatchesPinnedIntent()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj");

            Assert.Contains("4.2.0 is the tested package pin", source);
            Assert.Contains("Version=\"4.2.0\"", source);
        }

        [Fact]
        public void McapDataLoaderDtosDocumentMutableSnapshotContract()
        {
            var channel = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoaderChannel.cs");
            var schema = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/DataLoader/McapDataLoaderSchema.cs");

            Assert.Contains("mutable DTO snapshots", channel);
            Assert.Contains("mutable DTO snapshots", schema);
            Assert.Contains("<see cref=\"Data\"/> buffer", schema);
        }

        [Fact]
        public void CsharpConformanceRunnersFailOnStderr()
        {
            var streamed = TestSources.Text(
                "Scripts/mcap/conformance/csharp-runners/CsharpStreamedReaderTestRunner.ts");
            var writer = TestSources.Text(
                "Scripts/mcap/conformance/csharp-runners/CsharpWriterTestRunner.ts");

            Assert.Contains("throw new Error(`C# streamed reader runner wrote to stderr", streamed);
            Assert.Contains("throw new Error(`C# writer runner wrote to stderr", writer);
        }

        [Fact]
        public void ManagerConfigValidatorRejectsUnsupportedBindHostsBeforeStartup()
        {
            Assert.True(ManagerConfigValidator.IsSupportedBindHost("localhost"));
            Assert.True(ManagerConfigValidator.IsSupportedBindHost("0.0.0.0"));
            Assert.True(ManagerConfigValidator.IsSupportedBindHost("::"));
            Assert.False(ManagerConfigValidator.IsSupportedBindHost("mymachine.local"));
        }

        [Fact]
        public async Task SessionTimeBroadcasterAllowsAtMostOneConcurrentReservationPerInterval()
        {
            var broadcaster = new SessionTimeBroadcaster();
            var results = Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => broadcaster.TryReserveBroadcast(TimeSpan.TicksPerSecond, 10f)))
                .ToArray();

            await Task.WhenAll(results);

            Assert.Equal(1, results.Count(task => task.Result));
        }

        [Fact]
        public void Ros2MsgSchemaRegistrationIsIdempotentForDefaultRegistry()
        {
            var registry = new DefaultSchemaRegistry();

            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            Assert.True(registry.TryGetSchema("foxglove_msgs/msg/CompressedImage", "ros2msg", out var first));

            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            Assert.True(registry.TryGetSchema("foxglove_msgs/msg/CompressedImage", "ros2msg", out var second));

            Assert.Equal(first.Name, second.Name);
            Assert.Equal(first.Encoding, second.Encoding);
            Assert.Equal(first.Content, second.Content);
            Assert.Equal(first.RawContent, second.RawContent);
        }
    }
}
