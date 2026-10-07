// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace FoxgloveSdk.UnitTests.Mcap
{
    public sealed partial class RemoteMcapHttpServerBoundaryTests
    {
        private const long SlowResponseLength = 64L * 1024L * 1024L;

        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData("127.42.1.9", true)]
        [InlineData("::1", true)]
        [InlineData("[::1]", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("", true)]
        [InlineData("   ", true)]
        [InlineData("localhost", false)]
        [InlineData("0.0.0.0", false)]
        [InlineData("::", false)]
        [InlineData("+", false)]
        [InlineData("*", false)]
        [InlineData("192.0.2.1", false)]
        public void LoopbackClassificationRejectsExternallyReachableHosts(
            string host,
            bool expected)
        {
            Assert.Equal(expected, RemoteMcapHttpOptions.IsLoopbackHost(host));
        }

        [Fact]
        public void Ipv6BaseUrlUsesUriBrackets()
        {
            var options = new RemoteMcapHttpOptions
            {
                Host = "::1",
                Port = 8891
            };

            Assert.Equal("http://[::1]:8891", options.BaseUrl);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void NonLoopbackStartWithoutBearerTokenFailsBeforeBinding(
            string token)
        {
            var path = Path.GetTempFileName();
            RemoteMcapHttpServer server = null;
            try
            {
                var options = new RemoteMcapHttpOptions
                {
                    Host = "0.0.0.0",
                    Port = 1,
                    McapPath = path,
                    RequiredBearerToken = token
                };

                var error = Record.Exception(() =>
                    server = RemoteMcapHttpServer.Start(options));

                var argument = Assert.IsType<ArgumentException>(error);
                Assert.Equal("options", argument.ParamName);
                Assert.Contains("non-loopback", argument.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("bearer token", argument.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                server?.Dispose();
                File.Delete(path);
            }
        }

        [Fact]
        public void UnauthorizedMultiSourceRequestsDoNotExposeCapabilityChecks()
        {
            var source = new RemoteMcapDataSourcePrototype(
                "missing.mcap",
                "protected",
                "Protected",
                "secret");
            var request = new RemoteMcapRequest
            {
                BearerToken = "wrong",
                RequestMultipleSources = true
            };

            var manifest = source.GetManifest(request);
            source.GetManifestBytes(request, out var manifestBytesError);
            var data = source.GetData(request);
            using var dataStream = source.GetDataStream(request);
            using var directStream = source.GetDirectFileStream(request);

            Assert.Equal(RemoteMcapResponseStatus.Unauthorized, manifest.Status);
            Assert.Equal(
                RemoteMcapResponseStatus.Unauthorized,
                manifestBytesError.Status);
            Assert.Equal(RemoteMcapResponseStatus.Unauthorized, data.Status);
            Assert.Equal(RemoteMcapResponseStatus.Unauthorized, dataStream.Status);
            Assert.Equal(RemoteMcapResponseStatus.Unauthorized, directStream.Status);
            Assert.False(manifest.Authorization.Allowed);
            Assert.False(manifestBytesError.Authorization.Allowed);
            Assert.False(data.Authorization.Allowed);
            Assert.False(dataStream.Authorization.Allowed);
            Assert.False(directStream.Authorization.Allowed);
        }

        [Theory]
        [InlineData((short)2, 32, 24, 123456789L)]
        [InlineData((short)3, 48, 40, 987654321L)]
        public void WindowsUsnParserReadsV2AndV3Records(
            short majorVersion,
            int length,
            int expectedOffset,
            long expectedUsn)
        {
            var record = new byte[length];
            record[4] = (byte)majorVersion;
            record[5] = (byte)(majorVersion >> 8);
            for (var index = 0; index < sizeof(long); index++)
                record[expectedOffset + index] = (byte)(expectedUsn >> (index * 8));

            Assert.True(RemoteMcapDataSourcePrototype.TryGetWindowsUsnOffset(
                majorVersion,
                (uint)record.Length,
                out var usnOffset));
            Assert.Equal(expectedOffset, usnOffset);
            Assert.True(RemoteMcapDataSourcePrototype.TryParseWindowsUsnRecord(
                record,
                out var usn));
            Assert.Equal(expectedUsn, usn);

            var nativeRecord = Marshal.AllocHGlobal(record.Length);
            try
            {
                Marshal.Copy(record, 0, nativeRecord, record.Length);
                Assert.True(RemoteMcapDataSourcePrototype.TryParseWindowsUsnRecord(
                    nativeRecord,
                    (uint)record.Length,
                    out var nativeUsn));
                Assert.Equal(expectedUsn, nativeUsn);
            }
            finally
            {
                Marshal.FreeHGlobal(nativeRecord);
            }
        }

        [Fact]
        public void WindowsUsnParserRejectsUnsupportedOrTruncatedRecords()
        {
            var unsupported = new byte[48];
            unsupported[4] = 4;
            Assert.False(RemoteMcapDataSourcePrototype.TryParseWindowsUsnRecord(
                unsupported,
                out _));

            var truncated = new byte[47];
            truncated[4] = 3;
            Assert.False(RemoteMcapDataSourcePrototype.TryParseWindowsUsnRecord(
                truncated,
                out _));
        }

        [Fact]
        public void SourceIdentityChangesWhenTheRecordingGenerationChanges()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1));
                var source = new RemoteMcapDataSourcePrototype(path, "generation", "Generation", string.Empty);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                File.WriteAllBytes(path, BuildRemoteMcap(2));
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.NotEqual(first, second);
                Assert.Equal(RemoteMcapResponseStatus.NotFound,
                    source.GetData(new RemoteMcapRequest { SourceId = first }).Status);
                Assert.Equal(RemoteMcapResponseStatus.Ok,
                    source.GetData(new RemoteMcapRequest { SourceId = second }).Status);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityChangesWhenSameLengthContentIsReplacedWithSameTimestamp()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-same-stamp-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                var firstBytes = BuildRemoteMcap(1, 0);
                var secondBytes = BuildRemoteMcap(1, 1);
                File.WriteAllBytes(path, firstBytes);
                var stamp = File.GetLastWriteTimeUtc(path);
                var source = new RemoteMcapDataSourcePrototype(path, "same-stamp", "Same stamp", string.Empty);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                File.WriteAllBytes(path, secondBytes);
                File.SetLastWriteTimeUtc(path, stamp);
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.Equal(firstBytes.Length, secondBytes.Length);
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
                Assert.NotEqual(first, second);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityChangesWhenOnlyMiddleBytesChangeWithSameLengthAndTimestamp()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-middle-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                var firstBytes = BuildRemoteMcap(1, 0, 16 * 1024);
                var secondBytes = (byte[])firstBytes.Clone();
                secondBytes[8 * 1024] ^= 1;
                File.WriteAllBytes(path, firstBytes);
                var stamp = File.GetLastWriteTimeUtc(path);
                var source = new RemoteMcapDataSourcePrototype(path, "middle-only", "Middle only", string.Empty);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                File.WriteAllBytes(path, secondBytes);
                File.SetLastWriteTimeUtc(path, stamp);
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.Equal(firstBytes.Length, secondBytes.Length);
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
                Assert.NotEqual(first, second);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityChangesWhenAnUnsampledByteChangesWithSameLengthAndTimestamp()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-unsampled-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                var firstBytes = BuildRemoteMcap(1, 0, 64 * 1024);
                var secondBytes = (byte[])firstBytes.Clone();
                secondBytes[secondBytes.Length / 4] ^= 1;
                File.WriteAllBytes(path, firstBytes);
                var stamp = File.GetLastWriteTimeUtc(path);
                var source = new RemoteMcapDataSourcePrototype(path, "unsampled", "Unsampled", string.Empty);
                var firstManifest = source.GetManifest(new RemoteMcapRequest()).Manifest;
                var first = firstManifest.Sources[0].Id;

                File.WriteAllBytes(path, secondBytes);
                File.SetLastWriteTimeUtc(path, stamp);
                var secondManifest = source.GetManifest(new RemoteMcapRequest()).Manifest;
                var second = secondManifest.Sources[0].Id;

                Assert.Equal(firstBytes.Length, secondBytes.Length);
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
                Assert.NotEqual(first, second);
                Assert.NotEqual(firstManifest.Sources[0].DataUrl, secondManifest.Sources[0].DataUrl);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void SourceIdentityReusesFullHashWhenFileChangeIdentityIsStable()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-generation-cache-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1, 0, 64 * 1024));
                var source = new RemoteMcapDataSourcePrototype(path, "cached", "Cached", string.Empty);

                var counter = typeof(RemoteMcapDataSourcePrototype).GetField(
                    "_fullContentHashComputations",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(counter);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;
                var firstHashCount = (int)counter.GetValue(source);
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                Assert.Equal(first, second);
                var secondHashCount = (int)counter.GetValue(source);
                var observedToken = typeof(RemoteMcapDataSourcePrototype).GetField(
                    "_hasObservedStamp",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(observedToken);
                Assert.True(firstHashCount > 0);
                if ((bool)observedToken.GetValue(source))
                    Assert.Equal(firstHashCount, secondHashCount);
                else
                    Assert.True(secondHashCount > firstHashCount);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

        [Fact]
        public void DataRequestsReuseOneSourceStampPerRequest()
        {
            var path = Path.Combine(Path.GetTempPath(), "remote-source-stamp-request-" + Guid.NewGuid().ToString("N") + ".mcap");
            try
            {
                File.WriteAllBytes(path, BuildRemoteMcap(1));
                var generationCalls = 0;
                var source = new RemoteMcapDataSourcePrototype(
                    path,
                    "request",
                    "Request",
                    string.Empty,
                    generationVersionProvider: () =>
                    {
                        Interlocked.Increment(ref generationCalls);
                        return "stable";
                    });
                var sourceId = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;

                generationCalls = 0;
                var data = source.GetData(new RemoteMcapRequest { SourceId = sourceId });
                Assert.Equal(RemoteMcapResponseStatus.Ok, data.Status);
                Assert.Equal(1, generationCalls);

                generationCalls = 0;
                using (var dataStream = source.GetDataStream(new RemoteMcapRequest { SourceId = sourceId }))
                    Assert.Equal(RemoteMcapResponseStatus.Ok, dataStream.Status);
                Assert.Equal(1, generationCalls);

                generationCalls = 0;
                using (var directStream = source.GetDirectFileStream(new RemoteMcapRequest { SourceId = sourceId }))
                    Assert.Equal(RemoteMcapResponseStatus.Ok, directStream.Status);
                Assert.Equal(1, generationCalls);
            }
            finally
            {
                DeleteTempFileWithRetry(path);
            }
        }

    }
}
