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

    public readonly struct TopicContract : IEquatable<TopicContract>
    {
        public TopicContract(string topic, string schemaName, string encoding, string ownerKind)
        {
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            OwnerKind = ownerKind ?? string.Empty;
        }

        public string Topic { get; }
        public string SchemaName { get; }
        public string Encoding { get; }
        public string OwnerKind { get; }

        public bool Equals(TopicContract other)
            => string.Equals(Topic, other.Topic, StringComparison.Ordinal)
               && string.Equals(SchemaName, other.SchemaName, StringComparison.Ordinal)
               && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal)
               && string.Equals(OwnerKind, other.OwnerKind, StringComparison.Ordinal);

        public bool HasSameWireContract(TopicContract other)
            => string.Equals(Topic, other.Topic, StringComparison.Ordinal)
               && string.Equals(SchemaName, other.SchemaName, StringComparison.Ordinal)
               && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal);

        public override bool Equals(object obj)
            => obj is TopicContract other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Topic, SchemaName, Encoding, OwnerKind);
    }

    public readonly struct TopicContractConflict
    {
        public TopicContractConflict(TopicContract existing, TopicContract incoming)
        {
            Existing = existing;
            Incoming = incoming;
        }

        public TopicContract Existing { get; }
        public TopicContract Incoming { get; }
    }

    public readonly struct TopicContractRegistration
    {
        public TopicContractRegistration(
            TopicContract incoming,
            TopicContract existing,
            bool isNewClaim,
            bool isConflict,
            bool shouldWarn)
        {
            Incoming = incoming;
            Existing = existing;
            IsNewClaim = isNewClaim;
            IsConflict = isConflict;
            ShouldWarn = shouldWarn;
        }

        public TopicContract Incoming { get; }
        public TopicContract Existing { get; }
        public bool IsNewClaim { get; }
        public bool IsConflict { get; }
        public bool ShouldWarn { get; }
    }

    public sealed class TopicContractLedger
    {
        private const char KeySeparator = '\u001f';
        private readonly Dictionary<string, TopicContract> _claims =
            new Dictionary<string, TopicContract>(StringComparer.Ordinal);
        private readonly HashSet<string> _conflictKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<TopicContractConflict> _conflicts =
            new List<TopicContractConflict>();

        public int ClaimCount => _claims.Count;
        public int ConflictCount => _conflicts.Count;
        public IReadOnlyDictionary<string, TopicContract> Claims => _claims;
        public IReadOnlyList<TopicContractConflict> Conflicts => _conflicts;

        public TopicContractRegistration Register(TopicContract incoming)
        {
            if (!_claims.TryGetValue(incoming.Topic, out var existing))
            {
                _claims.Add(incoming.Topic, incoming);
                return new TopicContractRegistration(incoming, default, true, false, false);
            }

            if (existing.HasSameWireContract(incoming))
                return new TopicContractRegistration(incoming, existing, false, false, false);

            var conflictKey = BuildConflictKey(existing, incoming);
            var shouldWarn = _conflictKeys.Add(conflictKey);
            if (shouldWarn)
                _conflicts.Add(new TopicContractConflict(existing, incoming));
            return new TopicContractRegistration(incoming, existing, false, true, shouldWarn);
        }

        public void Clear()
        {
            _claims.Clear();
            _conflictKeys.Clear();
            _conflicts.Clear();
        }

        private static string BuildConflictKey(TopicContract existing, TopicContract incoming)
            => string.Join(
                KeySeparator.ToString(),
                existing.Topic,
                existing.SchemaName,
                existing.Encoding,
                existing.OwnerKind,
                incoming.SchemaName,
                incoming.Encoding,
                incoming.OwnerKind);
    }
}
