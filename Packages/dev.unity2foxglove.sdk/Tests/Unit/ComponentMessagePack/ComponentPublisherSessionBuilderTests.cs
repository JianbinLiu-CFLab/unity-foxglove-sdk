using System;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherSessionBuilderTests
    {
        private sealed class Publisher { }
        [Fact]
        public void BuilderNormalizesOrderAndUsesReferenceIdentity()
        {
            var a = new Publisher(); var b = new Publisher();
            var drafts = new[]
            {
                new ComponentPublisherContractDraft(b, "scene/B", typeof(Publisher), "mode", "/b", "schema.b", PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json),
                new ComponentPublisherContractDraft(a, "scene/A", typeof(Publisher), "mode", "/a", "schema.a", PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json),
                new ComponentPublisherContractDraft(a, "scene/A-duplicate", typeof(Publisher), "mode", "/dup", "schema.dup", PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json)
            };
            var snapshot = new ComponentPublisherSessionBuilder().Build(7, drafts.Reverse());
            Assert.True(snapshot.IsAvailable);
            Assert.Equal(new[] { "scene/A", "scene/B" }, snapshot.Entries.Select(e => e.CaptureIdentity));
            Assert.Equal(7, snapshot.Generation);
        }
        [Fact]
        public void UnsupportedMessagePackEntryIsUnavailableWithoutFallback()
        {
            var draft = new ComponentPublisherContractDraft(new Publisher(), "scene/A", typeof(Publisher), "mode", "/a", "schema.a", PublisherEffectiveEncoding.MsgPack, PublisherEffectiveEncoding.Unsupported);
            var entry = new ComponentPublisherSessionBuilder().Build(1, new[] { draft }).Entries.Single();
            Assert.False(entry.IsAvailable);
            Assert.Contains("unsupported", entry.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
        [Fact]
        public void OverLimitMetadataKeepsBoundedUnavailableSibling()
        {
            var draft = new ComponentPublisherContractDraft(new Publisher(), "scene/A", typeof(Publisher), "mode", new string('x', 600), "schema.a", PublisherEffectiveEncoding.Json, PublisherEffectiveEncoding.Json);
            var entry = new ComponentPublisherSessionBuilder().Build(1, new[] { draft }).Entries.Single();
            Assert.False(entry.IsAvailable);
            Assert.Contains("bounded", entry.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
    }
}
