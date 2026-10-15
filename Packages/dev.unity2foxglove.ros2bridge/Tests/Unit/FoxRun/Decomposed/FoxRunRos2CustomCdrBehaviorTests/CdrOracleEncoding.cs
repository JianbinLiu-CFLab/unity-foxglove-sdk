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


    }
}
