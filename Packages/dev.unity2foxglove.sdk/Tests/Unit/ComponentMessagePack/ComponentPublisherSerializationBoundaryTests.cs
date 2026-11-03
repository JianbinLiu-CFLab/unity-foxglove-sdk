using System.Linq;
using System.IO;
using System.Collections.Generic;
using Unity.FoxgloveSDK.UnitTests.Harness;
using System.Text.RegularExpressions;
using System;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherSerializationBoundaryTests
    {
        [Fact]
        public void RecoverableFailureDropsSampleAndAllowsRecovery()
        {
            var warnings = 0;
            var ok = ComponentPublisherSerializationBoundary.TrySerialize(() => throw new ComponentPublisherSessionFailure("bad sample"), "codec.failure", _ => warnings++, out var payload, out var diagnostic);
            Assert.False(ok); Assert.Null(payload); Assert.Equal("codec.failure", diagnostic); Assert.Equal(1, warnings);
            ok = ComponentPublisherSerializationBoundary.TrySerialize(() => new byte[] { 1, 2 }, "codec.failure", _ => warnings++, out payload, out diagnostic);
            Assert.True(ok); Assert.Equal(new byte[] { 1, 2 }, payload); Assert.Equal(string.Empty, diagnostic);
        }
        [Fact]
        public void FatalExceptionsAreNotCaught()
        {
            Assert.Throws<InvalidOperationException>(() => ComponentPublisherSerializationBoundary.TrySerialize(() => throw new InvalidOperationException("fatal"), "x", null, out _, out _));
        }
    }

    public sealed class PublisherCatalogParityTests
    {
        [Theory]
        [InlineData("FoxgloveLaserScanPublisher")]
        [InlineData("FoxgloveCameraCalibrationPublisher")]
        [InlineData("FoxgloveCompressedVideoCameraPublisher")]
        public void ConcretePublisherCatalogDisablesMessagePackWhenRuntimeDoesNotSupportIt(string publisherType)
        {
            var catalog = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/FoxgloveSdkPublisherCatalog.cs");
            var pattern = $"Concrete\\(\\s*\\\"[^\\\"]*{publisherType}\\\"(?:(?!\\bConcrete\\().)*?messagePackSupportMode:\\s*\\\"unsupported\\\"";

            Assert.Matches(new Regex(pattern, RegexOptions.Singleline), catalog);
        }
    }

    public sealed class TopicContractRegistrationScopeTests
    {
        [Fact]
        public void ProductionRegistrationExitsMatchAllowlist()
        {
            var repoRoot = FindRepoRoot();
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(Path.Combine(repoRoot, "Packages"), "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/Tests/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var source = File.ReadAllText(file);
                if (Regex.IsMatch(source, @"\.\s*(?:RegisterSchemaChannel|RegisterChannel)\s*\("))
                    actual.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
            }

            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Replay/ReplayController.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime/FoxgloveRuntime.Publishing.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/Decomposed/FoxgloveSession/FoxgloveSession.SchemaChannels.cs",
                "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/Decomposed/FoxgloveSession/FoxgloveSession.TransportEvents.cs",
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2BridgeMcapCodecs.cs",
                "Packages/dev.unity2foxglove.remotegateway.win64/Runtime/RemoteGatewayMirrorSink.cs"
            };

            Assert.Equal(expected.OrderBy(path => path), actual.OrderBy(path => path));
        }

        private static string FindRepoRoot()
        {
            for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current != null; current = current.Parent)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Packages"))
                    && Directory.Exists(Path.Combine(current.FullName, "Unity2Foxglove")))
                    return current.FullName;
            }

            throw new DirectoryNotFoundException("Repository root was not found.");
        }
    }

    public sealed class TopicContractManagerIntegrationTests
    {
        [Fact]
        public void ManagerIntegratesLedgerWithPublishingServicesAndLifecycle()
        {
            var contracts = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.ComponentPublishContracts.cs");
            var publishing = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.cs");
            var server = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Server.cs");
            var rawRecording = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.Publishing.MessagePack.cs");
            var imu = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var foxRun = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");

            Assert.Contains("TopicContractServiceName", contracts);
            Assert.Contains("RegisterTopicContractService", contracts);
            Assert.Contains("RecordTopicContract", publishing);
            Assert.Contains("RecordTopicContract(topic, schemaName, messageEncoding, \"FoxRun\", TopicContractScope.Recording)", rawRecording);
            Assert.Contains("requestedTopic", contracts);
            Assert.Contains("ClearTopicContractLedger();", server);
            Assert.Contains("if (!_foxRunRawRecordingChannelCache.TryGet(descriptor, out channelId))", rawRecording);
            var cacheHitIndex = publishing.IndexOf(
                "if (_channelCache.TryGetValue(key, out var id))",
                StringComparison.Ordinal);
            var cacheRecordIndex = publishing.IndexOf(
                "RecordTopicContract(topic",
                cacheHitIndex,
                StringComparison.Ordinal);
            Assert.True(cacheHitIndex >= 0 && cacheRecordIndex > cacheHitIndex);
            Assert.Contains("PublishProtoFromVirtualImu", imu);
            Assert.Contains("PublishProtoFromFoxRun", foxRun);
            Assert.Contains("RegisterTopicContractService();", server);
            Assert.Contains("UnregisterTopicContractService", server);
            Assert.Contains("ClearTopicContractLedger();", server);
        }
    }
}
