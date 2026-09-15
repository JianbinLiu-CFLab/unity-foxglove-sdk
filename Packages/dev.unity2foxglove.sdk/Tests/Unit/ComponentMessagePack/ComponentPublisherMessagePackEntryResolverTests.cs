using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherMessagePackEntryResolverTests
    {
        private sealed class Telemetry { public int Value; }

        [Fact]
        public void ResolvesGeneratedEntryForMessagePackPublisherType()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            var expected = new ComponentMessagePackGeneratedEntry(
                typeof(Telemetry), "demo.Telemetry", "shape.v1", true, true, string.Empty,
                value => new[] { (byte)((Telemetry)value).Value });
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(
                new ComponentMessagePackGeneratedManifest("fixture", "hash", new[] { expected }));

            var actual = ComponentPublisherMessagePackEntryResolver.Resolve(
                typeof(Telemetry), PublisherEffectiveEncoding.MsgPack);

            Assert.Same(expected, actual);
            Assert.Equal(new byte[] { 7 }, actual.Serialize(new Telemetry { Value = 7 }));
        }

        [Fact]
        public void DoesNotResolveCodecWhenEffectiveEncodingIsNotMessagePack()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(
                new ComponentMessagePackGeneratedManifest(
                    "fixture", "hash", new[]
                    {
                        new ComponentMessagePackGeneratedEntry(
                            typeof(Telemetry), "demo.Telemetry", "shape.v1", true, true, string.Empty,
                            _ => new byte[] { 7 })
                    }));

            Assert.Null(ComponentPublisherMessagePackEntryResolver.Resolve(
                typeof(Telemetry), PublisherEffectiveEncoding.Json));
            Assert.Null(ComponentPublisherMessagePackEntryResolver.Resolve(
                null, PublisherEffectiveEncoding.MsgPack));
        }
    }
}
