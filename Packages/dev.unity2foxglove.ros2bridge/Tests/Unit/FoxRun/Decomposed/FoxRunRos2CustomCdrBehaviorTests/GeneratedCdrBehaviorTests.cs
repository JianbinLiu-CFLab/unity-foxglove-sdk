// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Editor;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunRos2CustomCdrBehaviorTests
    {
        [Fact]
        public void GeneratedReaderConsumesRclpyEnvelopeWithFourOctetRmwPadding()
        {
            var serialized = Convert.FromBase64String(
                "AAEAACMAAABwaGFzZTE4Ni1leHRlcm5hbC0zYjc1ZDIwZDRhZjMyMjA1AAABAAAAAAAAALoAAAAVzVsHAwAAAAGGAQEBAAAAAQBjACMAAABwaGFzZTE4NjozYjc1ZDIwZDRhZjM6MTpleHRlcm5hbC1hAAEBAGQACwAAAGV4dGVybmFsLWEAAQEAagABAAAAAQB5AAsAAABleHRlcm5hbC1hAAECAAAAYQBrAAEAAAAAAAAAAgAAAAAAAAAB");
            var payload = serialized
                .Concat(new byte[] { 0xa5, 0x5a, 0xc3 })
                .ToArray();

            var decoded = ArtifactContract.Value.Decode(
                payload,
                "unity2foxglove.ros2bridge",
                generation: 186UL,
                markRemoteOwned: true);

            Assert.True(decoded.Success, decoded.Reason);
            Assert.Equal(new byte[] { 0x01, 0x86, 0x01 }, decoded.Values.Bytes);
            Assert.Equal(1, decoded.Values.Count);
            Assert.Equal((ushort)1, decoded.Values.Kind);
            Assert.Equal(
                "phase186:3b75d20d4af3:1:external-a",
                decoded.Values.Message);
            Assert.NotNull(decoded.Values.Nested);
            Assert.True(decoded.Values.Nested.Enabled);
            Assert.Equal("external-a", decoded.Values.Nested.Label);
            Assert.Equal(1, decoded.Values.OptionalCount);
            Assert.Equal("external-a", decoded.Values.OptionalText);
            Assert.Equal(new long[] { 1L, 2L }, decoded.Values.Values);

            var unclaimed = ArtifactContract.Value.Decode(
                payload.Concat(new byte[] { 0x7e }).ToArray(),
                "unity2foxglove.ros2bridge",
                generation: 187UL,
                markRemoteOwned: true);
            Assert.False(unclaimed.Success);
            Assert.Contains(
                "trailing",
                unclaimed.Reason,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GeneratedReaderRejectsTrailingMalformedAndOversizedPayloadsWithoutApplying()
        {
            var built = Contract.Value.Build(
                new FixtureValues { Count = 42 },
                "phase186-strict-reader",
                sequence: 1UL,
                nowNs: 0UL);
            Assert.True(built.Success, built.Reason);

            var trailing = built.Payload.Concat(new byte[] { 0xff }).ToArray();
            var trailingResult = Contract.Value.Decode(
                trailing,
                "unity2foxglove.ros2bridge",
                generation: 1UL,
                markRemoteOwned: true);
            Assert.False(trailingResult.Success);
            Assert.Contains("trailing", trailingResult.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.False(trailingResult.RemoteOwned);

            var malformed = (byte[])built.Payload.Clone();
            malformed[0] = 0x01;
            var malformedResult = Contract.Value.Decode(
                malformed,
                "unity2foxglove.ros2bridge",
                generation: 1UL,
                markRemoteOwned: true);
            Assert.False(malformedResult.Success);
            Assert.False(malformedResult.RemoteOwned);

            var oversized = new byte[MaximumPayloadBytes + 1];
            oversized[0] = 0x00;
            oversized[1] = 0x01;
            var oversizedResult = Contract.Value.Decode(
                oversized,
                "unity2foxglove.ros2bridge",
                generation: 1UL,
                markRemoteOwned: true);
            Assert.False(oversizedResult.Success);
            Assert.Contains("budget", oversizedResult.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.False(oversizedResult.RemoteOwned);
        }

        [Fact]
        public void StandardDeserializerRegistryResolvesClrTypeAndRejectsTrailingData()
        {
            var sample = Ros2CdrSampleFactory.CreateLogSample();
            var payload = Ros2CdrGeneratedSerializers.Serialize(sample);

            Assert.True(
                Ros2CdrDeserializerRegistry.TryGetByClrType(
                    typeof(Foxglove.Log),
                    out var entry));
            Assert.Equal("foxglove_msgs/msg/Log", entry.SchemaName);
            Assert.IsType<Foxglove.Log>(entry.Deserialize(payload));

            var trailing = payload.Concat(new byte[] { 0xff }).ToArray();
            Assert.Throws<InvalidDataException>(
                () => entry.Deserialize(trailing));
            Assert.False(
                Ros2CdrDeserializerRegistry.TryDeserialize(
                    entry.SchemaName,
                    trailing,
                    out _));
        }

        [Fact]
        public void StandardDeserializerConsumesRclpySerializedLogWithoutTrailingData()
        {
            var payload = Convert.FromBase64String(
                "AAEAALoAAAAVzVsHAgBvACMAAABwaGFzZTE4NjozYjc1ZDIwZDRhZjM6MTpleHRlcm5hbC1hAAAVAAAAUGhhc2UxODZFeHRlcm5hbFBlZXIAAG8AHQAAAHBoYXNlMTg2X2JyaWRnZV9saXZlX3BlZXIucHkAAGMAugAAAA==");

            Assert.True(
                Ros2CdrDeserializerRegistry.TryGetByClrType(
                    typeof(Foxglove.Log),
                    out var entry));
            var decoded = Assert.IsType<Foxglove.Log>(entry.Deserialize(payload));

            Assert.Equal(
                "phase186:3b75d20d4af3:1:external-a",
                decoded.Message);
            Assert.Equal("Phase186ExternalPeer", decoded.Name);
            Assert.Equal(186U, decoded.Line);
        }

        [Fact]
        public void GeneratedStandardSubscriberCompilesAndAppliesExactCatalogCdr()
        {
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase186",
                    "StandardCdrProbe",
                    new[] { CreateStandardSubscribeMember() }));
            Assert.Contains(
                "Ros2CdrDeserializerRegistry.TryGetByClrType",
                emitted,
                StringComparison.Ordinal);
            Assert.Contains(
                "FoxgloveRos2MsgSchemaCatalog.TryGet",
                emitted,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "System.Reflection",
                emitted,
                StringComparison.Ordinal);

            var host = @"
namespace Phase186
{
    public sealed partial class StandardCdrProbe
    {
        public global::Foxglove.Log Log;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase186StandardCdrProbe_"
                + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(host),
                    CSharpSyntaxTree.ParseText(emitted),
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(stream);
            Assert.True(
                result.Success,
                string.Join(
                    Environment.NewLine,
                    result.Diagnostics.Where(
                        diagnostic =>
                            diagnostic.Severity
                            == DiagnosticSeverity.Error)));
            stream.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
            var probeType = assembly.GetType(
                "Phase186.StandardCdrProbe",
                throwOnError: true);
            var probe = Activator.CreateInstance(probeType);
            var source = Assert.IsAssignableFrom<
                IFoxRunBridgeGeneratedSubscribeSource>(probe);
            Assert.Equal(1, source.FoxRunBridge_SubscribeBindingCount);
            Assert.True(
                source.FoxRunBridge_TryGetSubscribeBinding(
                    0,
                    out var binding,
                    out var bindingReason),
                bindingReason);
            Assert.Equal("foxglove_msgs/msg/Log", binding.CanonicalRosType);
            Assert.Equal("cdr", binding.MessageEncoding);
            Assert.Equal(
                Ros2BridgeFrameWriter.MaxPayloadBytes,
                binding.MaxPayloadBytes);
            Assert.True(
                FoxgloveRos2MsgSchemaCatalog.TryGet(
                    binding.CanonicalRosType,
                    out var schema));
            Assert.Equal(schema.SourceSha256, binding.SchemaSha256);

            var expected = Ros2CdrSampleFactory.CreateLogSample();
            var payload = Ros2CdrGeneratedSerializers.Serialize(expected);
            Assert.True(
                source.FoxRunBridge_TryDecodeAndApply(
                    0,
                    payload,
                    "unity2foxglove.ros2bridge",
                    ownershipGeneration: 17,
                    markRemoteOwned: true,
                    out var decodeReason),
                decodeReason);
            var actual = Assert.IsType<Foxglove.Log>(
                probeType.GetField(
                        "Log",
                        BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(probe));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MixedCustomAndStandardPublishTopicsBuildBothPhysicalRoutes()
        {
            const string standardTopic = "/phase186/mixed/standard";
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase186",
                    "MixedCdrProbe",
                    new[]
                    {
                        CreateScalarCustomMember(
                            "State",
                            "/phase186/mixed/custom",
                            rawMemberOrder: 0),
                        CreateStandardPublishMember(
                            "MixedCdrProbe",
                            "Log",
                            standardTopic,
                            rawMemberOrder: 1),
                    }));

            Assert.Contains(
                "Ros2CdrSerializerRegistry.TryGetByClrType",
                emitted,
                StringComparison.Ordinal);
            Assert.Contains("case 1:", emitted, StringComparison.Ordinal);

            var host = @"
namespace Phase186
{
    public sealed class MultiState
    {
        public int Count;
    }

    public partial class MixedCdrProbe
    {
        public string __foxRunOrigin = ""phase186-mixed"";
        public MultiState __foxRunCapture_0_0 = new MultiState { Count = 7 };
        public ulong __foxRunCaptureSequence_0 = 11UL;
        public global::Foxglove.Log __foxRunCapture_1_0 =
            new global::Foxglove.Log { Message = ""standard"" };
        public ulong __foxRunCaptureSequence_1 = 22UL;
        public global::Foxglove.Log Log;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase186MixedCdrProbe_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(host),
                    CSharpSyntaxTree.ParseText(emitted),
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var compilationResult = compilation.Emit(stream);
            Assert.True(
                compilationResult.Success,
                string.Join(
                    Environment.NewLine,
                    compilationResult.Diagnostics.Where(
                        diagnostic =>
                            diagnostic.Severity
                            == DiagnosticSeverity.Error)));

            stream.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(stream);
            var probe = Activator.CreateInstance(
                assembly.GetType(
                    "Phase186.MixedCdrProbe",
                    throwOnError: true));
            var source = Assert.IsAssignableFrom<
                IFoxRunBridgeGeneratedPublishSource>(probe);
            Assert.True(
                source.FoxRunBridge_TryBuildPublish(
                    1,
                    1_860_000_000UL,
                    out var route,
                    out var reason),
                reason);

            Assert.Equal(standardTopic, route.Topic);
            Assert.Equal("foxglove_msgs/msg/Log", route.LogicalSchemaName);
            Assert.Equal("cdr", route.MessageEncoding);
            Assert.Equal("ros2msg", route.SchemaEncoding);
            Assert.Equal(22UL, route.Sequence);
            Assert.Equal(
                Ros2CdrGeneratedSerializers.Serialize(
                    new Foxglove.Log { Message = "standard" }),
                route.Payload.ToArray());
        }

        [Fact]
        public void GeneratedBuilderDistinguishesNullAndEmptyMembersOnlyWithPresenceBits()
        {
            const string origin = "phase184-null-empty";
            const ulong sequence = 42UL;
            const ulong nowNs = 100UL;
            var nullValues = new FixtureValues();
            var emptyValues = new FixtureValues
            {
                Bytes = Array.Empty<byte>(),
                Labels = new List<string>(),
                Message = string.Empty,
                Nested = new NestedValues(),
                OptionalCount = 0,
                OptionalText = string.Empty,
                Values = new List<long>(),
            };

            var nullActual = Contract.Value.Build(nullValues, origin, sequence, nowNs);
            var emptyActual = Contract.Value.Build(emptyValues, origin, sequence, nowNs);
            var nullExpected = BuildOracle(nullValues, origin, sequence, nowNs);
            var emptyExpected = BuildOracle(emptyValues, origin, sequence, nowNs);

            Assert.True(nullActual.Success, nullActual.Reason);
            Assert.True(emptyActual.Success, emptyActual.Reason);
            Assert.Equal(nullExpected.Bytes, nullActual.Payload);
            Assert.Equal(emptyExpected.Bytes, emptyActual.Payload);

            var topLevelPresence = new[]
            {
                "bytes",
                "labels",
                "message",
                "nested",
                "optional_count",
                "optional_text",
                "values",
            };
            var normalizedEmpty = (byte[])emptyActual.Payload.Clone();
            foreach (var field in topLevelPresence)
            {
                var nullOffset = nullExpected.PresenceOffsets[field];
                var emptyOffset = emptyExpected.PresenceOffsets[field];
                Assert.Equal(nullOffset, emptyOffset);
                Assert.Equal((byte)0, nullActual.Payload[nullOffset]);
                Assert.Equal((byte)1, emptyActual.Payload[emptyOffset]);
                normalizedEmpty[emptyOffset] = 0;
            }

            Assert.Equal(nullActual.Payload, normalizedEmpty);
        }


    }
}
