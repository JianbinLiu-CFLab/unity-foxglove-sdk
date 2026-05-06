// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Pending service call model. Holds request metadata and provides
// Complete/Fail methods used by the service drain pipeline.

using System;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Represents a pending service call from a Foxglove client.
    /// Handler completes or fails the call; timeout is checked by the drain logic.
    /// </summary>
    public class FoxgloveServiceCall
    {
        public uint ServiceId { get; set; }
        public uint CallId { get; set; }
        public uint ClientId { get; set; }
        public string Encoding { get; set; }
        public byte[] Payload { get; set; }
        public DateTime CreatedAt { get; set; }

        public bool IsCompleted { get; private set; }
        public byte[] ResponsePayload { get; private set; }
        public string ResponseEncoding { get; private set; }
        public string FailureMessage { get; private set; }

        public bool IsTimedOut(TimeSpan timeout) => DateTime.UtcNow - CreatedAt > timeout;

        internal void Complete(string encoding, byte[] payload)
        {
            ResponseEncoding = encoding;
            ResponsePayload = payload;
            IsCompleted = true;
        }

        internal void Fail(string message)
        {
            FailureMessage = message;
            IsCompleted = true;
        }
    }
}
