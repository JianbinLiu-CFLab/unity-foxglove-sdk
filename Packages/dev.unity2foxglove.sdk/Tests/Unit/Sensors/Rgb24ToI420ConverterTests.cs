// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140D RGB24-to-I420 conversion behavior and allocation checks.

using System;
using System.IO;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140D")]
    [Trait("Domain", "Sensors")]
    public sealed class Rgb24ToI420ConverterTests
    {
        [Fact]
        public void ConverterUsesBlockWalkWithoutSeparateLumaPass()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/Rgb24ToI420Converter.cs");

            Assert.DoesNotContain("for (var y = 0; y < height; y++)", source, StringComparison.Ordinal);
            Assert.Contains("for (var y = 0; y < height; y += 2)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ConversionMatchesLegacyOutputForRepresentativeFrames()
        {
            AssertMatchesLegacy(width: 2, height: 2, seed: 17, flipVertical: false);
            AssertMatchesLegacy(width: 2, height: 2, seed: 17, flipVertical: true);
            AssertMatchesLegacy(width: 4, height: 2, seed: 91, flipVertical: false);
            AssertMatchesLegacy(width: 4, height: 2, seed: 91, flipVertical: true);
            AssertMatchesLegacy(width: 4, height: 4, seed: 203, flipVertical: false);
            AssertMatchesLegacy(width: 4, height: 4, seed: 203, flipVertical: true);
        }

        [Fact]
        public void ConversionDoesNotAllocateAfterWarmup()
        {
            const int width = 16;
            const int height = 8;
            var rgb24 = MakeFrame(width, height, seed: 151);
            var i420 = new byte[width * height * 3 / 2];

            for (var i = 0; i < 1000; i++)
                Assert.True(Rgb24ToI420Converter.TryConvertRgb24ToI420(rgb24, width, height, i420, (i & 1) == 0, out var error), error);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                if (!Rgb24ToI420Converter.TryConvertRgb24ToI420(rgb24, width, height, i420, (i & 1) == 0, out var error))
                    throw new InvalidOperationException(error);
            }

            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(before, after);
        }

        [Fact]
        public void InvalidInputsReturnFalseWithError()
        {
            Assert.False(Rgb24ToI420Converter.TryConvertRgb24ToI420(new byte[3 * 3 * 3], 3, 3, new byte[14], false, out var error));
            Assert.Equal("RGB24-to-I420 conversion requires positive even dimensions.", error);

            Assert.False(Rgb24ToI420Converter.TryConvertRgb24ToI420(new byte[3], 2, 2, new byte[6], false, out error));
            Assert.Equal("RGB24 input buffer length does not match width * height * 3.", error);

            Assert.False(Rgb24ToI420Converter.TryConvertRgb24ToI420(new byte[12], 2, 2, new byte[5], false, out error));
            Assert.Equal("I420 output buffer length does not match width * height * 3 / 2.", error);
        }

        private static void AssertMatchesLegacy(int width, int height, int seed, bool flipVertical)
        {
            var rgb24 = MakeFrame(width, height, seed);
            var expected = new byte[width * height * 3 / 2];
            var actual = new byte[expected.Length];

            LegacyConvert(rgb24, width, height, expected, flipVertical);

            Assert.True(Rgb24ToI420Converter.TryConvertRgb24ToI420(rgb24, width, height, actual, flipVertical, out var error), error);
            Assert.Equal(expected, actual);
        }

        private static byte[] MakeFrame(int width, int height, int seed)
        {
            var bytes = new byte[width * height * 3];
            var value = seed;
            for (var i = 0; i < bytes.Length; i++)
            {
                value = unchecked(value * 1103515245 + 12345);
                bytes[i] = (byte)(value >> 16);
            }

            return bytes;
        }

        private static void LegacyConvert(byte[] rgb24, int width, int height, byte[] i420, bool flipVertical)
        {
            var yOffset = 0;
            var uOffset = width * height;
            var vOffset = uOffset + (width * height / 4);

            for (var y = 0; y < height; y++)
            {
                var rowBase = LegacyRgbRowBase(y, width, height, flipVertical);
                for (var x = 0; x < width; x++)
                {
                    var rgbIndex = rowBase + x * 3;
                    var r = rgb24[rgbIndex];
                    var g = rgb24[rgbIndex + 1];
                    var b = rgb24[rgbIndex + 2];
                    i420[yOffset + y * width + x] = ComputeY(r, g, b);
                }
            }

            for (var y = 0; y < height; y += 2)
            {
                var rowBase0 = LegacyRgbRowBase(y, width, height, flipVertical);
                var rowBase1 = LegacyRgbRowBase(y + 1, width, height, flipVertical);
                for (var x = 0; x < width; x += 2)
                {
                    var rSum = 0;
                    var gSum = 0;
                    var bSum = 0;
                    var rgbIndex = rowBase0 + x * 3;
                    rSum += rgb24[rgbIndex];
                    gSum += rgb24[rgbIndex + 1];
                    bSum += rgb24[rgbIndex + 2];

                    rgbIndex += 3;
                    rSum += rgb24[rgbIndex];
                    gSum += rgb24[rgbIndex + 1];
                    bSum += rgb24[rgbIndex + 2];

                    rgbIndex = rowBase1 + x * 3;
                    rSum += rgb24[rgbIndex];
                    gSum += rgb24[rgbIndex + 1];
                    bSum += rgb24[rgbIndex + 2];

                    rgbIndex += 3;
                    rSum += rgb24[rgbIndex];
                    gSum += rgb24[rgbIndex + 1];
                    bSum += rgb24[rgbIndex + 2];

                    var rAvg = rSum / 4;
                    var gAvg = gSum / 4;
                    var bAvg = bSum / 4;
                    var chromaIndex = (y / 2) * (width / 2) + (x / 2);
                    i420[uOffset + chromaIndex] = ComputeU(rAvg, gAvg, bAvg);
                    i420[vOffset + chromaIndex] = ComputeV(rAvg, gAvg, bAvg);
                }
            }
        }

        private static int LegacyRgbRowBase(int y, int width, int height, bool flipVertical)
            => (flipVertical ? height - 1 - y : y) * width * 3;

        private static byte ComputeY(int r, int g, int b)
            => ClampToByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static byte ComputeU(int r, int g, int b)
            => ClampToByte(((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128);

        private static byte ComputeV(int r, int g, int b)
            => ClampToByte(((112 * r - 94 * g - 18 * b + 128) >> 8) + 128);

        private static byte ClampToByte(int value)
        {
            if (value < 0)
                return 0;
            if (value > 255)
                return 255;
            return (byte)value;
        }

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
