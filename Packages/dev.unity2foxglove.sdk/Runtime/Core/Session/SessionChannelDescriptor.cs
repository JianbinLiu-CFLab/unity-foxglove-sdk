using System;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    internal static class SessionChannelDescriptor
    {
        internal static bool Equals(AdvertiseChannel left, AdvertiseChannel right)
            => left != null
               && right != null
               && string.Equals(left.Topic, right.Topic, StringComparison.Ordinal)
               && string.Equals(left.Encoding, right.Encoding, StringComparison.Ordinal)
               && string.Equals(left.SchemaName, right.SchemaName, StringComparison.Ordinal)
               && string.Equals(left.SchemaEncoding ?? string.Empty, right.SchemaEncoding ?? string.Empty, StringComparison.Ordinal)
               && string.Equals(left.Schema ?? string.Empty, right.Schema ?? string.Empty, StringComparison.Ordinal);
    }
}
