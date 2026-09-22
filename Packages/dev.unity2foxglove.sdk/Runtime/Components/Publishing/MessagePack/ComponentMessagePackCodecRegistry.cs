using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    public sealed class ComponentMessagePackCodecSnapshot
    {
        internal ComponentMessagePackCodecSnapshot(IReadOnlyList<ComponentMessagePackGeneratedEntry> entries, string hash, bool sealedState)
        { Entries = Array.AsReadOnly(entries.ToArray()); ManifestHash = hash; IsSealed = sealedState; }
        public IReadOnlyList<ComponentMessagePackGeneratedEntry> Entries { get; }
        public string ManifestHash { get; }
        public bool IsSealed { get; }
    }

    public static class ComponentMessagePackCodecRegistry
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, ComponentMessagePackGeneratedEntry> ByType = new Dictionary<Type, ComponentMessagePackGeneratedEntry>();
        private static readonly Dictionary<string, ComponentMessagePackGeneratedEntry> BySchema = new Dictionary<string, ComponentMessagePackGeneratedEntry>(StringComparer.Ordinal);
        private static readonly HashSet<string> Contributions = new HashSet<string>(StringComparer.Ordinal);
        private static int _contributionCount;
        private static bool _sealed;
        private static string _aggregateHash = string.Empty;

        public static void RegisterGenerated(ComponentMessagePackGeneratedManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            lock (Gate)
            {
                var contributionKey = manifest.AssemblyIdentity + "|" + manifest.ManifestHash;
                if (Contributions.Contains(contributionKey)) return;
                if (_sealed) return;
                if (_contributionCount >= ComponentMessagePackMetadataLimits.MaxContributions || manifest.Entries.Count > ComponentMessagePackMetadataLimits.MaxEntriesPerContribution) return;
                var pending = manifest.Entries.OrderBy(e => e.ClrType.FullName, StringComparer.Ordinal).ToArray();
                if (ByType.Count + pending.Length > ComponentMessagePackMetadataLimits.MaxAggregateEntries) return;
                foreach (var entry in pending)
                {
                    if (!ComponentMessagePackMetadataLimits.Fits(entry.ClrType.FullName) || !ComponentMessagePackMetadataLimits.Fits(entry.LogicalSchemaName) || !ComponentMessagePackMetadataLimits.Fits(entry.ShapeIdentity) || !ComponentMessagePackMetadataLimits.Fits(entry.Diagnostic, ComponentMessagePackMetadataLimits.MaxDiagnosticUtf8Bytes)) continue;
                    if (ByType.TryGetValue(entry.ClrType, out var existing))
                    {
                        if (!Equivalent(existing, entry)) ByType[entry.ClrType] = Conflict(existing, entry, entry.ClrType);
                        continue;
                    }
                    if (entry.ClaimsLogicalSchemaKey && BySchema.TryGetValue(entry.LogicalSchemaName, out var schemaExisting) && !Equivalent(schemaExisting, entry))
                    {
                        ByType[schemaExisting.ClrType] = Conflict(schemaExisting, entry, schemaExisting.ClrType);
                        ByType[entry.ClrType] = Conflict(schemaExisting, entry, entry.ClrType);
                        BySchema[entry.LogicalSchemaName] = ByType[schemaExisting.ClrType];
                        continue;
                    }
                    ByType.Add(entry.ClrType, entry);
                    if (entry.ClaimsLogicalSchemaKey) BySchema[entry.LogicalSchemaName] = entry;
                }
                Contributions.Add(contributionKey);
                _contributionCount++;
                var canonical = string.Join(";", ByType.Values.OrderBy(e => e.ClrType.FullName, StringComparer.Ordinal).Select(e => e.ClrType.FullName + "|" + e.LogicalSchemaName + "|" + e.ShapeIdentity + "|" + e.IsAvailable));
                using (var sha = SHA256.Create())
                    _aggregateHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static bool TryGet(Type clrType, out ComponentMessagePackGeneratedEntry entry)
        { lock (Gate) return ByType.TryGetValue(clrType, out entry); }
        public static void Seal() { lock (Gate) _sealed = true; }
        public static ComponentMessagePackCodecSnapshot CaptureSnapshot()
        { lock (Gate) return new ComponentMessagePackCodecSnapshot(ByType.Values.OrderBy(e => e.ClrType.FullName, StringComparer.Ordinal).ToArray(), _aggregateHash, _sealed); }
        public static void ResetForSubsystemRegistration()
        { lock (Gate) { ByType.Clear(); BySchema.Clear(); Contributions.Clear(); _contributionCount = 0; _sealed = false; _aggregateHash = string.Empty; } }

        private static bool Equivalent(ComponentMessagePackGeneratedEntry a, ComponentMessagePackGeneratedEntry b)
            => a.ClrType == b.ClrType && string.Equals(a.LogicalSchemaName, b.LogicalSchemaName, StringComparison.Ordinal) && string.Equals(a.ShapeIdentity, b.ShapeIdentity, StringComparison.Ordinal) && a.IsAvailable == b.IsAvailable && a.ClaimsLogicalSchemaKey == b.ClaimsLogicalSchemaKey;
        private static ComponentMessagePackGeneratedEntry Conflict(ComponentMessagePackGeneratedEntry a, ComponentMessagePackGeneratedEntry b, Type clrType)
            => new ComponentMessagePackGeneratedEntry(clrType, a.LogicalSchemaName, a.ShapeIdentity, false, false, "MessagePack codec conflict for CLR type or logical schema.");
    }
}
