using System;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.UnitTests.Harness;
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
            var guard = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/Session/ComponentPublisherChannelContractGuard.cs");
            var imu = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var foxRun = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager/FoxgloveManager.FoxRunTransportProviders.cs");

            Assert.Contains("TopicContractServiceName", contracts);
            Assert.Contains("RegisterTopicContractService", contracts);
            Assert.Contains("TopicContractItemSchema", contracts);
            Assert.Contains(
                "\\\"required\\\":[\\\"topic\\\",\\\"schemaName\\\",\\\"encoding\\\",\\\"ownerKind\\\",\\\"shapeIdentity\\\",\\\"schemaEncoding\\\",\\\"schemaFingerprint\\\",\\\"scope\\\"]",
                contracts);
            Assert.Contains("\\\"scope\\\":{\\\"type\\\":\\\"string\\\",\\\"enum\\\":[\\\"Live\\\",\\\"Recording\\\"]}", contracts);
            Assert.Contains("internal enum TopicContractScope", guard);
            Assert.Contains("internal readonly struct TopicContract", guard);
            Assert.Contains("internal readonly struct TopicContractConflict", guard);
            Assert.Contains("internal readonly struct TopicContractRegistration", guard);
            Assert.Contains("internal sealed class TopicContractLedger", guard);
            Assert.Contains("RecordTopicContract", publishing);
            Assert.Contains(
                "RecordTopicContract(\n                topic,\n                key.schemaName,",
                publishing);
            Assert.Contains("                key.shapeIdentity);", publishing);
            Assert.Contains("ComputeSchemaFingerprint(schema)", rawRecording);
            Assert.Contains("TopicContractScope.Recording", rawRecording);
            Assert.Contains("requestedTopic", contracts);
            Assert.Contains("ClearPublishingSessionState();", server);
            Assert.Contains(
                "ClearPublishingSessionState, \"clear publishing session state after failed startup\"",
                server);
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
            Assert.Contains("ClearPublishingSessionState();", server);
        }
    }
}
