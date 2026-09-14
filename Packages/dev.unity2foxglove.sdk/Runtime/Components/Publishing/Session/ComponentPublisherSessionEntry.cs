using System;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherSessionEntry
    {
        internal ComponentPublisherSessionEntry(ComponentPublisherContractDraft draft, bool available, string diagnostic)
        { Publisher = draft.Publisher; CaptureIdentity = draft.CaptureIdentity; PublisherType = draft.PublisherType; ModeKey = draft.ModeKey; Topic = draft.Topic; SchemaName = draft.SchemaName; RequestedEncoding = draft.RequestedEncoding; EffectiveEncoding = draft.EffectiveEncoding; MessagePackEntry = draft.MessagePackEntry; IsAvailable = available; Diagnostic = diagnostic ?? string.Empty; }
        public object Publisher { get; }
        public string CaptureIdentity { get; }
        public Type PublisherType { get; }
        public string ModeKey { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public PublisherEffectiveEncoding RequestedEncoding { get; }
        public PublisherEffectiveEncoding EffectiveEncoding { get; }
        public ComponentMessagePackGeneratedEntry MessagePackEntry { get; }
        public bool IsAvailable { get; }
        public string Diagnostic { get; }
    }
}
