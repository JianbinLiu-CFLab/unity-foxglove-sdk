// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed class RemoteMcapHttpRouterParsingTests
    {
        [Theory]
        [InlineData("bytes=10-19", 100L, 10L, 19L)]
        [InlineData("bytes=95-", 100L, 95L, 99L)]
        [InlineData("bytes=-8", 100L, 92L, 99L)]
        [InlineData("bytes=98-1000", 100L, 98L, 99L)]
        public void ByteRangeParserAcceptsSingleNormalizedRange(string header, long length, long expectedStart, long expectedEnd)
        {
            var valid = TryParseByteRange(header, length, out var start, out var end, out var problem);

            Assert.True(valid, problem);
            Assert.Equal(expectedStart, start);
            Assert.Equal(expectedEnd, end);
        }

        [Theory]
        [InlineData("items=0-1")]
        [InlineData("bytes=0-1,4-5")]
        [InlineData("bytes=-0")]
        [InlineData("bytes=100-101")]
        public void ByteRangeParserRejectsUnsupportedOrUnsatisfiableRange(string header)
        {
            var valid = TryParseByteRange(header, 100L, out _, out _, out var problem);

            Assert.False(valid);
            Assert.NotEmpty(problem);
        }

        [Theory]
        [InlineData("1970-01-01T00:00:01.25Z", 1_250_000_000UL)]
        [InlineData("2026-06-05T12:00:00Z", 1_780_660_800_000_000_000UL)]
        public void IsoUtcParserPreservesWholeAndFractionalNanoseconds(string value, ulong expectedNanoseconds)
        {
            var valid = TryParseIsoUtcNs(value, out var nanoseconds);

            Assert.True(valid);
            Assert.Equal(expectedNanoseconds, nanoseconds);
        }

        [Theory]
        [InlineData("1970-01-01T00:00:01")]
        [InlineData("1970-01-01T00:00:01.1234567890Z")]
        [InlineData("1970-01-01T00:00:01.invalidZ")]
        [InlineData("1969-12-31T23:59:59Z")]
        public void IsoUtcParserRejectsMalformedOrPreEpochValues(string value)
        {
            var valid = TryParseIsoUtcNs(value, out var nanoseconds);

            Assert.False(valid);
            Assert.Equal(0UL, nanoseconds);
        }

        private static bool TryParseByteRange(string header, long length, out long start, out long end, out string problem)
        {
            var method = typeof(RemoteMcapHttpRouter).GetMethod(
                "TryParseByteRange",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var arguments = new object[] { header, length, 0L, 0L, string.Empty };
            var valid = (bool)method.Invoke(null, arguments);
            start = (long)arguments[2];
            end = (long)arguments[3];
            problem = (string)arguments[4];
            return valid;
        }

        private static bool TryParseIsoUtcNs(string value, out ulong nanoseconds)
        {
            var method = typeof(RemoteMcapHttpRouter).GetMethod(
                "TryParseIsoUtcNs",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var arguments = new object[] { value, 0UL };
            var valid = (bool)method.Invoke(null, arguments);
            nanoseconds = (ulong)arguments[1];
            return valid;
        }
    }
}
