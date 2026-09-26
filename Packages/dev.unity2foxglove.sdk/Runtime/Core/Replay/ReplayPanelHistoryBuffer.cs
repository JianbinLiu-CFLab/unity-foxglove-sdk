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
        private readonly Dictionary<uint, ClientDrainState> _clientDrains = new();

        internal List<McapMessage> Buffer => _buffer;
        internal bool DebugActive => _active;
        internal int DebugBufferedCount => _buffer.Count;
        internal int DebugClientDrainCount => _clientDrains.Count;
        internal List<McapMessage> DebugGetClientBuffer(uint clientId)
            => _clientDrains.TryGetValue(clientId, out var state) ? state.Buffer : null;

        private sealed class ClientDrainState
        {
            internal List<McapMessage> Buffer = new();
            internal int Offset;
            internal ulong ParkTimeNs;
            internal bool Active;
            internal bool HasHistoryTime;
            internal ulong LastHistoryTimeNs;
        }

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
            _clientDrains.Clear();
        }

        internal void CancelDrain(uint clientId)
        {
            _clientDrains.Remove(clientId);
        }

        internal void ResetDebounce()
        {
            CancelDrain();
            _hasHistoryTime = false;
            _lastHistoryTimeNs = 0;
        }

        internal void ResetDebounce(uint clientId)
        {
            CancelDrain(clientId);
        }

        internal ulong GetHistoryFromTime(
            uint clientId,
            ulong startNs,
            ulong clampedToNs,
            ulong windowNs)
        {
            if (!_clientDrains.TryGetValue(clientId, out var state))
                return clampedToNs > windowNs ? Math.Max(startNs, clampedToNs - windowNs) : startNs;

            ulong fromNs;
            if (state.HasHistoryTime && clampedToNs >= state.LastHistoryTimeNs)
                fromNs = state.LastHistoryTimeNs < ulong.MaxValue ? state.LastHistoryTimeNs + 1UL : ulong.MaxValue;
            else
                fromNs = clampedToNs > windowNs ? clampedToNs - windowNs : startNs;

            return fromNs < startNs ? startNs : fromNs;
        }

        internal void BeginClientDrains(
            ulong parkTimeNs,
            IReadOnlyDictionary<uint, List<McapMessage>> clientBuffers,
            bool replaceExisting = true)
        {
            if (replaceExisting)
                _clientDrains.Clear();
            if (clientBuffers == null)
                return;

            foreach (var pair in clientBuffers)
            {
                var state = new ClientDrainState
                {
                    Buffer = pair.Value ?? new List<McapMessage>(),
                    ParkTimeNs = parkTimeNs,
                    Active = true
                };
                _clientDrains[pair.Key] = state;
            }
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
            var maxFrameCapacity = int.MaxValue;
            var maxByteCapacity = int.MaxValue;
            if (session.TryGetReplayQueueHeadroom(
                queueReserveFrames,
                queueReserveBytes,
                out var queueFrameHeadroom,
                out var queueByteHeadroom,
                out maxFrameCapacity,
                out maxByteCapacity))
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
                {
                    var reservedCapacity = Math.Max(0, maxByteCapacity - Math.Max(0, queueReserveBytes));
                    if (estimatedBytes > reservedCapacity)
                    {
                        logger?.LogWarning(
                            "Replay history message skipped because its estimated frame size exceeds transport capacity.");
                        _offset++;
                        continue;
                    }

                    break;
                }

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

        internal void DrainClientsLocked(
            FoxgloveSession session,
            IReadOnlyDictionary<ushort, string> channelTopicMap,
            IFoxgloveLogger logger,
            int maxMessagesPerTick,
            int queueReserveFrames,
            int queueReserveBytes)
        {
            if (session == null || _clientDrains.Count == 0)
                return;

            foreach (var pair in _clientDrains)
            {
                var clientId = pair.Key;
                var state = pair.Value;
                if (!state.Active)
                    continue;

                var frameBudget = maxMessagesPerTick;
                var byteBudget = int.MaxValue;
                var maxFrameCapacity = int.MaxValue;
                var maxByteCapacity = int.MaxValue;
                if (session.TryGetReplayQueueHeadroom(
                    clientId,
                    queueReserveFrames,
                    queueReserveBytes,
                    out var queueFrameHeadroom,
                    out var queueByteHeadroom,
                    out maxFrameCapacity,
                    out maxByteCapacity))
                {
                    frameBudget = Math.Min(frameBudget, queueFrameHeadroom);
                    byteBudget = queueByteHeadroom;
                }

                if (frameBudget <= 0 || byteBudget <= 0)
                    continue;

                var sentFrames = 0;
                var sentBytes = 0;
                while (state.Offset < state.Buffer.Count && sentFrames < frameBudget)
                {
                    var msg = state.Buffer[state.Offset];
                    var estimatedBytes = EstimateMessageDataFrameBytes(msg);
                    if (sentBytes + estimatedBytes > byteBudget)
                    {
                        var reservedCapacity = Math.Max(0, maxByteCapacity - Math.Max(0, queueReserveBytes));
                        if (estimatedBytes > reservedCapacity)
                        {
                            logger?.LogWarning(
                                "Replay history message skipped because its estimated frame size exceeds transport capacity.");
                            state.Offset++;
                            continue;
                        }

                        break;
                    }

                    var replayId = (uint)(McapReplayEngine.ReplayChannelIdBase | msg.ChannelId);
                    string topic = null;
                    channelTopicMap?.TryGetValue(msg.ChannelId, out topic);
                    session.PublishReplayToClient(clientId, replayId, msg.Data, msg.LogTime, "History", topic);
                    state.Offset++;
                    sentFrames++;
                    sentBytes += estimatedBytes;
                }

                if (state.Offset >= state.Buffer.Count)
                {
                    if (state.ParkTimeNs > 0)
                    {
                        if (FoxgloveReplayTrace.TryTime("History", state.ParkTimeNs, "data", out var trace))
                            logger?.LogWarning(trace);
                        session.SendReplayTimeToClient(clientId, state.ParkTimeNs);
                    }

                    state.LastHistoryTimeNs = state.ParkTimeNs;
                    state.HasHistoryTime = true;
                    state.Buffer = null;
                    state.Offset = 0;
                    state.ParkTimeNs = 0;
                    state.Active = false;
                }
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
