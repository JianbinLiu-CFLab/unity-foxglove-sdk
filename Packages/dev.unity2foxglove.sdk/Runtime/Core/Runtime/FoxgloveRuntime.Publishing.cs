// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Runtime
// Purpose: FoxgloveRuntime Publishing responsibilities.

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;
using static Unity.FoxgloveSDK.Transport.TransportStatsSnapshot;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveRuntime
    {
        // ── Channel API ──

        /// <summary>Register an advertise channel on the session.</summary>
        public void RegisterChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(RegisterChannel), channel?.Id);
                return;
            }

            _session.RegisterChannel(channel);
        }

        /// <summary>Register a channel visible only to the attached MCAP recorder.</summary>
        internal void RegisterRecordingOnlyChannel(AdvertiseChannel channel)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(RegisterRecordingOnlyChannel), channel?.Id);
                return;
            }

            _session.RegisterRecordingOnlyChannel(channel);
        }

        /// <summary>Whether an MCAP recorder currently accepts this hidden channel.</summary>
        public bool HasRecordingDemand(uint channelId)
            => !ReplaySuppressesLivePublishing
               && _session != null
               && _session.HasRecordingDemand(channelId);

        /// <summary>Unregister a channel by its numeric ID.</summary>
        public void UnregisterChannel(uint channelId)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.UnregisterChannel(channelId);
        }

        /// <summary>Publish raw bytes to a channel. Timestamp is taken from the clock.</summary>
        public void Publish(uint channelId, byte[] payload)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(Publish), channelId);
                return;
            }

            _session.Publish(channelId, payload);
        }

        /// <summary>Publish raw bytes with an explicit nanosecond timestamp.</summary>
        public void Publish(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(Publish), channelId);
                return;
            }

            _session.Publish(channelId, payload, logTimeNs);
        }

        /// <summary>Publish raw bytes only to a previously hidden MCAP channel.</summary>
        public bool PublishRecordingOnly(uint channelId, byte[] payload, ulong logTimeNs)
        {
            if (_session == null || ReplaySuppressesLivePublishing || !_session.HasRecordingDemand(channelId))
                return false;
            _session.Publish(channelId, payload, logTimeNs);
            return true;
        }

        /// <summary>Register a schema channel on the session with the given encoding (default "json").</summary>
        public void RegisterSchemaChannel(
            uint channelId,
            string topic,
            string schemaName,
            string encoding = "json",
            string schemaEncoding = null)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(RegisterSchemaChannel), channelId);
                return;
            }

            _session.RegisterSchemaChannel(channelId, topic, schemaName, encoding, schemaEncoding);
        }

        /// <summary>Serialize and publish a JSON message. Timestamp is taken from the clock.</summary>
        public void PublishJson(uint channelId, object message)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(PublishJson), channelId);
                return;
            }

            _session.PublishJson(channelId, message);
        }

        /// <summary>Serialize and publish a JSON message with an explicit nanosecond timestamp.</summary>
        public void PublishJson(uint channelId, object message, ulong logTimeNs)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            if (ReplaySuppressesLivePublishing)
            {
                WarnReplaySuppressed(nameof(PublishJson), channelId);
                return;
            }

            _session.PublishJson(channelId, message, logTimeNs);
        }

        /// <summary>
        /// Publish an official Foxglove diagnostics status message to connected clients.
        /// </summary>
        /// <param name="level">Status severity encoded with official numeric values.</param>
        /// <param name="message">Human-readable diagnostic message.</param>
        /// <param name="id">Optional stable status identifier for later removal.</param>
        public void PublishStatus(FoxgloveStatusLevel level, string message, string id = null)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.PublishStatus(level, message, id);
        }

        /// <summary>
        /// Remove one or more official Foxglove diagnostics status messages.
        /// </summary>
        /// <param name="ids">Status identifiers to remove.</param>
        public void RemoveStatus(params string[] ids)
        {
            if (_session == null) throw new InvalidOperationException("Session not started.");
            _session.RemoveStatus(ids);
        }

        /// <summary>
        /// Drain pending service calls on the calling thread.
        /// Must be called on the Unity main thread if handlers touch Unity objects.
        /// </summary>
        public void DrainServiceCalls() => _session?.DrainServiceCalls();
    }
}
