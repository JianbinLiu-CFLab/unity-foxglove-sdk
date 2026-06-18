// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Internal trailer offsets used by post-recording MCAP amendment.

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapTrailerInfo
    {
        public ulong FooterOffset;
        public ulong SummaryStart;
        public ulong SummaryOffsetStart;
        public uint SummaryCrc;
        public ulong DataEndOffset;
        public ulong DataEndEndOffset;
    }
}
