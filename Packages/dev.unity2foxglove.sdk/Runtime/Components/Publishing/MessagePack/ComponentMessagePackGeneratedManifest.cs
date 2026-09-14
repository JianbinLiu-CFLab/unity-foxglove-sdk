using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Components.Publishing.MessagePack
{
    public sealed class ComponentMessagePackGeneratedManifest
    {
        public ComponentMessagePackGeneratedManifest(string assemblyIdentity, string manifestHash, IEnumerable<ComponentMessagePackGeneratedEntry> entries)
        {
            AssemblyIdentity = assemblyIdentity ?? string.Empty;
            ManifestHash = manifestHash ?? string.Empty;
            Entries = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        }
        public string AssemblyIdentity { get; }
        public string ManifestHash { get; }
        public IReadOnlyList<ComponentMessagePackGeneratedEntry> Entries { get; }
    }
}
