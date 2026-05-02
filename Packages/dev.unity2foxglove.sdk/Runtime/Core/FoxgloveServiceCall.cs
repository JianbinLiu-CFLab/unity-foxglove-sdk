using System;
using System.Threading;

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
        public int Completed; // 0=pending, 1=completed
        public byte[] ResponsePayload;
        public string ResponseEncoding;
        public string FailureMessage;

        public bool IsCompleted => Completed != 0;
        public bool IsTimedOut(TimeSpan timeout) => DateTime.UtcNow - CreatedAt > timeout;
    }
}
