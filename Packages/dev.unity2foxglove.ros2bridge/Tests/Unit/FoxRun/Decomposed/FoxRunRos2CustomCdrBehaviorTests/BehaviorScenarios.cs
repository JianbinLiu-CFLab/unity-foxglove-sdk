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
        public void BridgeContributionExposesOneTypedProviderPublishRoute()
        {
            var source = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase181",
                    "GeneratedCdrProbe",
                    new[] { CreateCustomMember() }));

            Assert.Contains(
                "IFoxRunBridgeGeneratedPublishSource",
                source);
            Assert.Contains(
                "FoxRunBridge_TryBuildPublish(int topicIndex, ulong nowNs",
                source);
            Assert.Contains(
                "new global::Unity.FoxgloveSDK.Components.FoxRunTransportPublishRoute(",
                source);
            Assert.Contains(
                "\"cdr\"",
                source);
            Assert.Contains(
                "\"ros2msg\"",
                source);
            Assert.DoesNotContain(
                "System.Reflection",
                source);
        }

        [Fact]
        public void MultipleBridgePublishTopicsCompileWithoutSwitchLocalCollisions()
        {
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase186",
                    "MultiCdrProbe",
                    new[]
                    {
                        CreateScalarCustomMember(
                            "StateA",
                            "/phase186/multi/a",
                            rawMemberOrder: 0),
                        CreateScalarCustomMember(
                            "StateB",
                            "/phase186/multi/b",
                            rawMemberOrder: 1),
                    }));
            var host = @"
namespace Phase186
{
    public sealed class MultiState
    {
        public int Count;
    }

    public partial class MultiCdrProbe
    {
        private readonly string __foxRunOrigin;
        private readonly MultiState __foxRunCapture_0_0;
        private readonly MultiState __foxRunCapture_1_0;
        private readonly ulong __foxRunCaptureSequence_0;
        private readonly ulong __foxRunCaptureSequence_1;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase186MultiCdrProbe_" + Guid.NewGuid().ToString("N"),
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
        }

        [Fact]
        public void NestedValueTypeMemberCompilesForPublishAndSubscribe()
        {
            var vector = FoxRunTypeShape.Object(
                "UnityEngine.Vector3",
                new[]
                {
                    new FoxRunTypeField(
                        "x",
                        "x",
                        FoxRunTypeShape.Canonical("float32")),
                    new FoxRunTypeField(
                        "y",
                        "y",
                        FoxRunTypeShape.Canonical("float32")),
                    new FoxRunTypeField(
                        "z",
                        "z",
                        FoxRunTypeShape.Canonical("float32")),
                },
                canConstruct: true,
                isValueType: true);
            var state = FoxRunTypeShape.Object(
                "Phase181.State",
                new[]
                {
                    new FoxRunTypeField(
                        "position",
                        "Position",
                        vector),
                },
                canConstruct: true);
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase181",
                    "GeneratedCdrProbe",
                    new[]
                    {
                        CreateCustomMember(
                            FoxRunFlow.PublishAndSubscribe,
                            state),
                    }));
            var host = @"
namespace Phase181
{
    public sealed class State
    {
        public global::UnityEngine.Vector3 Position;
    }

    public sealed partial class GeneratedCdrProbe :
        global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource
    {
        private readonly string __foxRunOrigin;
        private readonly State __foxRunCapture_0_0;
        private readonly ulong __foxRunCaptureSequence_0;
        public State State;

        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_MarkRemoteApplied(
            int topicIndex, string transportId, ulong generation) { }
        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_ClearRemoteApplied(
            int topicIndex, string transportId, ulong generation) { }
        bool global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_TryGetRemoteApplied(
            int topicIndex, out string transportId, out ulong generation)
        {
            transportId = string.Empty;
            generation = 0;
            return false;
        }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase187ValueTypeNestedCdr_"
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
        }

