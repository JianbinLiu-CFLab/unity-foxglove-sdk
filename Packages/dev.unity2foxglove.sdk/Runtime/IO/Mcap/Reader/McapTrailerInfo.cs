// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap
// Purpose: Internal trailer offsets used by post-recording MCAP amendment.

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapTrailerInfo
    {
        internal ulong FooterOffset;
        internal ulong SummaryStart;
        internal ulong SummaryOffsetStart;
        internal uint SummaryCrc;
        internal ulong DataEndOffset;
        internal ulong DataEndEndOffset;
        internal uint DataSectionCrc;
    }
}
