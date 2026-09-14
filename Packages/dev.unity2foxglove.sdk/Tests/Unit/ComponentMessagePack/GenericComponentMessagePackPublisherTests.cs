using System;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class GenericComponentMessagePackPublisherTests
    {
        private sealed class Telemetry { public int Value; }
        [Fact]
        public void GeneratedEntryIsAuthoritativeForGenericPayload()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            var entry = new ComponentMessagePackGeneratedEntry(typeof(Telemetry), "demo.Telemetry", "shape.v1", true, true, string.Empty, value => new byte[] { (byte)((Telemetry)value).Value });
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("fixture", "hash", new[] { entry }));
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Telemetry), out var resolved));
            Assert.True(resolved.IsAvailable);
            Assert.Equal(new byte[] { 7 }, resolved.Serialize(new Telemetry { Value = 7 }));
        }
        [Fact]
        public void UnsupportedGenericPayloadDoesNotFallbackToAnotherCodec()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            var entry = new ComponentMessagePackGeneratedEntry(typeof(Telemetry), "demo.Telemetry", "", false, false, "unsupported typed MessagePack member or shape");
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("fixture", "hash", new[] { entry }));
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Telemetry), out var resolved));
            Assert.False(resolved.IsAvailable);
            Assert.Throws<InvalidOperationException>(() => resolved.Serialize(new Telemetry()));
        }
    }
}
