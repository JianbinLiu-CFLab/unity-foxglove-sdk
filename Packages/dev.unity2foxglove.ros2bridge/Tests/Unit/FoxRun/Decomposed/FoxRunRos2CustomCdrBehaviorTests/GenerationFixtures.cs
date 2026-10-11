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
