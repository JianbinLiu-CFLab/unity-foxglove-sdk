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
            Assert.Contains("RecordTopicContract(topic, schemaName, messageEncoding, \"FoxRun\")", rawRecording);
            Assert.Contains("PublishProtoFromVirtualImu", imu);
            Assert.Contains("PublishProtoFromFoxRun", foxRun);
            Assert.Contains("RegisterTopicContractService();", server);
            Assert.Contains("UnregisterTopicContractService", server);
            Assert.Contains("_topicContractLedger.Clear();", server);
        }
    }
}
