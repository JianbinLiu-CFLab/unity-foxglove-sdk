// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: High-level MCAP recorder that wraps McapWriter. Handles chunk
// management, schema/channel deduplication, metadata indexes, compression,
// and final summary/statistics output on close. Attaches to FoxgloveSession
// via dual-write hooks so live publish data is simultaneously recorded.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
using ZstdSharp;

namespace Unity.FoxgloveSDK.IO
{
    public partial class McapRecorder
    {
        /// <summary>
        /// Register a server-side channel and write its MCAP channel record immediately.
        /// </summary>
        public void AddChannel(uint fId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            AddChannelCore(fId, topic, enc, sName, sEnc, sContent, null);
        }

        /// <summary>
        /// Return whether a server channel id is already recorded and, when it
        /// is, whether the proposed descriptor exactly matches the durable
        /// topic and encoding/schema signature.
        /// </summary>
        internal bool TryGetServerChannelCompatibility(
            uint fId,
            string topic,
            string enc,
            string sName,
            string sEnc,
            string sContent,
            out bool compatible)
        {
            var candidate = CreateTopicSignature(enc, sName, sEnc, sContent);
            lock (_lock)
            {
                if (!_serverChannelWriteStates.TryGetValue(fId, out var state))
                {
                    compatible = false;
                    return false;
                }

                compatible = string.Equals(state.Topic, topic, StringComparison.Ordinal)
                             && state.Signature.Equals(candidate);
                return true;
            }
        }

        /// <summary>Return whether this recorder owns a durable server channel for the Foxglove id.</summary>
        internal bool HasServerChannel(uint fId)
        {
            lock (_lock)
                return _serverChannelWriteStates.ContainsKey(fId);
        }

        internal bool IsServerChannelAdmissionRejected(
            uint fId,
            string topic,
            string enc,
            string sName,
            string sEnc,
            string sContent)
        {
            var signature = CreateTopicSignature(NormalizeMessageEncoding(enc), sName, sEnc, sContent);
            lock (_lock)
            {
                return _rejectedServerChannelAdmissions.TryGetValue(fId, out var rejection)
                       && string.Equals(rejection.Topic, topic, StringComparison.Ordinal)
                       && rejection.Signature.Equals(signature);
            }
        }

        internal void ClearServerChannelAdmissionRejection(uint fId)
        {
            lock (_lock)
                _rejectedServerChannelAdmissions.Remove(fId);
        }

        internal void ClearServerChannelAdmissionRejections()
        {
            lock (_lock)
                _rejectedServerChannelAdmissions.Clear();
        }

        internal void AddChannelPreservingMcapId(uint fId, ushort mcapChannelId, string topic, string enc, string sName, string sEnc, string sContent)
        {
            AddChannelCore(fId, topic, enc, sName, sEnc, sContent, mcapChannelId);
        }

        private void AddChannelCore(uint fId, string topic, string enc, string sName, string sEnc, string sContent, ushort? explicitMcapChannelId)
        {
            lock (_lock)
            {
                if (_recordingFailed || _closed) return;
                if (_serverChannelWriteStates.ContainsKey(fId))
                {
                    _log.LogWarning(
                        $"MCAP: ignoring duplicate server channel id {fId} for topic '{topic}' because the channel id is already registered.");
                    return;
                }

                var normalizedEnc = NormalizeMessageEncoding(enc);
                var signature = CreateTopicSignature(normalizedEnc, sName, sEnc, sContent);
                if (_rejectedServerChannelAdmissions.TryGetValue(fId, out var rejection)
                    && string.Equals(rejection.Topic, topic, StringComparison.Ordinal)
                    && rejection.Signature.Equals(signature))
                {
                    return;
                }

                if (WouldMixTopicSignature(topic, signature))
                {
                    _rejectedServerChannelAdmissions[fId] = (topic, signature);
                    _log.LogWarning(
                        $"MCAP: skipping server channel for topic '{topic}' because its signature is incompatible with an existing recorded channel.");
                    return;
                }
                var sid = GetOrCreateSchema(sName, sEnc, sContent);
                if (sid != 0) _outputSchemaIds.Add(sid);
                if (_recordingFailed) return;
                ushort mCid;
                if (explicitMcapChannelId.HasValue)
                {
                    mCid = explicitMcapChannelId.Value;
                    if (mCid == 0 || IsMcapChannelIdRegistered(mCid))
                    {
                        Fail("MCAP channel ID collision");
                        return;
                    }

                    if (mCid >= _nextChannelId)
                        _nextChannelId = (ushort)(mCid == ushort.MaxValue ? 0 : mCid + 1);
                }
                else
                {
                    if (_nextChannelId == 0) { Fail("Channel ID overflow"); return; }
                    mCid = _nextChannelId++;
                }
                var state = new ChannelWriteState
                {
                    McapId = mCid,
                    SchemaId = sid,
                    Topic = topic,
                    Signature = signature
                };
                var meta = CreateChannelMetadata(McapChannelDirection.Output);
                var channelRecordStart = _writer.Position;
                try
                {
                    _writer.WriteChannel(mCid, sid, topic, normalizedEnc, meta);
                }
                catch (Exception ex)
                {
                    HandleTopLevelRecordWriteFailure(channelRecordStart, ex, "Channel");
                    throw;
                }
                _serverChannelWriteStates[fId] = state;
                _rejectedServerChannelAdmissions.Remove(fId);
                if (!_topicChannelWriteState.ContainsKey((topic, McapChannelDirection.Output)))
                    _topicChannelWriteState[(topic, McapChannelDirection.Output)] = state;
                _channels.Add(new ChannelRecordState { Id = mCid, SchemaId = sid, Topic = topic, Encoding = normalizedEnc, Metadata = SnapshotChannelMetadata(meta) });
                RecordTopicSignature(topic, signature);
            }
        }

        private bool IsMcapChannelIdRegistered(ushort mcapChannelId)
        {
            foreach (var state in _serverChannelWriteStates.Values)
                if (state.McapId == mcapChannelId)
                    return true;
            foreach (var state in _clientChannelWriteState.Values)
                if (state.McapId == mcapChannelId)
                    return true;
            return false;
        }

    }
}
