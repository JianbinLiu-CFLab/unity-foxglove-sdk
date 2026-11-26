// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Mcap
// Purpose: Reject chunk inner record lengths that do not fit in int instead of truncating them.

using System;
using System.IO;
using Unity.FoxgloveSDK.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Mcap
{
    /// <summary>Rejects chunk inner record lengths that do not fit in int.</summary>
    public sealed class McapChunkRecordLengthTests
    {
        [Fact]
        /// <summary>A length whose low 32 bits fit the buffer must still be rejected.</summary>
        public void OversizedInnerRecordLengthIsRejectedInsteadOfTruncated()
        {
            var records = new byte[10];
            records[0] = 0x07;
            BitConverter.TryWriteBytes(new Span<byte>(records, 1, 8), 0x1_0000_0001UL);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(records, 1, 8);

            var exception = Assert.Throws<InvalidDataException>(
                () => McapChunkReader.ReadMessages(records));
            Assert.Contains("exceeds int.MaxValue", exception.Message, StringComparison.Ordinal);
        }
    }
}
