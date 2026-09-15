using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public static class ComponentPublisherSessionLimits
    {
        public const int MaxEntries = ComponentMessagePackMetadataLimits.MaxAggregateEntries;
        public const int MaxMetadataBytesPerEntry = ComponentMessagePackMetadataLimits.MaxMetadataBytesPerEntry;
        public const int MaxMetadataBytesTotal = ComponentMessagePackMetadataLimits.MaxMetadataBytesTotal;
        public static int MetadataBytes(ComponentPublisherContractDraft d)
            => StringBytes(d.CaptureIdentity) + StringBytes(d.PublisherType.FullName) + StringBytes(d.ModeKey) + StringBytes(d.Topic) + StringBytes(d.SchemaName) + (d.MessagePackEntry == null ? 0 : StringBytes(d.MessagePackEntry.ShapeIdentity));
        private static int StringBytes(string value) => value == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(value);
    }
    public sealed class ComponentPublisherSessionBuilder
    {
        public ComponentPublisherSessionSnapshot Build(long generation, IEnumerable<ComponentPublisherContractDraft> drafts)
        {
            if (drafts == null) throw new ArgumentNullException(nameof(drafts));
            var ordered = drafts.OrderBy(d => d.CaptureIdentity, StringComparer.Ordinal).ThenBy(d => d.PublisherType.FullName, StringComparer.Ordinal).ToArray();
            if (ordered.Length > ComponentPublisherSessionLimits.MaxEntries) return new ComponentPublisherSessionSnapshot(generation, "component session entry-count limit exceeded");
            var seen = new HashSet<object>(ComponentPublisherReferenceComparer.Instance);
            var entries = new List<ComponentPublisherSessionEntry>(ordered.Length);
            var total = 0;
            foreach (var draft in ordered)
            {
                if (!seen.Add(draft.Publisher)) continue;
                var bytes = ComponentPublisherSessionLimits.MetadataBytes(draft);
                var fieldOverflow = new[] { draft.CaptureIdentity, draft.PublisherType.FullName, draft.ModeKey, draft.Topic, draft.SchemaName, draft.MessagePackEntry?.ShapeIdentity }
                    .Any(value => value != null && System.Text.Encoding.UTF8.GetByteCount(value) > ComponentMessagePackMetadataLimits.MaxStringUtf8Bytes);
                total += Math.Min(bytes, ComponentPublisherSessionLimits.MaxMetadataBytesPerEntry);
                var valid = bytes <= ComponentPublisherSessionLimits.MaxMetadataBytesPerEntry && !fieldOverflow;
                var diagnostic = valid ? string.Empty : "component metadata exceeds bounded entry limit";
                if (draft.EffectiveEncoding == PublisherEffectiveEncoding.Unsupported) { valid = false; diagnostic = "requested encoding is unsupported"; }
                if (draft.EffectiveEncoding == PublisherEffectiveEncoding.MsgPack && (draft.MessagePackEntry == null || !draft.MessagePackEntry.IsAvailable)) { valid = false; diagnostic = draft.MessagePackEntry?.Diagnostic ?? "MessagePack codec unavailable"; }
                entries.Add(new ComponentPublisherSessionEntry(draft, valid, diagnostic));
            }
            if (total > ComponentPublisherSessionLimits.MaxMetadataBytesTotal) return new ComponentPublisherSessionSnapshot(generation, "component session metadata budget exceeded");
            return new ComponentPublisherSessionSnapshot(generation, entries);
        }
    }
}
