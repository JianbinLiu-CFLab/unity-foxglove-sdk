// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Recording
// Purpose: Topic signature reuse and conflict checks for McapRecorder.

using System;

namespace Unity.FoxgloveSDK.IO
{
    public partial class McapRecorder
    {
        bool TryReuseExistingTopicChannel(
            string topic,
            TopicSignature incoming,
            string sContent,
            out ChannelWriteState state)
        {
            state = null;
            if (string.IsNullOrEmpty(topic)) return false;
            if (!_topicChannelWriteState.TryGetValue(topic, out var existingState)) return false;
            if (!_topicSignatures.TryGetValue(topic, out var existing)) return false;

            if (!string.IsNullOrEmpty(incoming.SchemaName) &&
                string.IsNullOrEmpty(sContent) &&
                existing.Encoding == incoming.Encoding &&
                existing.SchemaName == incoming.SchemaName &&
                (string.IsNullOrEmpty(incoming.SchemaEncoding) || existing.SchemaEncoding == incoming.SchemaEncoding) &&
                !string.IsNullOrEmpty(existing.Hash))
            {
                state = existingState;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check whether an incoming topic signature conflicts with a previously
        /// recorded signature for the same topic.
        /// </summary>
        bool WouldMixTopicSignature(string topic, TopicSignature signature)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            return _topicSignatures.TryGetValue(topic, out var existing) && !existing.Equals(signature);
        }

        /// <summary>
        /// Persist the topic signature on first use so future channels for the
        /// same topic can be validated for compatibility.
        /// </summary>
        void RecordTopicSignature(string topic, TopicSignature signature)
        {
            if (string.IsNullOrEmpty(topic)) return;
            if (_topicSignatures.ContainsKey(topic)) return;
            _topicSignatures[topic] = signature;
        }
    }
}
