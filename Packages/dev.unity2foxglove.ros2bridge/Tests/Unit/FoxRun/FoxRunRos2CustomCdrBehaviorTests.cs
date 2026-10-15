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
    /// <remarks>Phase192 source sections: GeneratedCdrBehaviorTests.cs, GeneratedContractCompilation.cs, CdrOracleEncoding.cs, CdrFixtureModels.cs</remarks>
    public sealed partial class FoxRunRos2CustomCdrBehaviorTests
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


    }
}
