// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Shared MCAP wire constants used by both readers and writers.

using System;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapConstants
    {
        internal static readonly byte[] MagicBytes =
            { 0x89, (byte)'M', (byte)'C', (byte)'A', (byte)'P', 0x30, 0x0D, 0x0A };

        internal const int MagicLength = 8;

        internal static ReadOnlySpan<byte> MagicSpan => MagicBytes;

        internal static byte[] MagicCopy() => (byte[])MagicBytes.Clone();
    }
}
