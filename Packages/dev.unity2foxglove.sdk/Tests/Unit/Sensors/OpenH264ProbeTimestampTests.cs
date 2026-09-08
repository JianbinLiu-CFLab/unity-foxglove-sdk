// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class OpenH264ProbeTimestampTests
    {
        [Fact]
        public void ExperimentalProbeCarriesCaptureTimestampThroughSidecarAndPublisher()
        {
            var sidecar = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            var publisher = Read("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");

            Assert.Contains("ConcurrentQueue<ProbeInputFrame>", sidecar, StringComparison.Ordinal);
            Assert.Contains("TrySubmitFrame(byte[] i420Frame, ulong timestampNs)", sidecar, StringComparison.Ordinal);
            Assert.Contains("_encodedFrameTimestamps.Enqueue(frame.TimestampNs)", sidecar, StringComparison.Ordinal);
            Assert.Contains("TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit", sidecar, StringComparison.Ordinal);
            Assert.Contains("var renderUnixNs = CurrentLogTimeNs;", publisher, StringComparison.Ordinal);
            Assert.Contains("i420Bytes, renderUnixNs", publisher, StringComparison.Ordinal);
            Assert.Contains("TrySubmitFrame(_i420Buffer, renderUnixNs)", publisher, StringComparison.Ordinal);
            Assert.Contains("TryDequeueEncodedAccessUnit(out EncodedVideoAccessUnit timestampedAccessUnit)", publisher, StringComparison.Ordinal);
            Assert.Contains("var unixNs = timestampedAccessUnit.TimestampNs;", publisher, StringComparison.Ordinal);
        }

        private static string Read(string relativePath)
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }

            throw new DirectoryNotFoundException("Could not locate repository file " + relativePath);
        }
    }
}
