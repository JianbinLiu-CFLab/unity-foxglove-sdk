using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.Schemas.Proto
{
    public sealed class CameraJpegWorkerPayloadTests
    {
        [Fact]
        public void CompletedWorkerResultRetainsEncodedJpegForMessagePackPublication()
        {
            var result = CameraJpegWorkerEncoder.EncodeJpegRequest(new JpegEncodeRequest(
                new byte[] { 255, 0, 0 },
                1,
                1,
                90,
                123,
                "camera",
                publishWebSocket: false,
                publishProvider: false,
                publishNativeFrame: false,
                PublisherEffectiveEncoding.Json,
                maxEncodedBytes: 1024 * 1024,
                generation: 1,
                jpegWorkerGeneration: 1));

            Assert.True(result.Success, result.Error);
            Assert.NotNull(result.EncodedJpeg);
            Assert.Equal(result.EncodedJpeg.Length, result.JpegBytes);
            Assert.Equal(0xFF, result.EncodedJpeg[0]);
            Assert.Equal(0xD8, result.EncodedJpeg[1]);
        }

        [Fact]
        public void CameraRawSubscribersRespectReplaySuppressionAtTheNativeFanoutBoundary()
        {
            AssertNativeFanoutHonorsReplaySuppression(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Raw.cs",
                "private void InvokeRawSubscribers(SensorRawImageFrame frame)",
                "SensorRawImageFrame",
                "SensorRawImageReady");
        }

        [Fact]
        public void CameraCompressedSubscribersRespectReplaySuppressionAtTheNativeFanoutBoundary()
        {
            AssertNativeFanoutHonorsReplaySuppression(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs",
                "private void InvokeCompressedSubscribers(SensorCompressedImageFrame frame)",
                "SensorCompressedImageFrame",
                "SensorCompressedImageReady");
        }

        [Fact]
        public void CameraNativeFanoutBlocksPublisherOpenedDuringReplay()
        {
            AssertNativeFanoutHonorsReplaySuppression(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Raw.cs",
                "private void InvokeRawSubscribers(SensorRawImageFrame frame)",
                "SensorRawImageFrame",
                "SensorRawImageReady",
                includeManagerReplaySuppression: true);
        }

        [Fact]
        public void CameraCompressedFanoutBlocksPublisherOpenedDuringReplay()
        {
            AssertNativeFanoutHonorsReplaySuppression(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs",
                "private void InvokeCompressedSubscribers(SensorCompressedImageFrame frame)",
                "SensorCompressedImageFrame",
                "SensorCompressedImageReady",
                includeManagerReplaySuppression: true);
        }

        [Fact]
        public void CameraReadbackDropsResultsThatCompleteDuringReplaySuppression()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs");
            var method = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                source,
                "private void OnReadbackComplete(AsyncGPUReadbackRequest req, int generation, ulong renderUnixNs, int captureWidth, int captureHeight)");
            AssertReplayGuardPrecedesOutput(
                method,
                "SubmitVideoFrame(",
                "PublishRawFrame(",
                "QueueJpegFrame(",
                "PublishJpegFrame(");
        }

        [Fact]
        public void CameraCompletedJpegResultsDropDuringReplaySuppression()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.Jpeg.cs");
            var method = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                source,
                "private void PublishCompletedJpegFrame(JpegEncodeResult result)");
            AssertReplayGuardPrecedesOutput(
                method,
                "InvokeCompressedSubscribers(",
                "TryPublishComponentMessagePackImage(",
                "PublishProto(",
                "Publish(",
                "PublishOrdinaryTransport(");
        }

        private static void AssertReplayGuardPrecedesOutput(string method, params string[] outputCalls)
        {
            var guardIndex = method.IndexOf("if (IsReplaySuppressed", StringComparison.Ordinal);
            Assert.True(guardIndex >= 0, "Missing replay suppression guard.");

            var firstOutputIndex = outputCalls
                .Select(call => method.IndexOf(call, StringComparison.Ordinal))
                .Where(index => index >= 0)
                .DefaultIfEmpty(-1)
                .Min();
            Assert.True(firstOutputIndex >= 0, "Missing camera output call.");
            Assert.True(
                guardIndex < firstOutputIndex,
                "Replay suppression must be checked before the first output call.");
        }

        private static void AssertNativeFanoutHonorsReplaySuppression(
            string relativePath,
            string signature,
            string frameType,
            string eventName,
            bool includeManagerReplaySuppression = false)
        {
            var method = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.ExtractMethod(
                    Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(relativePath),
                    signature)
                .Replace("private void", "public void", StringComparison.Ordinal)
                .Replace(frameType, "Frame", StringComparison.Ordinal)
                .Replace("Debug.LogWarning", "LogWarning", StringComparison.Ordinal);
            var replayGate = "private bool IsReplaySuppressed => Suppressed;";
            var managerField = string.Empty;
            var managerType = string.Empty;
            var managerSetup = "";
            if (includeManagerReplaySuppression)
            {
                var publisherBase = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                    "Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
                var propertyStart = publisherBase.IndexOf(
                    "protected bool IsReplaySuppressed", StringComparison.Ordinal);
                Assert.True(propertyStart >= 0);
                var propertyEnd = publisherBase.IndexOf(';', propertyStart) + 1;
                replayGate = publisherBase.Substring(propertyStart, propertyEnd - propertyStart)
                    .Replace("protected", "private", StringComparison.Ordinal);
                managerField = "    private FakeManager _manager;\n";
                managerType = "    private sealed class FakeManager { public bool SuppressLivePublishersForReplay; }\n";
                managerSetup = "        _manager = new FakeManager { SuppressLivePublishersForReplay = managerSuppressed };\n";
            }
            var source = "using System;\n"
                         + "public sealed class CameraNativeOutputProbe {\n"
                         + "    public bool Suppressed;\n"
                         + "    private bool _replaySuppressed;\n"
                         + "    public event Action<Frame> " + eventName + ";\n"
                         + managerField
                         + managerType
                         + "    " + replayGate + "\n"
                         + "    private static void LogWarning(string message) { }\n"
                         + "    public int Dispatch(bool suppressed, bool managerSuppressed) {\n"
                         + "        var count = 0;\n"
                         + "        " + eventName + " += _ => count++;\n"
                         + "        Suppressed = suppressed;\n"
                         + "        _replaySuppressed = suppressed;\n"
                         + managerSetup
                         + "        " + (eventName.Contains("Raw", StringComparison.Ordinal)
                             ? "InvokeRawSubscribers(new Frame());"
                             : "InvokeCompressedSubscribers(new Frame());") + "\n"
                         + "        return count;\n"
                         + "    }\n"
                         + method + "\n"
                         + "    public sealed class Frame { }\n"
                         + "}\n";
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create(
                "CameraNativeOutputProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(emit.Success, string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            var assembly = Assembly.Load(image.ToArray());
            var type = assembly.GetType("CameraNativeOutputProbe", throwOnError: true);
            var dispatch = type.GetMethod("Dispatch", BindingFlags.Public | BindingFlags.Instance);
            var probe = Activator.CreateInstance(type);
            Assert.Equal(1, dispatch.Invoke(probe, new object[] { false, false }));
            Assert.Equal(0, dispatch.Invoke(probe, new object[] { true, false }));
            if (includeManagerReplaySuppression)
                Assert.Equal(0, dispatch.Invoke(probe, new object[] { false, true }));
        }
    }
}
