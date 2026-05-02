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

        internal void InterlockedComplete(string encoding, byte[] payload)
        {
            ResponseEncoding = encoding;
            ResponsePayload = payload;
            IsCompleted = true;
        }

        internal void InterlockedFail(string message)
        {
            FailureMessage = message;
            IsCompleted = true;
        }
    }
}
