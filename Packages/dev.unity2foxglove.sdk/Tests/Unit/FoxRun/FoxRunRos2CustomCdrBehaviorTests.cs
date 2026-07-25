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
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunRos2CustomCdrBehaviorTests
    {
        private const int MaximumPayloadBytes = 4 * 1024 * 1024;
        private const string Topic = "/phase184/custom-cdr";
        private static readonly Lazy<GeneratedContract> Contract =
            new Lazy<GeneratedContract>(CompileGeneratedContract);

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
        public void GeneratedBuilderAcceptsTheLargestPayloadWithinFourMiBAndRejectsTheFirstLargerPayload()
        {
            const string origin = "phase184-four-mib-boundary";
            const ulong sequence = 1UL;
            const ulong nowNs = 0UL;
            var boundaryValues = new FixtureValues();
            var acceptedByteCount = FindLargestByteSequenceWithinLimit(
                boundaryValues,
                origin,
                sequence,
                nowNs,
                MaximumPayloadBytes);
            var rejectedByteCount = checked(acceptedByteCount + 1);
            var acceptedSize = MeasureOracle(
                boundaryValues,
                origin,
                sequence,
                nowNs,
                acceptedByteCount);
            var rejectedSize = MeasureOracle(
                boundaryValues,
                origin,
                sequence,
                nowNs,
                rejectedByteCount);

            Assert.InRange(acceptedSize, MaximumPayloadBytes - 8, MaximumPayloadBytes);
            Assert.True(rejectedSize > MaximumPayloadBytes);

            boundaryValues.Bytes = new byte[acceptedByteCount];
            var accepted = Contract.Value.Build(boundaryValues, origin, sequence, nowNs);
            Assert.True(accepted.Success, accepted.Reason);
            Assert.Equal(acceptedSize, accepted.Payload.Length);

            boundaryValues.Bytes = new byte[rejectedByteCount];
            var rejected = Contract.Value.Build(boundaryValues, origin, sequence, nowNs);
            Assert.False(rejected.Success);
            Assert.Null(rejected.Payload);
            Assert.False(string.IsNullOrWhiteSpace(rejected.Reason));
        }

        private static GeneratedContract CompileGeneratedContract()
        {
            var emitted = new StringBuilder();
            var member = CreateCustomMember();
            Ros2CustomCdrEmitter.EmitBuilders(
                emitted,
                "Phase181",
                "GeneratedCdrProbe",
                new[] { Topic },
                new Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>>(StringComparer.Ordinal)
                {
                    [Topic] = new List<FoxgloveSourceEmitter.TopicMember> { member },
                },
                string.Empty);

            var source = new StringBuilder();
            source.AppendLine("using System;");
            source.AppendLine("using System.Collections.Generic;");
            source.AppendLine("namespace Phase181");
            source.AppendLine("{");
            source.AppendLine("    public enum StateKind : ushort { Unknown = 0 }");
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
            source.AppendLine("    public sealed class GeneratedCdrProbe");
            source.AppendLine("    {");
            source.AppendLine("        private readonly string __foxRunOrigin;");
            source.AppendLine("        private readonly State __foxRunCapture_0_0;");
            source.AppendLine("        private readonly ulong __foxRunCaptureSequence_0;");
            source.AppendLine("        public GeneratedCdrProbe(string origin, State source, ulong sequence)");
            source.AppendLine("        {");
            source.AppendLine("            __foxRunOrigin = origin;");
            source.AppendLine("            __foxRunCapture_0_0 = source;");
            source.AppendLine("            __foxRunCaptureSequence_0 = sequence;");
            source.AppendLine("        }");
            source.Append(emitted);
            source.AppendLine("    }");
            source.AppendLine("}");

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

        private static FoxgloveSourceEmitter.TopicMember CreateCustomMember()
        {
            var nested = new FoxRunRos2CustomDtoShape(
                "Phase181.NestedState",
                "phase181/NestedState",
                "Phase181NestedState3281D0E21244",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Label", "label", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Enabled", "enabled", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Boolean", "bool", "", "", false, true, true),
                },
                diagnostics: Array.Empty<string>());
            var state = new FoxRunRos2CustomDtoShape(
                "Phase181.State",
                "phase181/State",
                "Phase181State48D288ED82F1",
                hasPublicParameterlessConstructor: true,
                isSupported: true,
                members: new[]
                {
                    new FoxRunRos2CustomDtoMemberShape(
                        "Values", "values", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<System.Int64>", "int64[]", "System.Int64", "",
                        true, true, true, FoxRunRos2CustomDtoSequenceRepresentation.List),
                    new FoxRunRos2CustomDtoMemberShape(
                        "OptionalText", "optional_text", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Nested", "nested", FoxRunRos2CustomDtoMemberKind.NestedDto,
                        "Phase181.NestedState", "Phase181NestedState3281D0E21244", "", nested.CanonicalIdentity,
                        true, true, true, nestedShape: nested),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Count", "count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Int32", "int32", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Labels", "labels", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Collections.Generic.List<System.String>", "string[]", "System.String", "",
                        true, true, true, FoxRunRos2CustomDtoSequenceRepresentation.List),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Kind", "kind", FoxRunRos2CustomDtoMemberKind.Enum,
                        "Phase181.StateKind", "uint16", "", "", false, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Bytes", "bytes", FoxRunRos2CustomDtoMemberKind.Sequence,
                        "System.Byte[]", "uint8[]", "System.Byte", "",
                        true, true, true, FoxRunRos2CustomDtoSequenceRepresentation.Array),
                    new FoxRunRos2CustomDtoMemberShape(
                        "Message", "message", FoxRunRos2CustomDtoMemberKind.String,
                        "System.String", "string", "", "", true, true, true),
                    new FoxRunRos2CustomDtoMemberShape(
                        "OptionalCount", "optional_count", FoxRunRos2CustomDtoMemberKind.Scalar,
                        "System.Nullable<System.Int32>", "int32", "", "", true, true, true),
                },
                diagnostics: Array.Empty<string>());
            return new FoxgloveSourceEmitter.TopicMember(
                "State",
                "Phase181.State",
                Topic,
                10f,
                "phase181.State",
                policy: (int)FoxRunPolicy.Trigger,
                tolerance: 0f,
                mode: (int)FoxRunFlow.Publish,
                canonicalType: "phase181/State",
                encoding: FoxRunGenerationDescriptorConstants.JsonEncoding,
                source: string.Empty,
                qosProfile: FoxRunGenerationDescriptorConstants.DefaultQosProfile,
                generatesWebSocketCodec: false,
                generatesRos2NativeRegistration: true,
                ros2MessageShape: null,
                ros2CustomDtoShape: state,
                ros2ContractKind: FoxRunRos2ContractKind.CustomDto);
        }

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

        private static int MeasureOracle(
            FixtureValues values,
            string origin,
            ulong sequence,
            ulong nowNs,
            int byteCount)
        {
            var writer = new OracleSizeWriter();
            WriteCanonicalEnvelope(
                writer,
                values,
                origin,
                sequence,
                nowNs,
                presenceOffsets: null,
                byteCountOverride: byteCount);
            return writer.Position;
        }

        private static int FindLargestByteSequenceWithinLimit(
            FixtureValues values,
            string origin,
            ulong sequence,
            ulong nowNs,
            int maximumBytes)
        {
            var low = 0;
            var high = maximumBytes;
            while (low < high)
            {
                var candidate = low + ((high - low + 1) / 2);
                if (MeasureOracle(values, origin, sequence, nowNs, candidate) <= maximumBytes)
                    low = candidate;
                else
                    high = candidate - 1;
            }

            return low;
        }

        private static void WriteCanonicalEnvelope(
            IOracleWriter writer,
            FixtureValues values,
            string origin,
            ulong sequence,
            ulong nowNs,
            IDictionary<string, int> presenceOffsets,
            int? byteCountOverride = null)
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

            var byteCount = byteCountOverride ?? (values.Bytes == null ? 0 : values.Bytes.Length);
            writer.WriteByteSequence(values.Bytes, byteCount);
            RecordPresence(writer, presenceOffsets, "bytes", byteCountOverride.HasValue || values.Bytes != null);

            writer.WriteInt32(values.Count);
            writer.WriteUInt16(values.Kind);

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
                var state = Activator.CreateInstance(_stateType);
                SetField(state, "Bytes", values.Bytes);
                SetField(state, "Count", values.Count);
                SetField(state, "Kind", Enum.ToObject(_stateKindType, values.Kind));
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

        private sealed class OracleSizeWriter : IOracleWriter
        {
            private const int AlignmentOrigin = 4;
            private int _position = AlignmentOrigin;

            public int Position => _position;

            public void WriteBool(bool value) => _position++;

            public void WriteUInt16(ushort value)
            {
                Align(2);
                _position += 2;
            }

            public void WriteInt32(int value)
            {
                Align(4);
                _position += 4;
            }

            public void WriteUInt32(uint value)
            {
                Align(4);
                _position += 4;
            }

            public void WriteInt64(long value)
            {
                Align(8);
                _position += 8;
            }

            public void WriteUInt64(ulong value)
            {
                Align(8);
                _position += 8;
            }

            public void WriteString(string value)
            {
                Align(4);
                _position = checked(_position + 4 + Encoding.UTF8.GetByteCount(value ?? string.Empty) + 1);
            }

            public void WriteByteSequence(byte[] values, int count)
            {
                WriteSequenceLength(count);
                _position = checked(_position + count);
            }

            public void WriteSequenceLength(int count)
            {
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                WriteUInt32((uint)count);
            }

            private void Align(int alignment)
            {
                var relative = (_position - AlignmentOrigin) % alignment;
                if (relative != 0)
                    _position += alignment - relative;
            }
        }
    }
}
