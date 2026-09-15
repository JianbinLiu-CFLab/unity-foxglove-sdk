using System;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherSessionFailure : Exception
    {
        public ComponentPublisherSessionFailure(string message) : base(message) { }
    }
    public static class ComponentPublisherSerializationBoundary
    {
        public static bool TrySerialize(Func<byte[]> serializer, string failureIdentity, Action<string> warnOnce, out byte[] payload, out string diagnostic)
        {
            if (serializer == null) throw new ArgumentNullException(nameof(serializer));
            try { payload = serializer(); diagnostic = string.Empty; return true; }
            catch (ComponentPublisherSessionFailure)
            {
                payload = null; diagnostic = string.IsNullOrEmpty(failureIdentity) ? "component serialization failed" : failureIdentity;
                warnOnce?.Invoke(diagnostic); return false;
            }
        }
    }
}
