using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.ComponentMessagePack
{
    public sealed class ComponentMessagePackRegistryTests
    {
        private sealed class Alpha { public int Value; }
        private sealed class Beta { public int Value; }

        [Fact]
        public void RegisterGeneratedIsIdempotentAndSnapshotIsDeterministic()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            var alpha = Entry(typeof(Alpha), "schema.alpha", "shape.alpha");
            var beta = Entry(typeof(Beta), "schema.beta", "shape.beta");
            var manifest = new ComponentMessagePackGeneratedManifest("assembly.z", "hash.z", new[] { beta, alpha });
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(manifest);
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(manifest);
            var snapshot = ComponentMessagePackCodecRegistry.CaptureSnapshot();
            Assert.Equal(2, snapshot.Entries.Count);
            Assert.Equal(typeof(Alpha), snapshot.Entries[0].ClrType);
            Assert.Equal(typeof(Beta), snapshot.Entries[1].ClrType);
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Alpha), out var found));
            Assert.Equal("schema.alpha", found.LogicalSchemaName);
        }

        [Fact]
        public void ConflictMarksOnlyDisputedTypeUnavailable()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("a", "1", new[] { Entry(typeof(Alpha), "schema.alpha", "shape.1") }));
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("b", "2", new[] { Entry(typeof(Alpha), "schema.other", "shape.2"), Entry(typeof(Beta), "schema.beta", "shape.beta") }));
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Beta), out var beta));
            Assert.True(beta.IsAvailable);
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Alpha), out var alpha));
            Assert.False(alpha.IsAvailable);
            Assert.Contains("conflict", alpha.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SealRejectsLateDifferentContributionAndResetClears()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("a", "1", new[] { Entry(typeof(Alpha), "schema.alpha", "shape.1") }));
            ComponentMessagePackCodecRegistry.Seal();
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("b", "2", new[] { Entry(typeof(Beta), "schema.beta", "shape.beta") }));
            Assert.False(ComponentMessagePackCodecRegistry.TryGet(typeof(Beta), out _));
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            Assert.False(ComponentMessagePackCodecRegistry.TryGet(typeof(Alpha), out _));
        }

        [Fact]
        public void IgnoreAttributeIsExplicitAndNonClaiming()
        {
            var entry = new ComponentMessagePackGeneratedEntry(typeof(Alpha), "schema.alpha", "shape.alpha", false, false, "excluded by declaration");
            Assert.False(entry.IsAvailable);
            Assert.False(entry.ClaimsLogicalSchemaKey);
            Assert.Contains("excluded", entry.Diagnostic, StringComparison.Ordinal);
        }

        [Fact]
        public void LogicalSchemaConflictMarksEachClrTypeIndependently()
        {
            ComponentMessagePackCodecRegistry.ResetForSubsystemRegistration();
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("a", "1", new[] { Entry(typeof(Alpha), "schema.same", "shape.a") }));
            ComponentMessagePackGeneratedBootstrap.RegisterGenerated(new ComponentMessagePackGeneratedManifest("b", "2", new[] { Entry(typeof(Beta), "schema.same", "shape.b") }));
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Alpha), out var alpha));
            Assert.True(ComponentMessagePackCodecRegistry.TryGet(typeof(Beta), out var beta));
            Assert.False(alpha.IsAvailable);
            Assert.False(beta.IsAvailable);
            Assert.Equal(typeof(Alpha), alpha.ClrType);
            Assert.Equal(typeof(Beta), beta.ClrType);
        }

        private static ComponentMessagePackGeneratedEntry Entry(Type type, string schema, string shape)
            => new ComponentMessagePackGeneratedEntry(type, schema, shape, true, true, string.Empty, _ => Array.Empty<byte>());
    }
}
