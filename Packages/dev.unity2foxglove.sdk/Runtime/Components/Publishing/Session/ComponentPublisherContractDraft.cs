using System;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
namespace Unity.FoxgloveSDK.Components.Publishing.Session
{
    public sealed class ComponentPublisherContractDraft
    {
        public ComponentPublisherContractDraft(object publisher, string captureIdentity, Type publisherType, string modeKey, string topic, string schemaName, PublisherEffectiveEncoding requestedEncoding, PublisherEffectiveEncoding effectiveEncoding, ComponentMessagePackGeneratedEntry messagePackEntry = null)
        { Publisher = publisher ?? throw new ArgumentNullException(nameof(publisher)); CaptureIdentity = captureIdentity ?? string.Empty; PublisherType = publisherType ?? publisher.GetType(); ModeKey = modeKey ?? string.Empty; Topic = topic ?? string.Empty; SchemaName = schemaName ?? string.Empty; RequestedEncoding = requestedEncoding; EffectiveEncoding = effectiveEncoding; MessagePackEntry = messagePackEntry; }
        public object Publisher { get; }
        public string CaptureIdentity { get; }
        public Type PublisherType { get; }
        public string ModeKey { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public PublisherEffectiveEncoding RequestedEncoding { get; }
        public PublisherEffectiveEncoding EffectiveEncoding { get; }
        public ComponentMessagePackGeneratedEntry MessagePackEntry { get; }
    }
}
