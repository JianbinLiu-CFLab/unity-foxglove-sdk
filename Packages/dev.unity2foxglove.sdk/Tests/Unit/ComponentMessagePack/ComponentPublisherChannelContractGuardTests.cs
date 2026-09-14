using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherChannelContractGuardTests
    {
        [Fact]
        public void SameDescriptorIsIdempotentAndConflictDoesNotAddClaim()
        {
            var guard = new ComponentPublisherChannelContractGuard();
            var a = new ComponentPublisherChannelDescriptor("typed", "/telemetry", PublisherEffectiveEncoding.MsgPack, "demo.Telemetry", "shape.v1", 3);
            Assert.True(guard.TryClaim(a, out _));
            Assert.True(guard.TryClaim(a, out _));
            Assert.Equal(1, guard.Count);
            var conflicting = new ComponentPublisherChannelDescriptor("raw", "/telemetry", PublisherEffectiveEncoding.Json, "", "", 3);
            Assert.False(guard.TryClaim(conflicting, out var reason));
            Assert.Contains("conflict", reason);
            Assert.Equal(1, guard.Count);
        }
        [Fact]
        public void ClearRemovesSidecarsWithoutOwningChannelIds()
        {
            var guard = new ComponentPublisherChannelContractGuard();
            Assert.True(guard.TryClaim(new ComponentPublisherChannelDescriptor("typed", "/a", PublisherEffectiveEncoding.Json, "s", "", 1), out _));
            guard.Clear();
            Assert.Equal(0, guard.Count);
        }
    }
}
