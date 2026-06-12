// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140D RGB24-to-NV12 conversion behavior and allocation checks.

using System;
using System.IO;
using Foxglove.Schemas.Video;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    [Trait("Phase", "140D")]
    [Trait("Domain", "Sensors")]
    public sealed class Rgb24ToNv12ConverterTests
    {
        [Fact]
        public void ConverterUsesBlockWalkWithoutSeparateLumaPass()
        {
            var source = Text("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/Rgb24ToNv12Converter.cs");

            Assert.DoesNotContain("for (var y = 0; y < height; y++)", source, StringComparison.Ordinal);
            Assert.Contains("for (var y = 0; y < height; y += 2)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void ConversionMatchesLegacyMediaFoundationOutput()
        {
            AssertMatchesLegacy(width: 2, height: 2, seed: 29, flipVertical: true);
            AssertMatchesLegacy(width: 4, height: 2, seed: 97, flipVertical: true);
            AssertMatchesLegacy(width: 4, height: 4, seed: 221, flipVertical: true);
            AssertMatchesLegacy(width: 4, height: 4, seed: 221, flipVertical: false);
        }

        [Fact]
        public void ConversionDoesNotAllocateAfterWarmup()
        {
            const int width = 16;
            const int height = 8;
            var rgb24 = MakeFrame(width, height, seed: 177);
            var nv12 = new byte[width * height * 3 / 2];

            Assert.True(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(rgb24, width, height, nv12, flipVertical: true, out var error), error);

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
                Assert.True(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(rgb24, width, height, nv12, (i & 1) == 0, out error), error);
            var after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(before, after);
        }

        [Fact]
        public void InvalidInputsReturnFalseWithError()
        {
            Assert.False(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(new byte[3 * 3 * 3], 3, 3, new byte[14], true, out var error));
            Assert.Equal("RGB24-to-NV12 conversion requires positive even dimensions.", error);

            Assert.False(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(new byte[3], 2, 2, new byte[6], true, out error));
            Assert.Equal("RGB24 input buffer length does not match width * height * 3.", error);

            Assert.False(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(new byte[12], 2, 2, new byte[5], true, out error));
            Assert.Equal("NV12 output buffer length is smaller than width * height * 3 / 2.", error);
        }

        private static void AssertMatchesLegacy(int width, int height, int seed, bool flipVertical)
        {
            var rgb24 = MakeFrame(width, height, seed);
            var expected = new byte[width * height * 3 / 2];
            var actual = new byte[expected.Length];

            LegacyConvert(rgb24, width, height, expected, flipVertical);

            Assert.True(Rgb24ToNv12Converter.TryConvertRgb24ToNv12(rgb24, width, height, actual, flipVertical, out var error), error);
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

        private static void LegacyConvert(byte[] rgb24Frame, int width, int height, byte[] nv12, bool flipVertical)
        {
            var yPlaneLength = checked(width * height);

            for (var y = 0; y < height; y++)
            {
                var sourceY = flipVertical ? height - 1 - y : y;
                for (var x = 0; x < width; x++)
                {
                    var rgb = (sourceY * width + x) * 3;
                    var r = rgb24Frame[rgb];
                    var g = rgb24Frame[rgb + 1];
                    var b = rgb24Frame[rgb + 2];
                    nv12[y * width + x] = ToLuma(r, g, b);
                }
            }

            var uvOffset = yPlaneLength;
            for (var y = 0; y < height; y += 2)
            {
                var sourceY0 = flipVertical ? height - 1 - y : y;
                var sourceY1 = flipVertical ? sourceY0 - 1 : y + 1;
                for (var x = 0; x < width; x += 2)
                {
                    var u = 0;
                    var v = 0;
                    AccumulateChroma(rgb24Frame, width, sourceY0, x, ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY0, x + 1, ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY1, x, ref u, ref v);
                    AccumulateChroma(rgb24Frame, width, sourceY1, x + 1, ref u, ref v);
                    var uv = uvOffset + (y / 2) * width + x;
                    nv12[uv] = ClampByte(u / 4);
                    nv12[uv + 1] = ClampByte(v / 4);
                }
            }
        }

        private static void AccumulateChroma(byte[] rgb24Frame, int width, int y, int x, ref int u, ref int v)
        {
            var rgb = (y * width + x) * 3;
            var r = rgb24Frame[rgb];
            var g = rgb24Frame[rgb + 1];
            var b = rgb24Frame[rgb + 2];
            u += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
            v += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        }

        private static byte ToLuma(byte r, byte g, byte b)
            => ClampByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);

        private static byte ClampByte(int value)
            => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;

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
