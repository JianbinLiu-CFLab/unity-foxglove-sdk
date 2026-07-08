// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>
    /// Head-indexed pending replay queue that avoids O(n) front removal.
    /// </summary>
    internal sealed class McapReplayPendingQueue
    {
        private readonly List<McapMessage> _messages = new();
        private int _headIndex;
        private bool _isSorted = true;

        internal int Count => _messages.Count - _headIndex;
        internal int DebugHeadIndex => _headIndex;

        internal void Clear()
        {
            _messages.Clear();
            _headIndex = 0;
            _isSorted = true;
        }

        internal McapMessage Peek()
        {
            ThrowIfEmpty(nameof(Peek));
            return _messages[_headIndex];
        }

        internal McapMessage Pop()
        {
            ThrowIfEmpty(nameof(Pop));
            var message = _messages[_headIndex++];
            CompactIfUseful();
            return message;
        }

        internal void Drop()
        {
            _headIndex++;
            CompactIfUseful();
        }

        internal void Add(McapMessage message)
        {
            _messages.Add(message);
            _isSorted = false;
        }

        internal void Sort(Comparison<McapMessage> comparison)
        {
            if (comparison == null) throw new ArgumentNullException(nameof(comparison));

            if (Count <= 0)
            {
                if (_messages.Count > 0)
                    Compact();
                _isSorted = true;
                return;
            }

            Compact();
            if (!_isSorted && _messages.Count > 1)
                _messages.Sort(comparison);
            _isSorted = true;
        }

        private void CompactIfUseful()
        {
            if (_headIndex > 32 && _headIndex * 2 >= _messages.Count)
                Compact();
        }

        private void Compact()
        {
            if (_headIndex <= 0)
                return;

            if (_headIndex >= _messages.Count)
                _messages.Clear();
            else
                _messages.RemoveRange(0, _headIndex);
            _headIndex = 0;
        }

        private void ThrowIfEmpty(string operation)
        {
            if (Count <= 0)
                throw new InvalidOperationException("McapReplayPendingQueue cannot " + operation + " an empty queue.");
        }
    }
}
