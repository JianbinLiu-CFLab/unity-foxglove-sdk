using System;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Xunit;
namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentPublisherSerializationBoundaryTests
    {
        [Fact]
        public void RecoverableFailureDropsSampleAndAllowsRecovery()
        {
            var warnings = 0;
            var ok = ComponentPublisherSerializationBoundary.TrySerialize(() => throw new ComponentPublisherSessionFailure("bad sample"), "codec.failure", _ => warnings++, out var payload, out var diagnostic);
            Assert.False(ok); Assert.Null(payload); Assert.Equal("codec.failure", diagnostic); Assert.Equal(1, warnings);
            ok = ComponentPublisherSerializationBoundary.TrySerialize(() => new byte[] { 1, 2 }, "codec.failure", _ => warnings++, out payload, out diagnostic);
            Assert.True(ok); Assert.Equal(new byte[] { 1, 2 }, payload); Assert.Equal(string.Empty, diagnostic);
        }
        [Fact]
        public void FatalExceptionsAreNotCaught()
        {
            Assert.Throws<InvalidOperationException>(() => ComponentPublisherSerializationBoundary.TrySerialize(() => throw new InvalidOperationException("fatal"), "x", null, out _, out _));
        }
    }
}
