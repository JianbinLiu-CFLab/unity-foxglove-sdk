using System;
using Unity.FoxgloveSDK.Components;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public readonly struct ComponentPublisherChannelDescriptor : IEquatable<ComponentPublisherChannelDescriptor>
    {
        public ComponentPublisherChannelDescriptor(string sourceKind, string topic, PublisherEffectiveEncoding encoding, string schema, string shape, long generation)
        { SourceKind=sourceKind??string.Empty; Topic=topic??string.Empty; Encoding=encoding; SchemaName=schema??string.Empty; ShapeIdentity=shape??string.Empty; Generation=generation; }
        public string SourceKind { get; } public string Topic { get; } public PublisherEffectiveEncoding Encoding { get; } public string SchemaName { get; } public string ShapeIdentity { get; } public long Generation { get; }
        public bool Equals(ComponentPublisherChannelDescriptor other) => SourceKind==other.SourceKind && Topic==other.Topic && Encoding==other.Encoding && SchemaName==other.SchemaName && ShapeIdentity==other.ShapeIdentity && Generation==other.Generation;
        public override bool Equals(object obj) => obj is ComponentPublisherChannelDescriptor other && Equals(other);
        public override int GetHashCode() => (SourceKind, Topic, Encoding, SchemaName, ShapeIdentity, Generation).GetHashCode();
    }
}
