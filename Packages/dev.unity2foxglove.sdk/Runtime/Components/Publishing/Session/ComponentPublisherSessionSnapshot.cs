using System;
using System.Collections.Generic;
using System.Linq;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherSessionSnapshot
    {
        internal ComponentPublisherSessionSnapshot(ulong generation, IReadOnlyList<ComponentPublisherSessionEntry> entries)
        { Generation = generation; Entries = Array.AsReadOnly(entries.ToArray()); IsAvailable = true; }
        internal ComponentPublisherSessionSnapshot(ulong generation, string diagnostic)
        { Generation = generation; Entries = Array.Empty<ComponentPublisherSessionEntry>(); IsAvailable = false; Diagnostic = diagnostic ?? string.Empty; }
        public ulong Generation { get; }
        public IReadOnlyList<ComponentPublisherSessionEntry> Entries { get; }
        public bool IsAvailable { get; }
        public string Diagnostic { get; }

        /// <summary>Finds the immutable contract captured for one publisher instance.</summary>
        public bool TryGetEntry(object publisher, out ComponentPublisherSessionEntry entry)
        {
            entry = null;
            if (publisher == null) return false;
            foreach (var candidate in Entries)
            {
                if (ReferenceEquals(candidate.Publisher, publisher))
                {
                    entry = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