        [Fact]
        public void BridgeContributionExposesDeterministicTypedSubscribeBindingWithoutReflection()
        {
            var source = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                    "Phase181",
                    "GeneratedCdrProbe",
                    new[] { CreateCustomMember(FoxRunFlow.PublishAndSubscribe) }));

            Assert.Contains(
                "IFoxRunBridgeGeneratedSubscribeSource",
                source);
            Assert.Contains(
                "FoxRunBridge_TryGetSubscribeBinding",
                source);
            Assert.Contains(
                "FoxRunBridge_TryDecodeAndApply",
                source);
            Assert.Contains(
                "EnsureFullyConsumed",
                source);
            Assert.DoesNotContain(
                "System.Reflection",
                source);
        }

        [Fact]
        public void GeneratedBuilderMatchesIndependentPhase181StyleOracleExactly()
        {
            const string origin = "phase184-origin-probe";
            const ulong sequence = 0x0102030405060708UL;
            const ulong nowNs = 1_234_567_890_123_456_789UL;
            var values = new FixtureValues
            {
                Bytes = new byte[] { 0xa5, 0x5a, 0x00 },
                Count = -123_456_789,
                Kind = 0xbeef,
                Kinds = new[] { -1, 0, 1 },
                Labels = new List<string> { null, string.Empty, "A\u03a9" },
                Message = "message-\u4e2d",
                Nested = new NestedValues
                {
                    Enabled = true,
                    Label = "nested-\u03a9",
                },
                OptionalCount = int.MinValue,
                OptionalText = string.Empty,
                Values = new List<long>
                {
                    long.MinValue,
                    0x0102030405060708L,
                },
            };

            var actual = Contract.Value.Build(values, origin, sequence, nowNs);
            var expected = BuildOracle(values, origin, sequence, nowNs);

            Assert.True(actual.Success, actual.Reason);
            Assert.Equal(string.Empty, actual.Reason);
            Assert.Equal(expected.Bytes, actual.Payload);
            Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x00 }, actual.Payload.Take(4).ToArray());
        }

        [Fact]
        public void GeneratedProviderRouteCarriesExactCdrIdentityAndCapturedMetadata()
        {
            const string origin = "phase186-provider-route";
            const ulong sequence = 186UL;
            const ulong nowNs = 1_860_000_000UL;
            var values = new FixtureValues
            {
                Count = 42,
                Message = "bridge"
            };

            var result = Contract.Value.BuildRoute(
                values,
                origin,
                sequence,
                nowNs);
            var oracle = BuildOracle(
                values,
                origin,
                sequence,
                nowNs);

            Assert.True(result.Success, result.Reason);
            Assert.Equal(Topic, result.Route.Topic);
            Assert.Equal(nowNs, result.Route.LogTimeNs);
            Assert.Equal(sequence, result.Route.Sequence);
            Assert.Equal("cdr", result.Route.MessageEncoding);
            Assert.Equal("ros2msg", result.Route.SchemaEncoding);
            Assert.StartsWith(
                "unity2foxglove_foxrun_interfaces_v1/msg/",
                result.Route.LogicalSchemaName);
            Assert.EndsWith(
                "Envelope",
                result.Route.LogicalSchemaName);
            Assert.Equal(oracle.Bytes, result.Route.Payload.ToArray());
        }

        [Fact]
        public void GeneratedReaderRoundTripsNestedNullableAndSequenceValuesAndMarksExactOriginToken()
        {
            const string origin = "phase186-inbound-roundtrip";
            const ulong sequence = 1862UL;
            const ulong nowNs = 1_862_000_000UL;
            var values = new FixtureValues
            {
                Bytes = new byte[] { 0, 1, 255 },
                Count = -17,
                Kind = 0xbeef,
                Kinds = new[] { -1, 0, 1 },
                Labels = new List<string> { string.Empty, "A\u03a9" },
                Message = "message-\u4e2d",
                Nested = new NestedValues
                {
                    Enabled = true,
                    Label = "nested-\u03a9",
                },
                OptionalCount = int.MinValue,
                OptionalText = string.Empty,
                Values = new List<long> { long.MinValue, 0, long.MaxValue },
            };
            var payload = Contract.Value.Build(
                values,
                origin,
                sequence,
                nowNs);

            Assert.True(payload.Success, payload.Reason);
            var decoded = Contract.Value.Decode(
                payload.Payload,
                "unity2foxglove.ros2bridge",
                generation: 73UL,
                markRemoteOwned: true);

            Assert.True(decoded.Success, decoded.Reason);
            Assert.Equal(values.Bytes, decoded.Values.Bytes);
            Assert.Equal(values.Count, decoded.Values.Count);
            Assert.Equal(values.Kind, decoded.Values.Kind);
            Assert.Equal(values.Kinds, decoded.Values.Kinds);
            Assert.Equal(values.Labels, decoded.Values.Labels);
            Assert.Equal(values.Message, decoded.Values.Message);
            Assert.NotNull(decoded.Values.Nested);
            Assert.Equal(values.Nested.Enabled, decoded.Values.Nested.Enabled);
            Assert.Equal(values.Nested.Label, decoded.Values.Nested.Label);
            Assert.Equal(values.OptionalCount, decoded.Values.OptionalCount);
            Assert.Equal(values.OptionalText, decoded.Values.OptionalText);
            Assert.Equal(values.Values, decoded.Values.Values);
            Assert.True(decoded.RemoteOwned);
            Assert.Equal(
                "unity2foxglove.ros2bridge",
                decoded.RemoteTransportId);
            Assert.Equal(73UL, decoded.RemoteGeneration);
        }

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

        [Fact]
        public void GeneratedBuilderReadsPresenceBearingPropertyExactlyOnce()
        {
            var actual = Contract.Value.Build(
                new FixtureValues { OptionalText = "single-read" },
                "phase184-single-read",
                184UL,
                184UL);

            Assert.True(actual.Success, actual.Reason);
            Assert.Equal(1, actual.OptionalTextReadCount);
        }

        [Fact]
        public void GeneratedBuilderNormalizesNullStringSequenceElementsToEmptyStrings()
        {
            const string origin = "phase184-string-sequence";
            const ulong sequence = 7UL;
            const ulong nowNs = 2_000_000_003UL;
            var values = new FixtureValues
            {
                Labels = new List<string> { null, string.Empty, "\u00df", "\u4e2d" },
            };

            var actual = Contract.Value.Build(values, origin, sequence, nowNs);
            var expected = BuildOracle(values, origin, sequence, nowNs);

            Assert.True(actual.Success, actual.Reason);
            Assert.Equal(expected.Bytes, actual.Payload);
            Assert.Equal((byte)1, actual.Payload[expected.PresenceOffsets["labels"]]);
        }

        [Fact]
        public void GeneratedBuilderRejectsSequenceAboveTheDeclaredItemBudget()
        {
            var values = new FixtureValues
            {
                Bytes = new byte[
                    FoxRunBridgeCustomDtoBudgetPolicy.MaximumSequenceItems + 1],
            };

            var actual = Contract.Value.Build(
                values,
                "phase187-sequence-budget",
                sequence: 1UL,
                nowNs: 0UL);

            Assert.False(actual.Success);
            Assert.Null(actual.Payload);
            Assert.Contains(
                "item budget",
                actual.Reason,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GeneratedBuilderAcceptsTheItemLimitAndRetainsTheFourMiBByteBudget()
        {
            const string origin = "phase184-four-mib-boundary";
            const ulong sequence = 1UL;
            const ulong nowNs = 0UL;
            var boundaryValues = new FixtureValues
            {
                Bytes = new byte[
                    FoxRunBridgeCustomDtoBudgetPolicy.MaximumSequenceItems],
            };
            var accepted = Contract.Value.Build(boundaryValues, origin, sequence, nowNs);
            Assert.True(accepted.Success, accepted.Reason);
            Assert.InRange(accepted.Payload.Length, 1, MaximumPayloadBytes);

            boundaryValues.Bytes = Array.Empty<byte>();
            boundaryValues.Message = new string('x', MaximumPayloadBytes);
            var rejected = Contract.Value.Build(boundaryValues, origin, sequence, nowNs);
            Assert.False(rejected.Success);
            Assert.Null(rejected.Payload);
            Assert.Contains(
                "byte budget",
                rejected.Reason,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
