using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    /// <summary>Immutable bounded view of one Component session entry.</summary>
    public sealed class ComponentPublisherContractProjection
    {
        internal ComponentPublisherContractProjection(ComponentPublisherSessionSnapshot snapshot, ComponentPublisherSessionEntry entry)
        {
            Generation = snapshot.Generation;
            SourceKind = "component";
            CaptureIdentity = entry.CaptureIdentity;
            PublisherType = entry.PublisherType.FullName ?? entry.PublisherType.Name;
            Mode = entry.ModeKey;
            Topic = entry.Topic;
            RequestedEncoding = PublisherEncodingPolicy.ToProtocolEncoding(entry.RequestedEncoding);
            EffectiveEncoding = PublisherEncodingPolicy.ToProtocolEncoding(entry.EffectiveEncoding);
            LogicalSchema = entry.MessagePackEntry?.LogicalSchemaName ?? entry.SchemaName;
            WireSchema = entry.EffectiveEncoding == PublisherEffectiveEncoding.MsgPack ? "schemaless" : entry.SchemaName;
            ShapeIdentity = entry.MessagePackEntry?.ShapeIdentity ?? string.Empty;
            Available = entry.IsAvailable && (entry.MessagePackEntry == null || entry.MessagePackEntry.IsAvailable);
            Diagnostic = entry.Diagnostic.Length == 0 ? entry.MessagePackEntry?.Diagnostic ?? string.Empty : entry.Diagnostic;
        }

        public long Generation { get; }
        public string SourceKind { get; }
        public string CaptureIdentity { get; }
        public string PublisherType { get; }
        public string Mode { get; }
        public string Topic { get; }
        public string RequestedEncoding { get; }
        public string EffectiveEncoding { get; }
        public string LogicalSchema { get; }
        public string WireSchema { get; }
        public string ShapeIdentity { get; }
        public bool Available { get; }
        public string Diagnostic { get; }

        internal JObject ToJson(bool includeTypeShape)
        {
            var topic = ComponentPublisherContractCatalogLimits.TruncateUtf8(Topic, ComponentPublisherContractCatalogLimits.MaxTopicBytes, out var topicTruncated);
            var diagnostic = ComponentPublisherContractCatalogLimits.TruncateUtf8(Diagnostic, ComponentPublisherContractCatalogLimits.MaxDiagnosticBytes, out var diagnosticTruncated);
            var shape = ComponentPublisherContractCatalogLimits.TruncateUtf8(ShapeIdentity, ComponentPublisherContractCatalogLimits.MaxShapeIdentityBytes, out var shapeTruncated);
            var value = new JObject
            {
                ["sourceKind"] = SourceKind,
                ["generation"] = Generation,
                ["captureIdentity"] = CaptureIdentity,
                ["publisherType"] = PublisherType,
                ["mode"] = Mode,
                ["topic"] = topic,
                ["requestedEncoding"] = RequestedEncoding,
                ["effectiveEncoding"] = EffectiveEncoding,
                ["logicalSchema"] = LogicalSchema,
                ["wireSchema"] = WireSchema,
                ["shapeIdentity"] = shape,
                ["available"] = Available,
                ["diagnostic"] = diagnostic
            };
            if (topicTruncated) value["topicTruncated"] = true;
            if (diagnosticTruncated) value["diagnosticTruncated"] = true;
            if (shapeTruncated) value["shapeIdentityTruncated"] = true;
            if (includeTypeShape) value["typeShape"] = JValue.CreateNull();
            return value;
        }
    }
}
