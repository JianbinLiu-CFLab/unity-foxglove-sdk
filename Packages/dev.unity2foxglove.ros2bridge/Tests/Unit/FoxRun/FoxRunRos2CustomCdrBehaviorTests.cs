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
    public sealed class FoxRunRos2CustomCdrBehaviorTests
    {
        private const int MaximumPayloadBytes = 4 * 1024 * 1024;
        private const string Topic = "/phase184/custom-cdr";
        private static readonly Lazy<GeneratedContract> Contract =
            new Lazy<GeneratedContract>(CompileGeneratedContract);
        private static readonly Lazy<GeneratedContract> ArtifactContract =
            new Lazy<GeneratedContract>(
                () => CompileGeneratedContract(
                    CreateArtifactCustomMember(
                        FoxRunFlow.PublishAndSubscribe)));

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

        private static GeneratedContract CompileGeneratedContract()
        {
            return CompileGeneratedContract(
                CreateCustomMember(FoxRunFlow.PublishAndSubscribe));
        }

        private static GeneratedContract CompileGeneratedContract(
            FoxRunGenerationMember member)
        {
            var emitted = FoxRunBridgeSourceEmitter.EmitBridgeContribution(
                new FoxRunGenerationType(
                "Phase181",
                "GeneratedCdrProbe",
                    new[] { member }));

            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("namespace Phase181");
            source.AppendLine("{");
            source.AppendLine("    public enum StateKind { Faulted = -1, Unknown = 0, Ready = 1 }");
            source.AppendLine("    public sealed class NestedState");
            source.AppendLine("    {");
            source.AppendLine("        public bool Enabled;");
            source.AppendLine("        public string Label;");
            source.AppendLine("    }");
            source.AppendLine("    public sealed class State");
            source.AppendLine("    {");
            source.AppendLine("        public byte[] Bytes;");
            source.AppendLine("        public int Count;");
            source.AppendLine("        public StateKind Kind;");
            source.AppendLine("        public StateKind[] Kinds;");
            source.AppendLine("        public List<string> Labels;");
            source.AppendLine("        public string Message;");
            source.AppendLine("        public NestedState Nested;");
            source.AppendLine("        public int? OptionalCount;");
            source.AppendLine("        private string _optionalText;");
            source.AppendLine("        public int OptionalTextReadCount;");
            source.AppendLine("        public string OptionalText");
            source.AppendLine("        {");
            source.AppendLine("            get { OptionalTextReadCount++; return _optionalText; }");
            source.AppendLine("            set { _optionalText = value; }");
            source.AppendLine("        }");
            source.AppendLine("        public List<long> Values;");
            source.AppendLine("    }");
            source.AppendLine("    public sealed partial class GeneratedCdrProbe : global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource");
            source.AppendLine("    {");
            source.AppendLine("        private readonly string __foxRunOrigin;");
            source.AppendLine("        private readonly State __foxRunCapture_0_0;");
            source.AppendLine("        private readonly ulong __foxRunCaptureSequence_0;");
            source.AppendLine("        public State State;");
            source.AppendLine("        public bool RemoteOwned;");
            source.AppendLine("        public string RemoteTransportId;");
            source.AppendLine("        public ulong RemoteGeneration;");
            source.AppendLine("        public GeneratedCdrProbe(string origin, State source, ulong sequence)");
            source.AppendLine("        {");
            source.AppendLine("            __foxRunOrigin = origin;");
            source.AppendLine("            __foxRunCapture_0_0 = source;");
            source.AppendLine("            __foxRunCaptureSequence_0 = sequence;");
            source.AppendLine("            State = source;");
            source.AppendLine("        }");
            source.AppendLine("        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_MarkRemoteApplied(int topicIndex, string transportId, ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            RemoteOwned = true;");
            source.AppendLine("            RemoteTransportId = transportId ?? string.Empty;");
            source.AppendLine("            RemoteGeneration = generation;");
            source.AppendLine("        }");
            source.AppendLine("        void global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_ClearRemoteApplied(int topicIndex, string transportId, ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            if (!RemoteOwned || !string.Equals(RemoteTransportId, transportId ?? string.Empty, StringComparison.Ordinal) || RemoteGeneration != generation) return;");
            source.AppendLine("            RemoteOwned = false;");
            source.AppendLine("            RemoteTransportId = null;");
            source.AppendLine("            RemoteGeneration = 0;");
            source.AppendLine("        }");
            source.AppendLine("        bool global::Unity.FoxgloveSDK.Components.IFoxRunRemoteOwnershipSource.FoxRunOrigin_TryGetRemoteApplied(int topicIndex, out string transportId, out ulong generation)");
            source.AppendLine("        {");
            source.AppendLine("            transportId = RemoteTransportId ?? string.Empty;");
            source.AppendLine("            generation = RemoteGeneration;");
            source.AppendLine("            return RemoteOwned;");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");
            source.Append(emitted);

            var compilation = CSharpCompilation.Create(
                "Phase184CustomCdrBehavior_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source.ToString(),
                        new CSharpParseOptions(LanguageVersion.CSharp9)),
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));
            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            if (!emit.Success)
            {
                throw new InvalidOperationException(
                    "Generated custom CDR fixture failed to compile: "
                    + string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString()))
                    + Environment.NewLine
                    + source);
            }

            image.Position = 0;
            return new GeneratedContract(AssemblyLoadContext.Default.LoadFromStream(image));
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                    ?? string.Empty;
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(Ros2CdrWriter).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static FoxRunGenerationMember CreateCustomMember(
            FoxRunFlow flow = FoxRunFlow.Publish)
        {
            var nested = FoxRunTypeShape.Object(
                "Phase181.NestedState",
                new[]
                {
                    new FoxRunTypeField(
                        "label",
                        "Label",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "enabled",
                        "Enabled",
                        FoxRunTypeShape.Canonical("bool")),
                },
                canConstruct: true);
            var state = FoxRunTypeShape.Object(
                "Phase181.State",
                new[]
                {
                    new FoxRunTypeField(
                        "values",
                        "Values",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.List,
                            FoxRunTypeShape.Canonical("int64"))),
                    new FoxRunTypeField(
                        "optionalText",
                        "OptionalText",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "nested",
                        "Nested",
                        nested.WithNullable()),
                    new FoxRunTypeField(
                        "count",
                        "Count",
                        FoxRunTypeShape.Canonical("int32")),
                    new FoxRunTypeField(
                        "labels",
                        "Labels",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.List,
                            FoxRunTypeShape.Canonical("string"))),
                    new FoxRunTypeField(
                        "kind",
                        "Kind",
                        FoxRunTypeShape.Enum(
                            "Phase181.StateKind",
                            new[]
                            {
                                new FoxRunEnumValue("Unknown", 0),
                            })),
                    new FoxRunTypeField(
                        "kinds",
                        "Kinds",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.Array,
                            FoxRunTypeShape.Enum(
                                "Phase181.StateKind",
                                new[]
                                {
                                    new FoxRunEnumValue("Faulted", -1),
                                    new FoxRunEnumValue("Unknown", 0),
                                    new FoxRunEnumValue("Ready", 1),
                                }))),
                    new FoxRunTypeField(
                        "bytes",
                        "Bytes",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.Binary,
                            FoxRunTypeShape.Canonical("uint8"))),
                    new FoxRunTypeField(
                        "message",
                        "Message",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "optionalCount",
                        "OptionalCount",
                        FoxRunTypeShape.Canonical(
                            "int32",
                            nullable: true),
                        isNullable: true),
                },
                canConstruct: true);
            return CreateCustomMember(flow, state);
        }

        private static FoxRunGenerationMember CreateArtifactCustomMember(
            FoxRunFlow flow)
        {
            var nested = FoxRunTypeShape.Object(
                "Phase181.NestedState",
                new[]
                {
                    new FoxRunTypeField(
                        "label",
                        "Label",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "enabled",
                        "Enabled",
                        FoxRunTypeShape.Canonical("bool")),
                },
                canConstruct: true);
            var state = FoxRunTypeShape.Object(
                "Phase181.State",
                new[]
                {
                    new FoxRunTypeField(
                        "values",
                        "Values",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.List,
                            FoxRunTypeShape.Canonical("int64"))),
                    new FoxRunTypeField(
                        "optionalText",
                        "OptionalText",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "nested",
                        "Nested",
                        nested.WithNullable()),
                    new FoxRunTypeField(
                        "count",
                        "Count",
                        FoxRunTypeShape.Canonical("int32")),
                    new FoxRunTypeField(
                        "kind",
                        "Kind",
                        FoxRunTypeShape.Enum(
                            "Phase181.StateKind",
                            new[]
                            {
                                new FoxRunEnumValue("Unknown", 0),
                            },
                            underlyingCanonicalType: "uint16")),
                    new FoxRunTypeField(
                        "bytes",
                        "Bytes",
                        FoxRunTypeShape.Collection(
                            FoxRunCollectionKind.Binary,
                            FoxRunTypeShape.Canonical("uint8"))),
                    new FoxRunTypeField(
                        "message",
                        "Message",
                        FoxRunTypeShape.Canonical(
                            "string",
                            nullable: true)),
                    new FoxRunTypeField(
                        "optionalCount",
                        "OptionalCount",
                        FoxRunTypeShape.Canonical(
                            "int32",
                            nullable: true),
                        isNullable: true),
                },
                canConstruct: true);
            return CreateCustomMember(flow, state);
        }

        private static FoxRunGenerationMember CreateCustomMember(
            FoxRunFlow flow,
            FoxRunTypeShape state)
            => new FoxRunGenerationMember(
                "Phase181",
                "GeneratedCdrProbe",
                "State",
                "Field",
                "Phase181.State",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic: Topic,
                hz: 10f,
                schemaName: "phase181.State",
                policy: (int)FoxRunPolicy.Trigger,
                tolerance: 0f,
                hostKind: "Field",
                rawMemberOrder: 0,
                conditionalSymbols: string.Empty,
                mode: (int)flow,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                typeShape: state,
                generatesWebSocketCodec: false,
                publishTransportIds: new[]
                {
                    "unity2foxglove.ros2bridge",
                },
                subscribeTransportId:
                    flow == FoxRunFlow.Publish
                        ? null
                        : "unity2foxglove.ros2bridge");

        private static FoxRunGenerationMember CreateScalarCustomMember(
            string memberName,
            string topic,
            int rawMemberOrder)
            => new FoxRunGenerationMember(
                "Phase186",
                "MultiCdrProbe",
                memberName,
                "Field",
                "Phase186.MultiState",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic,
                hz: 10f,
                schemaName: "phase186.MultiState",
                policy: (int)FoxRunPolicy.Trigger,
                tolerance: 0f,
                hostKind: "Field",
                rawMemberOrder,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.Publish,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                typeShape: FoxRunTypeShape.Object(
                    "Phase186.MultiState",
                    new[]
                    {
                        new FoxRunTypeField(
                            "count",
                            "Count",
                            FoxRunTypeShape.Canonical("int32")),
                    },
                    canConstruct: true),
                generatesWebSocketCodec: false,
                publishTransportIds: new[]
                {
                    "unity2foxglove.ros2bridge",
                });

        private static FoxRunGenerationMember
            CreateStandardSubscribeMember()
            => new FoxRunGenerationMember(
                "Phase186",
                "StandardCdrProbe",
                "Log",
                "Field",
                "Foxglove.Log",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic: "/phase186/standard-cdr",
                hz: 10f,
                schemaName: "foxglove.Log",
                policy: (int)FoxRunPolicy.Trigger,
                tolerance: 0f,
                hostKind: "Field",
                rawMemberOrder: 0,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.Subscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                typeShape: FoxRunTypeShape.Object(
                    "Foxglove.Log",
                    Array.Empty<FoxRunTypeField>(),
                    canConstruct: true),
                generatesWebSocketCodec: false,
                publishTransportIds: Array.Empty<string>(),
                subscribeTransportId:
                    "unity2foxglove.ros2bridge");

        private static FoxRunGenerationMember CreateStandardPublishMember(
            string className,
            string memberName,
            string topic,
            int rawMemberOrder)
            => new FoxRunGenerationMember(
                "Phase186",
                className,
                memberName,
                "Field",
                "Foxglove.Log",
                isValueType: false,
                isArray: false,
                elementTypeName: string.Empty,
                topic,
                hz: 10f,
                schemaName: "foxglove.Log",
                policy: (int)FoxRunPolicy.Trigger,
                tolerance: 0f,
                hostKind: "Field",
                rawMemberOrder,
                conditionalSymbols: string.Empty,
                mode: (int)FoxRunFlow.PublishAndSubscribe,
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                typeShape: FoxRunTypeShape.Object(
                    "Foxglove.Log",
                    Array.Empty<FoxRunTypeField>(),
                    canConstruct: true),
                generatesWebSocketCodec: false,
                publishTransportIds: new[]
                {
                    "unity2foxglove.ros2bridge",
                },
                subscribeTransportId:
                    "unity2foxglove.ros2bridge");

        private static OracleResult BuildOracle(
            FixtureValues values,
            string origin,
            ulong sequence,
            ulong nowNs)
        {
            var writer = new OracleByteWriter();
            var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
            WriteCanonicalEnvelope(writer, values, origin, sequence, nowNs, offsets);
            return new OracleResult(writer.ToArray(), offsets);
        }

        private static void WriteCanonicalEnvelope(
            IOracleWriter writer,
            FixtureValues values,
            string origin,
            ulong sequence,
            ulong nowNs,
            IDictionary<string, int> presenceOffsets)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));
            var seconds = nowNs / 1_000_000_000UL;
            if (seconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(nowNs));

            writer.WriteString(origin);
            writer.WriteUInt64(sequence);
            writer.WriteInt32((int)seconds);
            writer.WriteUInt32((uint)(nowNs % 1_000_000_000UL));

            var byteCount = values.Bytes == null ? 0 : values.Bytes.Length;
            writer.WriteByteSequence(values.Bytes, byteCount);
            RecordPresence(writer, presenceOffsets, "bytes", values.Bytes != null);

            writer.WriteInt32(values.Count);
            writer.WriteInt32(values.Kind);

            writer.WriteSequenceLength(values.Kinds == null ? 0 : values.Kinds.Length);
            if (values.Kinds != null)
            {
                for (var index = 0; index < values.Kinds.Length; index++)
                    writer.WriteInt32(values.Kinds[index]);
            }
            RecordPresence(writer, presenceOffsets, "kinds", values.Kinds != null);

            writer.WriteSequenceLength(values.Labels == null ? 0 : values.Labels.Count);
            if (values.Labels != null)
            {
                for (var index = 0; index < values.Labels.Count; index++)
                    writer.WriteString(values.Labels[index]);
            }
            RecordPresence(writer, presenceOffsets, "labels", values.Labels != null);

            writer.WriteString(values.Message);
            RecordPresence(writer, presenceOffsets, "message", values.Message != null);

            var nested = values.Nested;
            writer.WriteBool(nested != null && nested.Enabled);
            writer.WriteString(nested == null ? null : nested.Label);
            RecordPresence(
                writer,
                presenceOffsets,
                "nested.label",
                nested != null && nested.Label != null);
            RecordPresence(writer, presenceOffsets, "nested", nested != null);

            writer.WriteInt32(values.OptionalCount.GetValueOrDefault());
            RecordPresence(writer, presenceOffsets, "optional_count", values.OptionalCount.HasValue);

            writer.WriteString(values.OptionalText);
            RecordPresence(writer, presenceOffsets, "optional_text", values.OptionalText != null);

            writer.WriteSequenceLength(values.Values == null ? 0 : values.Values.Count);
            if (values.Values != null)
            {
                for (var index = 0; index < values.Values.Count; index++)
                    writer.WriteInt64(values.Values[index]);
            }
            RecordPresence(writer, presenceOffsets, "values", values.Values != null);
        }

        private static void RecordPresence(
            IOracleWriter writer,
            IDictionary<string, int> offsets,
            string field,
            bool present)
        {
            if (offsets != null)
                offsets[field] = writer.Position;
            writer.WriteBool(present);
        }

        private sealed class GeneratedContract
        {
            private readonly Type _nestedType;
            private readonly Type _probeType;
            private readonly Type _stateKindType;
            private readonly Type _stateType;
            private readonly MethodInfo _buildMethod;

            public GeneratedContract(Assembly assembly)
            {
                _nestedType = assembly.GetType("Phase181.NestedState", throwOnError: true);
                _probeType = assembly.GetType("Phase181.GeneratedCdrProbe", throwOnError: true);
                _stateKindType = assembly.GetType("Phase181.StateKind", throwOnError: true);
                _stateType = assembly.GetType("Phase181.State", throwOnError: true);
                _buildMethod = _probeType.GetMethod(
                    "__TryBuildFoxRunRos2Cdr_0",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Generated custom CDR builder was not emitted.");
            }

            public BuildResult Build(
                FixtureValues values,
                string origin,
                ulong sequence,
                ulong nowNs)
            {
                var fixture = CreateFixture(
                    values,
                    origin,
                    sequence);
                var state = fixture.State;
                var probe = fixture.Probe;
                var arguments = new object[] { nowNs, null, null };
                var success = (bool)_buildMethod.Invoke(probe, arguments);
                var optionalTextReadCount = (int)_stateType
                    .GetField("OptionalTextReadCount", BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(state);
                return new BuildResult(
                    success,
                    (byte[])arguments[1],
                    (string)arguments[2],
                    optionalTextReadCount);
            }

            public (
                bool Success,
                FoxRunTransportPublishRoute Route,
                string Reason) BuildRoute(
                    FixtureValues values,
                    string origin,
                    ulong sequence,
                    ulong nowNs)
            {
                var fixture = CreateFixture(
                    values,
                    origin,
                    sequence);
                var source =
                    Assert.IsAssignableFrom<
                        IFoxRunBridgeGeneratedPublishSource>(
                        fixture.Probe);
                var success = source.FoxRunBridge_TryBuildPublish(
                    topicIndex: 0,
                    nowNs,
                    out var route,
                    out var reason);
                return (success, route, reason);
            }

            public DecodeResult Decode(
                byte[] payload,
                string transportId,
                ulong generation,
                bool markRemoteOwned)
            {
                var fixture = CreateFixture(
                    new FixtureValues(),
                    "decode-target",
                    sequence: 1UL);
                var source = Assert.IsAssignableFrom<
                    IFoxRunBridgeGeneratedSubscribeSource>(fixture.Probe);
                Assert.Equal(1, source.FoxRunBridge_SubscribeBindingCount);
                Assert.True(
                    source.FoxRunBridge_TryGetSubscribeBinding(
                        0,
                        out var binding,
                        out var bindingReason),
                    bindingReason);
                Assert.Equal(Topic, binding.Topic);
                Assert.Equal("cdr", binding.MessageEncoding);
                Assert.Equal(64, binding.SchemaSha256.Length);

                var success = source.FoxRunBridge_TryDecodeAndApply(
                    0,
                    payload,
                    transportId,
                    generation,
                    markRemoteOwned,
                    out var reason);
                var decodedState = _probeType
                    .GetField(
                        "State",
                        BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(fixture.Probe);
                return new DecodeResult(
                    success,
                    ReadFixtureValues(decodedState),
                    reason,
                    (bool)GetPublicField(fixture.Probe, "RemoteOwned"),
                    (string)GetPublicField(
                        fixture.Probe,
                        "RemoteTransportId"),
                    (ulong)GetPublicField(
                        fixture.Probe,
                        "RemoteGeneration"));
            }

            private (object State, object Probe) CreateFixture(
                FixtureValues values,
                string origin,
                ulong sequence)
            {
                var state = Activator.CreateInstance(_stateType);
                SetField(state, "Bytes", values.Bytes);
                SetField(state, "Count", values.Count);
                SetField(state, "Kind", Enum.ToObject(_stateKindType, values.Kind));
                SetField(state, "Kinds", CreateEnumArray(values.Kinds));
                SetField(state, "Labels", values.Labels);
                SetField(state, "Message", values.Message);
                SetField(state, "OptionalCount", values.OptionalCount);
                SetProperty(state, "OptionalText", values.OptionalText);
                SetField(state, "Values", values.Values);
                if (values.Nested == null)
                {
                    SetField(state, "Nested", null);
                }
                else
                {
                    var nested = Activator.CreateInstance(_nestedType);
                    SetField(nested, "Enabled", values.Nested.Enabled);
                    SetField(nested, "Label", values.Nested.Label);
                    SetField(state, "Nested", nested);
                }

                var probe = Activator.CreateInstance(
                    _probeType,
                    new[] { origin, state, (object)sequence });
                return (state, probe);
            }

            private FixtureValues ReadFixtureValues(object state)
            {
                var nested = GetPublicField(state, "Nested");
                return new FixtureValues
                {
                    Bytes = (byte[])GetPublicField(state, "Bytes"),
                    Count = (int)GetPublicField(state, "Count"),
                    Kind = Convert.ToUInt16(
                        GetPublicField(state, "Kind")),
                    Kinds = ReadEnumArray(GetPublicField(state, "Kinds")),
                    Labels = (List<string>)GetPublicField(state, "Labels"),
                    Message = (string)GetPublicField(state, "Message"),
                    Nested = nested == null
                        ? null
                        : new NestedValues
                        {
                            Enabled = (bool)GetPublicField(
                                nested,
                                "Enabled"),
                            Label = (string)GetPublicField(
                                nested,
                                "Label"),
                        },
                    OptionalCount = (int?)GetPublicField(
                        state,
                        "OptionalCount"),
                    OptionalText = (string)_stateType
                        .GetProperty(
                            "OptionalText",
                            BindingFlags.Instance | BindingFlags.Public)
                        .GetValue(state),
                    Values = (List<long>)GetPublicField(state, "Values"),
                };
            }

            private Array CreateEnumArray(IReadOnlyList<int> values)
            {
                if (values == null)
                    return null;

                var result = Array.CreateInstance(_stateKindType, values.Count);
                for (var index = 0; index < values.Count; index++)
                {
                    result.SetValue(
                        Enum.ToObject(_stateKindType, values[index]),
                        index);
                }
                return result;
            }

            private static int[] ReadEnumArray(object value)
            {
                if (value == null)
                    return null;

                return ((Array)value)
                    .Cast<object>()
                    .Select(Convert.ToInt32)
                    .ToArray();
            }

            private static object GetPublicField(
                object target,
                string name)
                => target.GetType()
                       .GetField(
                           name,
                           BindingFlags.Instance | BindingFlags.Public)
                       ?.GetValue(target)
                   ?? (target.GetType().GetField(
                           name,
                           BindingFlags.Instance | BindingFlags.Public)
                       == null
                       ? throw new InvalidOperationException(
                           "Dynamic fixture field was missing: " + name)
                       : null);

            private static void SetField(object target, string name, object value)
            {
                var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public)
                            ?? throw new InvalidOperationException("Dynamic fixture field was missing: " + name);
                field.SetValue(target, value);
            }

            private static void SetProperty(object target, string name, object value)
            {
                var property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                               ?? throw new InvalidOperationException("Dynamic fixture property was missing: " + name);
                property.SetValue(target, value);
            }
        }

        private sealed class FixtureValues
        {
            public byte[] Bytes;
            public int Count;
            public ushort Kind;
            public int[] Kinds;
            public List<string> Labels;
            public string Message;
            public NestedValues Nested;
            public int? OptionalCount;
            public string OptionalText;
            public List<long> Values;
        }

        private sealed class NestedValues
        {
            public bool Enabled;
            public string Label;
        }

        private readonly struct BuildResult
        {
            public BuildResult(
                bool success,
                byte[] payload,
                string reason,
                int optionalTextReadCount)
            {
                Success = success;
                Payload = payload;
                Reason = reason;
                OptionalTextReadCount = optionalTextReadCount;
            }

            public bool Success { get; }
            public byte[] Payload { get; }
            public string Reason { get; }
            public int OptionalTextReadCount { get; }
        }

        private readonly struct DecodeResult
        {
            public DecodeResult(
                bool success,
                FixtureValues values,
                string reason,
                bool remoteOwned,
                string remoteTransportId,
                ulong remoteGeneration)
            {
                Success = success;
                Values = values;
                Reason = reason;
                RemoteOwned = remoteOwned;
                RemoteTransportId = remoteTransportId;
                RemoteGeneration = remoteGeneration;
            }

            public bool Success { get; }
            public FixtureValues Values { get; }
            public string Reason { get; }
            public bool RemoteOwned { get; }
            public string RemoteTransportId { get; }
            public ulong RemoteGeneration { get; }
        }

        private readonly struct OracleResult
        {
            public OracleResult(byte[] bytes, IReadOnlyDictionary<string, int> presenceOffsets)
            {
                Bytes = bytes;
                PresenceOffsets = presenceOffsets;
            }

            public byte[] Bytes { get; }
            public IReadOnlyDictionary<string, int> PresenceOffsets { get; }
        }

        private interface IOracleWriter
        {
            int Position { get; }
            void WriteBool(bool value);
            void WriteUInt16(ushort value);
            void WriteInt32(int value);
            void WriteUInt32(uint value);
            void WriteInt64(long value);
            void WriteUInt64(ulong value);
            void WriteString(string value);
            void WriteByteSequence(byte[] values, int count);
            void WriteSequenceLength(int count);
        }

        private sealed class OracleByteWriter : IOracleWriter
        {
            private const int AlignmentOrigin = 4;
            private readonly List<byte> _bytes = new List<byte>
            {
                0x00, 0x01, 0x00, 0x00,
            };

            public int Position => _bytes.Count;

            public void WriteBool(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

            public void WriteUInt16(ushort value)
            {
                Align(2);
                Span<byte> buffer = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteInt32(int value)
            {
                Align(4);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteUInt32(uint value)
            {
                Align(4);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteInt64(long value)
            {
                Align(8);
                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteUInt64(ulong value)
            {
                Align(8);
                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
                Append(buffer);
            }

            public void WriteString(string value)
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                WriteUInt32(checked((uint)bytes.Length + 1U));
                _bytes.AddRange(bytes);
                _bytes.Add(0);
            }

            public void WriteByteSequence(byte[] values, int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                WriteSequenceLength(count);
                if (values == null)
                {
                    for (var index = 0; index < count; index++)
                        _bytes.Add(0);
                    return;
                }

                if (values.Length != count)
                    throw new ArgumentException("Byte sequence count did not match its oracle data.", nameof(count));
                _bytes.AddRange(values);
            }

            public void WriteSequenceLength(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                WriteUInt32((uint)count);
            }

            public byte[] ToArray() => _bytes.ToArray();

            private void Align(int alignment)
            {
                while (((_bytes.Count - AlignmentOrigin) % alignment) != 0)
                    _bytes.Add(0);
            }

            private void Append(ReadOnlySpan<byte> bytes)
            {
                for (var index = 0; index < bytes.Length; index++)
                    _bytes.Add(bytes[index]);
            }
        }

    }
}
