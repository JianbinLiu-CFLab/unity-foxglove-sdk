// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: OpenH264 helper protocol timestamp behavior.

using System.Reflection;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140-33")]
    [Trait("Domain", "Sensors")]
    public sealed class OpenH264EncoderSidecarTests
    {
        [Fact]
        public void EmptyAccessUnitIsRejectedWithoutConsumingTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            Assert.Throws<System.ArgumentException>(() => sidecar.AcceptHelperAccessUnit(System.Array.Empty<byte>()));
            sidecar.AcceptHelperAccessUnit(new byte[] { 1, 2, 3 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(100UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 1, 2, 3 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void SkipSentinelConsumesOnlySkippedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();
            sidecar.EnqueueTimestampForTests(100UL);
            sidecar.EnqueueTimestampForTests(200UL);

            sidecar.AcceptHelperSkippedAccessUnit();
            sidecar.AcceptHelperAccessUnit(new byte[] { 4, 5, 6 });

            Assert.Equal(1, sidecar.SkippedAccessUnits);
            Assert.Contains("skipped", sidecar.LastDiagnosticLine);
            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(200UL, accessUnit.TimestampNs);
            Assert.Equal(new byte[] { 4, 5, 6 }, accessUnit.Data);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Fact]
        public void FullOutputQueueConsumesDroppedTimestamp()
        {
            var sidecar = new OpenH264EncoderSidecar();

            for (var i = 1; i <= 5; i++)
            {
                sidecar.EnqueueTimestampForTests((ulong)i * 100UL);
                sidecar.AcceptHelperAccessUnit(new[] { (byte)i });
            }

            Assert.Equal(1, sidecar.DroppedOutputFrames);
            Assert.Equal(4, sidecar.OutputQueueDepth);
            Assert.Contains("output queue full", sidecar.LastDiagnosticLine);

            for (var i = 1; i <= 4; i++)
            {
                Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var queued));
                Assert.Equal((ulong)i * 100UL, queued.TimestampNs);
            }

            sidecar.EnqueueTimestampForTests(600UL);
            sidecar.AcceptHelperAccessUnit(new byte[] { 6 });

            Assert.True(sidecar.TryDequeueEncodedAccessUnit(out var accessUnit));
            Assert.Equal(600UL, accessUnit.TimestampNs);
            Assert.False(sidecar.TryDequeueEncodedAccessUnit(out _));
        }

        [Theory]
        [InlineData(@"C:\OpenH264\openh264.dll", "\"C:\\OpenH264\\openh264.dll\"")]
        [InlineData(@"C:\OpenH264 Runtime\", "\"C:\\OpenH264 Runtime\\\\\"")]
        [InlineData("C:\\OpenH264\\quoted\"name.dll", "\"C:\\OpenH264\\quoted\\\"name.dll\"")]
        public void OpenH264ArgumentsUseWindowsCommandLineEscaping(string value, string expected)
        {
            Assert.Equal(expected, QuoteArgument(value));
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryClampsGeometryAndRate()
        {
            var h264 = (FfmpegH264EncoderOptions)CreateOptions(
                "CreateH264Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var h265 = (FfmpegH265EncoderOptions)CreateOptions(
                "CreateH265Options",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var openH264 = (OpenH264EncoderOptions)CreateOptions(
                "CreateOpenH264Options",
                "",
                "",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);
            var mediaFoundation = (MediaFoundationH264EncoderOptions)CreateOptions(
                "CreateMediaFoundationH264Options",
                0,
                -1,
                0,
                -2,
                -3,
                0,
                -4);

            AssertPositiveVideoOptions(h264);
            AssertPositiveVideoOptions(h265);
            AssertPositiveVideoOptions(openH264);
            AssertPositiveVideoOptions(mediaFoundation);
        }

        [Fact]
        public void CameraVideoSidecarOptionsFactoryRoundsHalfFrameRatesAwayFromZero()
        {
            Assert.Equal(31, CameraVideoSidecarConfigFactory.ResolveFrameRate(30.5f));
        }

        [Fact]
        public void OpenH264TimestampTestSeamAvoidsPrivateFieldReflection()
        {
            var sidecar = new OpenH264EncoderSidecar();

            sidecar.EnqueueTimestampForTests(123UL);

            Assert.Equal(1, sidecar.PendingTimestampCountForTests);
        }

        private static string QuoteArgument(string value)
        {
            var method = typeof(OpenH264EncoderOptions).GetMethod(
                "QuoteArgument",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (string)method.Invoke(null, new object[] { value });
        }

        private static object CreateOptions(string methodName, params object[] args)
        {
            var factory = typeof(FfmpegH264EncoderOptions).Assembly.GetType(
                "Foxglove.Schemas.Video.CameraVideoSidecarOptionsFactory");
            Assert.NotNull(factory);
            var method = factory.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(method);
            return method.Invoke(null, args);
        }

        private static void AssertPositiveVideoOptions(object options)
        {
            Assert.Equal(1, IntProperty(options, "Width"));
            Assert.Equal(1, IntProperty(options, "Height"));
            Assert.Equal(1, IntProperty(options, "FrameRate"));
            Assert.Equal(1, IntProperty(options, "BitrateKbps"));
            Assert.Equal(1, IntProperty(options, "KeyframeInterval"));
            Assert.Equal(1, IntProperty(options, "MaxInputQueue"));
            Assert.Equal(1, IntProperty(options, "MaxOutputQueue"));
        }

        private static int IntProperty(object target, string name)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(field);
            return (int)field.GetValue(target);
        }
    }
}
