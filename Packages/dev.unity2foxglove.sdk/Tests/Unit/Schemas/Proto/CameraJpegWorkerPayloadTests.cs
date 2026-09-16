using System;
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
    }
}
