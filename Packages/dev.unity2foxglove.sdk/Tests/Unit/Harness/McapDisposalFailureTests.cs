// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: MCAP disposal terminal-state regression coverage.

using System.IO;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests
{
    [Trait("Phase", "173-089")]
    [Trait("Domain", "Review")]
    public sealed class McapDisposalFailureTests
    {
        [Fact]
        public void McapWriterFinalizesAfterStreamDisposeFailure()
        {
            var stream = new DisposeThrowingStream();
            var writer = new McapWriter(stream, leaveOpen: false);

            writer.Dispose();
            writer.Dispose();

            Assert.Equal(1, stream.FlushAttempts);
            Assert.Equal(1, stream.DisposeAttempts);
        }

        [Fact]
        public void McapWriterFinalizesAfterPersistentFlushFailure()
        {
            var stream = new FlushThrowingStream();
            var writer = new McapWriter(stream, leaveOpen: false);

            writer.Dispose();

            Assert.Equal(2, stream.FlushAttempts);
            Assert.Equal(1, stream.DisposeAttempts);
            Assert.True(writer.IsDisposed);
        }

        [Fact]
        public void McapRecorderFinalizesAfterPersistentFlushFailure()
        {
            var stream = new FlushThrowingStream();
            var recorder = new McapRecorder(stream, leaveOpen: false);

            recorder.Dispose();
            recorder.Dispose();

            Assert.Equal(1, stream.DisposeAttempts);
        }

        private sealed class DisposeThrowingStream : MemoryStream
        {
            public int FlushAttempts { get; private set; }
            public int DisposeAttempts { get; private set; }

            public override void Flush()
            {
                FlushAttempts++;
                base.Flush();
            }

            protected override void Dispose(bool disposing)
            {
                DisposeAttempts++;
                throw new IOException("persistent stream dispose failure");
            }
        }

        private sealed class FlushThrowingStream : MemoryStream
        {
            public int FlushAttempts { get; private set; }
            public int DisposeAttempts { get; private set; }

            public override void Flush()
            {
                FlushAttempts++;
                throw new IOException("persistent stream flush failure");
            }

            protected override void Dispose(bool disposing)
            {
                DisposeAttempts++;
                base.Dispose(disposing);
            }
        }
    }
}
