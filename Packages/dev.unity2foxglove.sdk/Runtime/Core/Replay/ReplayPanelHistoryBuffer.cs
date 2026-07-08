// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    internal sealed class ReplayPanelHistoryBuffer
    {
        private const int MessageDataFrameOverheadBytes = 32;

        private readonly List<McapMessage> _buffer = new();
        private bool _active;
        private int _offset;
        private ulong _parkTimeNs;
        private bool _hasHistoryTime;
        private ulong _lastHistoryTimeNs;

        internal List<McapMessage> Buffer => _buffer;
        internal bool DebugActive => _active;
        internal int DebugBufferedCount => _buffer.Count;

        internal ulong GetHistoryFromTime(ulong startNs, ulong clampedToNs, ulong windowNs)
        {
            ulong fromNs;
            if (_hasHistoryTime && clampedToNs >= _lastHistoryTimeNs)
                fromNs = _lastHistoryTimeNs < ulong.MaxValue ? _lastHistoryTimeNs + 1UL : ulong.MaxValue;
            else
                fromNs = clampedToNs > windowNs ? clampedToNs - windowNs : startNs;

            return fromNs < startNs ? startNs : fromNs;
        }

        internal void BeginDrain(ulong parkTimeNs)
        {
            _offset = 0;
            _parkTimeNs = parkTimeNs;
            _active = true;
        }

        internal void CancelDrain()
        {
            _buffer.Clear();
            _active = false;
            _offset = 0;
            _parkTimeNs = 0;
        }

        internal void ResetDebounce()
        {
            CancelDrain();
            _hasHistoryTime = false;
            _lastHistoryTimeNs = 0;
        }

        internal void DrainLocked(
            FoxgloveSession session,
            IReadOnlyDictionary<ushort, string> channelTopicMap,
            IFoxgloveLogger logger,
            int maxMessagesPerTick,
            int queueReserveFrames,
            int queueReserveBytes)
        {
            if (session == null || !_active) return;

            var frameBudget = maxMessagesPerTick;
            var byteBudget = int.MaxValue;
            if (session.TryGetReplayQueueHeadroom(
                queueReserveFrames,
                queueReserveBytes,
                out var queueFrameHeadroom,
                out var queueByteHeadroom))
            {
                frameBudget = Math.Min(frameBudget, queueFrameHeadroom);
                byteBudget = queueByteHeadroom;
            }

            if (frameBudget <= 0 || byteBudget <= 0) return;

            var sentFrames = 0;
            var sentBytes = 0;
            while (_offset < _buffer.Count && sentFrames < frameBudget)
            {
                var msg = _buffer[_offset];
                var estimatedBytes = EstimateMessageDataFrameBytes(msg);
                if (sentBytes + estimatedBytes > byteBudget)
                    break;

                var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                string topic = null;
                channelTopicMap?.TryGetValue(msg.ChannelId, out topic);
                session.PublishReplay(replayId, msg.Data, msg.LogTime, "History", topic);
                _offset++;
                sentFrames++;
                sentBytes += estimatedBytes;
            }

            if (_offset >= _buffer.Count)
            {
                if (_parkTimeNs > 0)
                {
                    if (FoxgloveReplayTrace.TryTime("History", _parkTimeNs, "data", out var trace))
                        logger?.LogWarning(trace);
                    session.BroadcastReplayBinary(BinaryEncoding.EncodeTime(_parkTimeNs));
                }

                MarkDrainComplete();
            }
        }

        internal void MarkDrainComplete()
        {
            _lastHistoryTimeNs = _parkTimeNs;
            _hasHistoryTime = true;
            _buffer.Clear();
            _offset = 0;
            _parkTimeNs = 0;
            _active = false;
        }

        private static int EstimateMessageDataFrameBytes(McapMessage message)
        {
            return MessageDataFrameOverheadBytes + (message.Data?.Length ?? 0);
        }
    }
}
