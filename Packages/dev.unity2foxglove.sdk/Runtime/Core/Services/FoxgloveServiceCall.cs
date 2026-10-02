// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Services
// Purpose: Pending service call model. Holds request metadata and provides
// Complete/Fail methods used by the service drain pipeline.

using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Represents a pending service call from a Foxglove client.
    /// Handler completes or fails the call; timeout is checked by the drain logic.
    /// </summary>
    public class FoxgloveServiceCall
    {
        /// <summary>The Foxglove service identifier.</summary>
        public uint ServiceId { get; set; }
        /// <summary>The unique call identifier assigned by the client.</summary>
        public uint CallId { get; set; }
        /// <summary>The client ID that initiated the call.</summary>
        public uint ClientId { get; set; }
        /// <summary>Encoding of the request payload.</summary>
        public string Encoding { get; set; }
        /// <summary>Raw request payload bytes.</summary>
        public byte[] Payload { get; set; }
        /// <summary>Parsed JSON request payload when the request uses JSON encoding.</summary>
        public JToken JsonPayload { get; set; }
        /// <summary>UTC timestamp when the call was created.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Monotonic timestamp captured when the call was enqueued.</summary>
        internal long CreatedTimestamp { get; set; }
        internal bool HasCreatedTimestamp { get; set; }

        /// <summary>Whether the call has been completed (success or failure).</summary>
        public bool IsCompleted { get; private set; }
        /// <summary>Response payload when completed successfully; null on failure.</summary>
        public byte[] ResponsePayload { get; private set; }
        /// <summary>Encoding of the response when completed successfully.</summary>
        public string ResponseEncoding { get; private set; }
        /// <summary>Failure reason message when completed with a failure.</summary>
        public string FailureMessage { get; private set; }

        /// <summary>Check whether the call has exceeded the given timeout.</summary>
        public bool IsTimedOut(TimeSpan timeout)
            => IsTimedOut(timeout, Stopwatch.GetTimestamp());

        internal bool IsTimedOut(TimeSpan timeout, long nowTimestamp)
        {
            if (!HasCreatedTimestamp)
                return DateTime.UtcNow - CreatedAt > timeout;
            if (nowTimestamp <= CreatedTimestamp)
                return false;

            var timeoutTicks = timeout <= TimeSpan.Zero
                ? 0L
                : (long)Math.Ceiling(timeout.TotalSeconds * Stopwatch.Frequency);
            return nowTimestamp - CreatedTimestamp > timeoutTicks;
        }

        internal FoxgloveServiceCall CreateSnapshot()
        {
            return new FoxgloveServiceCall
            {
                ServiceId = ServiceId,
                CallId = CallId,
                ClientId = ClientId,
                Encoding = Encoding,
                Payload = Payload == null ? null : (byte[])Payload.Clone(),
                JsonPayload = JsonPayload?.DeepClone(),
                CreatedAt = CreatedAt,
                CreatedTimestamp = CreatedTimestamp,
                HasCreatedTimestamp = HasCreatedTimestamp,
                IsCompleted = IsCompleted,
                ResponsePayload = ResponsePayload == null ? null : (byte[])ResponsePayload.Clone(),
                ResponseEncoding = ResponseEncoding,
                FailureMessage = FailureMessage
            };
        }

        /// <summary>Mark the call as completed with a success response.</summary>
        internal bool Complete(string encoding, byte[] payload)
        {
            if (IsCompleted)
                return false;
            ResponseEncoding = encoding;
            ResponsePayload = payload;
            IsCompleted = true;
            return true;
        }

        /// <summary>Mark the call as completed with a failure message.</summary>
        internal bool Fail(string message)
        {
            if (IsCompleted)
                return false;
            FailureMessage = message;
            IsCompleted = true;
            return true;
        }
    }
}
