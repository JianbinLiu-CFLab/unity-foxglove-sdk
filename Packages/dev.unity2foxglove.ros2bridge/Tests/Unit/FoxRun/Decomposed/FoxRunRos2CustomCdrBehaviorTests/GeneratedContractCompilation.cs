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


    }
}
