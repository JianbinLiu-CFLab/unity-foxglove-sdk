using System;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;

namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    /// <summary>
    /// Resolves the generated codec that belongs to a publisher's effective
    /// MessagePack contract.  The manager uses this at capture time so the
    /// immutable session records the same generated entry as the publish path.
    /// </summary>
    public static class ComponentPublisherMessagePackEntryResolver
    {
        public static ComponentMessagePackGeneratedEntry Resolve(
            Type messageType,
            PublisherEffectiveEncoding effectiveEncoding)
        {
            if (messageType == null || effectiveEncoding != PublisherEffectiveEncoding.MsgPack)
                return null;

            return ComponentMessagePackCodecRegistry.TryGet(messageType, out var entry)
                ? entry
                : null;
        }
    }
}
