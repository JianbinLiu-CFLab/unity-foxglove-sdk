// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.FoxRun
{
    public sealed class FoxRunRos2CustomDtoShapeTests
    {
        [Fact]
        public void ReflectionBuilderProducesDeterministicSupportedDtoShape()
        {
            var shape = BuildReflectionShape(typeof(ExampleDto));

            Assert.True(Read<bool>(shape, "IsSupported"));
            Assert.Equal(
                typeof(ExampleDto).FullName.Replace('+', '.'),
                Read<string>(shape, "FullyQualifiedTypeName"));
            Assert.StartsWith("ExampleDto", Read<string>(shape, "PayloadIdentity"), StringComparison.Ordinal);
            Assert.NotEmpty(Read<string>(shape, "CanonicalIdentity"));

            var members = Read<IEnumerable<object>>(shape, "Members").ToArray();
            Assert.Equal(new[] { "Count", "Message", "Nested", "Optional", "Values" },
                members.Select(member => Read<string>(member, "Name")).ToArray());
            Assert.Equal(new[] { false, true, true, true, true },
                members.Select(member => Read<bool>(member, "HasPresence")).ToArray());
            Assert.Equal(new[] { "", "foxrun_has_message", "foxrun_has_nested", "foxrun_has_optional", "foxrun_has_values" },
                members.Select(member => Read<string>(member, "PresenceFieldName")).ToArray());
        }

        [Fact]
        public void ReflectionBuilderMapsEveryLosslessScalarWidthAndSequenceForm()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(AllSupportedValuesDto));

            Assert.True(shape.IsSupported, string.Join(" || ", shape.Diagnostics));
            var members = shape.Members.ToDictionary(member => member.Name, StringComparer.Ordinal);
            Assert.Equal("bool", members[nameof(AllSupportedValuesDto.Boolean)].RosType);
            Assert.Equal("int8", members[nameof(AllSupportedValuesDto.SignedByte)].RosType);
            Assert.Equal("uint8", members[nameof(AllSupportedValuesDto.Byte)].RosType);
            Assert.Equal("int16", members[nameof(AllSupportedValuesDto.Short)].RosType);
            Assert.Equal("uint16", members[nameof(AllSupportedValuesDto.UShort)].RosType);
            Assert.Equal("int32", members[nameof(AllSupportedValuesDto.Integer)].RosType);
            Assert.Equal("uint32", members[nameof(AllSupportedValuesDto.UInteger)].RosType);
            Assert.Equal("int64", members[nameof(AllSupportedValuesDto.Long)].RosType);
            Assert.Equal("uint64", members[nameof(AllSupportedValuesDto.ULong)].RosType);
            Assert.Equal("float32", members[nameof(AllSupportedValuesDto.Float)].RosType);
            Assert.Equal("float64", members[nameof(AllSupportedValuesDto.Double)].RosType);
            Assert.Equal("string", members[nameof(AllSupportedValuesDto.Text)].RosType);
            Assert.Equal("uint16", members[nameof(AllSupportedValuesDto.State)].RosType);
            Assert.Equal("uint8[]", members[nameof(AllSupportedValuesDto.Bytes)].RosType);
            Assert.Equal("int32[]", members[nameof(AllSupportedValuesDto.Integers)].RosType);
            Assert.Equal("string[]", members[nameof(AllSupportedValuesDto.Labels)].RosType);
            Assert.Equal(FoxRunRos2CustomDtoSequenceRepresentation.Array,
                members[nameof(AllSupportedValuesDto.Integers)].SequenceRepresentation);
            Assert.Equal(FoxRunRos2CustomDtoSequenceRepresentation.List,
                members[nameof(AllSupportedValuesDto.Labels)].SequenceRepresentation);
            Assert.True(members[nameof(AllSupportedValuesDto.Text)].HasPresence);
            Assert.True(members[nameof(AllSupportedValuesDto.Bytes)].HasPresence);
            Assert.True(members[nameof(AllSupportedValuesDto.OptionalInteger)].HasPresence);
            Assert.False(members[nameof(AllSupportedValuesDto.Integer)].HasPresence);
        }

        [Fact]
        public void ReflectionBuilderRejectsReservedAndLossyDtoMembers()
        {
            var shape = BuildReflectionShape(typeof(UnsupportedDto));

            Assert.False(Read<bool>(shape, "IsSupported"));
            var diagnostics = Read<IEnumerable<string>>(shape, "Diagnostics").ToArray();
            Assert.Contains(diagnostics, value => value.StartsWith("FOXR2F009|", StringComparison.Ordinal));
            Assert.Contains(diagnostics, value => value.IndexOf("foxrun_", StringComparison.Ordinal) >= 0);
            Assert.Contains(diagnostics, value => value.IndexOf("Decimal", StringComparison.Ordinal) >= 0);
        }

        [Theory]
        [MemberData(nameof(UnsupportedDtoTypes))]
        public void ReflectionBuilderRejectsUnsupportedDtoShapes(Type dtoType, string expectedDiagnostic)
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(dtoType);

            Assert.False(shape.IsSupported);
            Assert.Contains(shape.Diagnostics, diagnostic => diagnostic.StartsWith(expectedDiagnostic + "|", StringComparison.Ordinal));
        }

        public static IEnumerable<object[]> UnsupportedDtoTypes()
        {
            yield return new object[] { typeof(GenericDto<int>), "FOXR2F009" };
            yield return new object[] { typeof(NonConstructibleDto), "FOXR2F010" };
            yield return new object[] { typeof(NonWritableDto), "FOXR2F011" };
            yield return new object[] { typeof(CyclicDto), "FOXR2F009" };
            yield return new object[] { typeof(UnsupportedCollectionDto), "FOXR2F009" };
            yield return new object[] { typeof(UnsupportedReferenceDto), "FOXR2F009" };
            yield return new object[] { typeof(UnsupportedArrayDto), "FOXR2F009" };
        }

        [Fact]
        public void AcyclicSharedReferenceMembersAreIndependentValueSlots()
        {
            var shape = FoxRunReflectionRos2CustomDtoShapeBuilder.Build(typeof(SharedReferenceDto));

            Assert.True(shape.IsSupported, string.Join(" || ", shape.Diagnostics));
            var first = shape.Members.Single(member => member.Name == nameof(SharedReferenceDto.First));
            var second = shape.Members.Single(member => member.Name == nameof(SharedReferenceDto.Second));
            Assert.Equal(FoxRunRos2CustomDtoMemberKind.NestedDto, first.Kind);
            Assert.Equal(FoxRunRos2CustomDtoMemberKind.NestedDto, second.Kind);
            Assert.Equal(first.NestedShapeIdentity, second.NestedShapeIdentity);
            Assert.NotSame(first, second);
            Assert.DoesNotContain(
                typeof(FoxRunRos2CustomDtoMemberShape).GetFields(BindingFlags.Public | BindingFlags.Instance),
                field => field.Name.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void SameSimpleNameAcrossNamespacesGetsDistinctCaseInsensitivePayloadIdentities()
        {
            var first = BuildReflectionShape(typeof(First.CollisionDto));
            var second = BuildReflectionShape(typeof(Second.CollisionDto));

            Assert.NotEqual(Read<string>(first, "CanonicalIdentity"), Read<string>(second, "CanonicalIdentity"));
            Assert.False(string.Equals(
                Read<string>(first, "PayloadIdentity"),
                Read<string>(second, "PayloadIdentity"),
                StringComparison.OrdinalIgnoreCase));
        }

        private static object BuildReflectionShape(Type dtoType)
        {
            var assembly = typeof(FoxRunGenerationModel).Assembly;
            var builder = assembly.GetType("Unity.FoxgloveSDK.Editor.FoxRunReflectionRos2CustomDtoShapeBuilder");
            Assert.NotNull(builder);

            var build = builder.GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null);
            Assert.NotNull(build);

            var shape = build.Invoke(null, new object[] { dtoType });
            Assert.NotNull(shape);
            return shape;
        }

        private static T Read<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field);
            return (T)field.GetValue(instance);
        }

        private static string MemberEvidence(FoxRunRos2CustomDtoMemberShape member)
            => string.Join("|", new[]
            {
                member.Name,
                member.RosFieldName,
                member.Kind.ToString(),
                member.FullyQualifiedTypeName,
                member.RosType,
                member.SequenceElementTypeName,
                member.NestedShapeIdentity,
                member.HasPresence.ToString(),
                member.SequenceRepresentation.ToString()
            });

        public sealed class ExampleDto
        {
            public int Count;
            public string Message { get; set; }
            public NestedDto Nested { get; set; }
            public int? Optional { get; set; }
            public List<long> Values { get; set; }
        }

        public sealed class NestedDto
        {
            public bool Enabled;
        }

        public sealed class UnsupportedDto
        {
            public decimal Amount;
            public int foxrun_reserved;
        }

        public sealed class AllSupportedValuesDto
        {
            public bool Boolean;
            public sbyte SignedByte;
            public byte Byte;
            public short Short;
            public ushort UShort;
            public int Integer;
            public uint UInteger;
            public long Long;
            public ulong ULong;
            public float Float;
            public double Double;
            public string Text { get; set; }
            public TestState State;
            public byte[] Bytes { get; set; }
            public int[] Integers { get; set; }
            public List<string> Labels { get; set; }
            public int? OptionalInteger { get; set; }
        }

        public enum TestState : ushort
        {
            Unknown = 0,
            Active = 1
        }

        public sealed class GenericDto<T>
        {
            public T Value { get; set; }
        }

        public sealed class NonConstructibleDto
        {
            public NonConstructibleDto(int value) { }
            public int Value { get; set; }
        }

        public sealed class NonWritableDto
        {
            public int Value { get; }
        }

        public sealed class CyclicDto
        {
            public CyclicDto Next { get; set; }
        }

        public sealed class UnsupportedCollectionDto
        {
            public Dictionary<string, int> Dictionary { get; set; }
            public HashSet<int> Set { get; set; }
        }

        public sealed class UnsupportedReferenceDto
        {
            public object Object { get; set; }
            public Action Delegate { get; set; }
            public Stream Stream { get; set; }
        }

        public sealed class UnsupportedArrayDto
        {
            public int[][] Jagged { get; set; }
            public int[,] Matrix { get; set; }
            public int?[] NullableElements { get; set; }
        }

        public sealed class SharedReferenceDto
        {
            public NestedDto First { get; set; }
            public NestedDto Second { get; set; }
        }

        public sealed class ParityDto
        {
            public int Count;
            public string Message { get; set; }
            public NestedDto Nested { get; set; }
            public int? Optional { get; set; }
            public List<long> Values { get; set; }
        }

        public sealed class ParityUnsupportedDto
        {
            public decimal Amount;
            public Dictionary<string, int> Map { get; set; }
            public int?[] OptionalValues { get; set; }
        }

        private static class First
        {
            public sealed class CollisionDto
            {
                public int Value;
            }
        }

        private static class Second
        {
            public sealed class CollisionDto
            {
                public int Value;
            }
        }
    }
}
