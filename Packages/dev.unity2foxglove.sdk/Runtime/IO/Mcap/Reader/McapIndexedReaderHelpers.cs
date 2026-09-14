// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Compatibility facade for non-latest streaming readers. Latest-at policy lives in
// McapLatestAtQuery and is consumed directly by McapIndexedReader and replay.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    internal static class McapIndexedReaderHelpers
    {
        internal static List<McapChunkIndex> OrderChunkIndexesByDescendingEndTime(IReadOnlyList<McapChunkIndex> x) => McapLatestAtQuery.OrderChunkIndexesByDescendingEndTime(x);
        internal static void ConsiderLatestCandidate(McapMessage m, McapReadOptions o, HashSet<ushort> s, Dictionary<ushort, McapMessage> l) => McapLatestAtQuery.ConsiderLatestCandidate(m,o,s,l);
        internal static bool CanStopLatestScan(Dictionary<ushort,McapMessage> l,int e,ulong t) => McapLatestAtQuery.CanStopLatestScan(l,e,t);
        internal static bool ContainsAnySelectedChannel(Dictionary<ushort,ulong> i,HashSet<ushort> s) => McapLatestAtQuery.ContainsAnySelectedChannel(i,s);
        internal static int CompareMessages(McapMessage l,McapMessage r) => McapLatestAtQuery.CompareMessages(l,r);
        internal static int CompareLatestCandidate(McapMessage l,McapMessage r) => McapLatestAtQuery.CompareLatestCandidate(l,r);
        internal static int CompareLatestOutput(McapMessage l,McapMessage r) => McapLatestAtQuery.CompareLatestOutput(l,r);
        internal static bool IsInTimeRange(ulong t,McapReadOptions o) => McapLatestAtQuery.IsInTimeRange(t,o);
        internal static McapReadOptions CreateLazyReadOptions(McapReadOptions s) => McapLatestAtQuery.CreateLazyReadOptions(s);
        internal static bool IsAtOrPastEnd(ulong t,McapReadOptions o) => McapLatestAtQuery.IsAtOrPastEnd(t,o);
        internal static void ApplyOrderingAndLimit(List<McapMessage> r,McapReadOptions o) => McapLatestAtQuery.ApplyOrderingAndLimit(r,o);
        internal static bool TryAddBoundedMessage(List<McapMessage> r,McapMessage m,McapReadOptions o,out McapMessage e) => McapLatestAtQuery.TryAddBoundedMessage(r,m,o,out e);
    }
}
