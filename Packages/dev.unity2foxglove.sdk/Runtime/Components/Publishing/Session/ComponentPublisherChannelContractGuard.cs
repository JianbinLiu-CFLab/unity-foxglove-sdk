using System;
using System.Collections.Generic;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherChannelContractGuard
    {
        private readonly Dictionary<string, ComponentPublisherChannelDescriptor> _claims = new Dictionary<string, ComponentPublisherChannelDescriptor>(StringComparer.Ordinal);
        public bool TryClaim(ComponentPublisherChannelDescriptor descriptor, out string reason)
        {
            if (_claims.TryGetValue(descriptor.Topic, out var existing))
            {
                if (existing.Equals(descriptor)) { reason = string.Empty; return true; }
                reason = "typed/raw Component channel descriptor conflict"; return false;
            }
            _claims.Add(descriptor.Topic, descriptor); reason = string.Empty; return true;
        }
        public void Clear() => _claims.Clear();
        public int Count => _claims.Count;
    }
}
