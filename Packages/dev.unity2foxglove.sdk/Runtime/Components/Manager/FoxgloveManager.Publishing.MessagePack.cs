// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Generated FoxRun MessagePack live and recording-only publication.

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>Publish one generated immutable MessagePack payload live.</summary>
        public void PublishFoxRunMessagePackBytes(
            string topic,
            byte[] payload,
            ulong logTimeNs)
            => PublishMsgPack(topic, payload, logTimeNs);

        /// <summary>
        /// Prepare a schemaless hidden MCAP channel for typed FoxRun
        /// MessagePack output.
        /// </summary>
        public bool TryPrepareFoxRunMessagePackRecording(
            string topic,
            out uint channelId,
            out string reason)
            => TryPrepareFoxRunRawRecording(
                topic,
                MsgPackEncoding,
                string.Empty,
                string.Empty,
                string.Empty,
                out channelId,
                out reason);

        /// <summary>
        /// Prepare or reassert a hidden raw MCAP channel using its complete
        /// immutable wire descriptor.
        /// </summary>
        internal bool TryPrepareFoxRunRawRecording(
            string topic,
            string messageEncoding,
            string schemaName,
            string schemaEncoding,
            string schema,
            out uint channelId,
            out string reason)
        {
            channelId = 0;
            reason = string.Empty;
            if (SuppressLivePublishersForReplay)
            {
                reason = "Replay is suppressing live publishers.";
                return false;
            }
            if (_runtime == null || !_runtime.IsRunning || !_runtime.RecordingEnabled)
                return false;
            if (!IsValidPublishTopic(topic))
            {
                reason = "FoxRun recording topic is invalid.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(messageEncoding))
            {
                reason = "FoxRun recording message encoding is invalid.";
                return false;
            }

            schemaName ??= string.Empty;
            schemaEncoding ??= string.Empty;
            schema ??= string.Empty;

            var descriptor = new FoxRunRawRecordingChannelDescriptor(
                topic,
                messageEncoding,
                schemaName,
                schemaEncoding,
                schema);
            channelId = _foxRunRawRecordingChannelCache.GetOrAdd(
                descriptor,
                () => (uint)_connectionState.NextChannelId++);

            _runtime.RegisterRecordingOnlyChannel(
                descriptor.ToChannel(channelId));

            if (_runtime.HasRecordingDemand(channelId))
                return true;
            reason = "MCAP recorder or recording filter rejected the FoxRun channel.";
            return false;
        }

        /// <summary>Publish one generated immutable MessagePack payload to MCAP.</summary>
        public bool TryPublishFoxRunMessagePackRecording(
            string topic,
            byte[] payload,
            ulong logTimeNs,
            out string reason)
        {
            if (payload == null)
            {
                reason = "FoxRun MessagePack payload is unavailable.";
                return false;
            }
            if (!TryPrepareFoxRunMessagePackRecording(
                    topic,
                    out var channelId,
                    out reason))
            {
                return false;
            }
            if (_runtime.PublishRecordingOnly(channelId, payload, logTimeNs))
                return true;
            reason = "MCAP recorder stopped accepting the FoxRun channel.";
            return false;
        }
    }
}
