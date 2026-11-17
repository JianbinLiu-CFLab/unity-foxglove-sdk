using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    internal sealed class McapHistoryChunkOrderCache
    {
        private List<int> _ordered;

        internal List<int> Get(McapFileSummary summary)
        {
            if (_ordered != null)
                return _ordered;

            var ordered = new List<int>(summary.ChunkIndexes.Count);
            for (var i = 0; i < summary.ChunkIndexes.Count; i++)
                ordered.Add(i);
            ordered.Sort((leftNumber, rightNumber) =>
            {
                var left = summary.ChunkIndexes[leftNumber];
                var right = summary.ChunkIndexes[rightNumber];
                var compare = right.MessageEndTime.CompareTo(left.MessageEndTime);
                if (compare != 0)
                    return compare;
                compare = right.MessageStartTime.CompareTo(left.MessageStartTime);
                return compare != 0
                    ? compare
                    : right.ChunkStartOffset.CompareTo(left.ChunkStartOffset);
            });
            return _ordered = ordered;
        }

        internal void Clear() => _ordered = null;
    }
}
