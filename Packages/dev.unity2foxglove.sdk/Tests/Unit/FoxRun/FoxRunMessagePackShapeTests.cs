// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks typed FoxRun MessagePack marker and recursive-shape compatibility.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [Trait("Phase", "185-B")]
    [Trait("Domain", "FoxRun")]
    public sealed class FoxRunMessagePackShapeTests
    {
        [Fact]
        public void IntegerBoundariesUseOfficialSignedAndUnsignedMarkerFamilies()
        {
            using var writer = new FoxgloveMsgPackWriter();

            writer.WriteInt64(-32);
            writer.WriteInt64(-33);
            writer.WriteInt64(sbyte.MinValue);
            writer.WriteInt64(sbyte.MinValue - 1L);
            writer.WriteInt64(short.MinValue);
            writer.WriteInt64(short.MinValue - 1L);
            writer.WriteInt64(int.MinValue);
            writer.WriteInt64((long)int.MinValue - 1L);
            writer.WriteInt64(long.MinValue);
            writer.WriteUInt64(0);
            writer.WriteUInt64(127);
            writer.WriteUInt64(128);
            writer.WriteUInt64(byte.MaxValue);
            writer.WriteUInt64(256);
            writer.WriteUInt64(ushort.MaxValue);
            writer.WriteUInt64(65_536);
            writer.WriteUInt64(uint.MaxValue);
            writer.WriteUInt64((ulong)uint.MaxValue + 1UL);
            writer.WriteUInt64(ulong.MaxValue);

            Assert.Equal(
                new byte[]
                {
                    0xe0,
                    0xd0, 0xdf,
                    0xd0, 0x80,
                    0xd1, 0xff, 0x7f,
                    0xd1, 0x80, 0x00,
                    0xd2, 0xff, 0xff, 0x7f, 0xff,
                    0xd2, 0x80, 0x00, 0x00, 0x00,
                    0xd3, 0xff, 0xff, 0xff, 0xff, 0x7f, 0xff, 0xff, 0xff,
                    0xd3, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00,
                    0x7f,
                    0xcc, 0x80,
                    0xcc, 0xff,
                    0xcd, 0x01, 0x00,
                    0xcd, 0xff, 0xff,
                    0xce, 0x00, 0x01, 0x00, 0x00,
                    0xce, 0xff, 0xff, 0xff, 0xff,
                    0xcf, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0xcf, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff
                },
                writer.ToArray());
        }

        [Fact]
        public void FloatWidthsPreserveNegativeZeroNanAndInfinities()
        {
            using var writer = new FoxgloveMsgPackWriter();

            writer.WriteFloat(-0f);
            writer.WriteFloat(BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00000)));
            writer.WriteFloat(float.PositiveInfinity);
            writer.WriteDouble(-0d);
            writer.WriteDouble(BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000000)));
            writer.WriteDouble(double.NegativeInfinity);

            Assert.Equal(
                new byte[]
                {
                    0xca, 0x80, 0x00, 0x00, 0x00,
                    0xca, 0x7f, 0xc0, 0x00, 0x00,
                    0xca, 0x7f, 0x80, 0x00, 0x00,
                    0xcb, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0xcb, 0x7f, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0xcb, 0xff, 0xf0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                },
                writer.ToArray());
        }

        [Fact]
        public void NilBooleanStringBinaryArrayAndMapUseDistinctOfficialFamilies()
        {
            using var writer = new FoxgloveMsgPackWriter();

            writer.WriteNil();
            writer.WriteBool(false);
            writer.WriteString("A");
            writer.WriteBinary(new byte[] { 1, 2 });
            writer.WriteArrayHeader(2);
            writer.WriteUInt32(1);
            writer.WriteUInt32(2);
            writer.WriteMapHeader(0);

            Assert.Equal(
                new byte[]
                {
                    0xc0, 0xc2, 0xa1, 0x41,
                    0xc4, 0x02, 0x01, 0x02,
                    0x92, 0x01, 0x02,
                    0x80
                },
                writer.ToArray());
        }

        [Fact]
        public void StringBinaryArrayAndMapHeadersCoverEveryOfficialLengthFamily()
        {
            AssertPrefix(
                writer => writer.WriteString(new string('a', 31)),
                new byte[] { 0xbf });
            AssertPrefix(
                writer => writer.WriteString(new string('a', 32)),
                new byte[] { 0xd9, 0x20 });
            AssertPrefix(
                writer => writer.WriteString(new string('a', 255)),
                new byte[] { 0xd9, 0xff });
            AssertPrefix(
                writer => writer.WriteString(new string('a', 256)),
                new byte[] { 0xda, 0x01, 0x00 });
            AssertPrefix(
                writer => writer.WriteString(new string('a', 65_535)),
                new byte[] { 0xda, 0xff, 0xff });
            AssertPrefix(
                writer => writer.WriteString(new string('a', 65_536)),
                new byte[] { 0xdb, 0x00, 0x01, 0x00, 0x00 });

            AssertPrefix(
                writer => writer.WriteBinary(new byte[255]),
                new byte[] { 0xc4, 0xff });
            AssertPrefix(
                writer => writer.WriteBinary(new byte[256]),
                new byte[] { 0xc5, 0x01, 0x00 });
            AssertPrefix(
                writer => writer.WriteBinary(new byte[65_535]),
                new byte[] { 0xc5, 0xff, 0xff });
            AssertPrefix(
                writer => writer.WriteBinary(new byte[65_536]),
                new byte[] { 0xc6, 0x00, 0x01, 0x00, 0x00 });

            AssertHeader(
                writer => writer.WriteArrayHeader(15),
                new byte[] { 0x9f });
            AssertHeader(
                writer => writer.WriteArrayHeader(16),
                new byte[] { 0xdc, 0x00, 0x10 });
            AssertHeader(
                writer => writer.WriteArrayHeader(65_535),
                new byte[] { 0xdc, 0xff, 0xff });
            AssertHeader(
                writer => writer.WriteArrayHeader(65_536),
                new byte[] { 0xdd, 0x00, 0x01, 0x00, 0x00 });
            AssertHeader(
                writer => writer.WriteMapHeader(15),
                new byte[] { 0x8f });
            AssertHeader(
                writer => writer.WriteMapHeader(16),
                new byte[] { 0xde, 0x00, 0x10 });
            AssertHeader(
                writer => writer.WriteMapHeader(65_535),
                new byte[] { 0xde, 0xff, 0xff });
            AssertHeader(
                writer => writer.WriteMapHeader(65_536),
                new byte[] { 0xdf, 0x00, 0x01, 0x00, 0x00 });
        }

        [Fact]
        public void RecursiveShapePreservesNullableEnumUnityListBinaryAndNestedDtoIdentity()
        {
            var shape = FoxRunReflectionTypeShapeBuilder.Build(typeof(Envelope));

            Assert.Equal(FoxRunTypeShapeKind.Object, shape.Kind);
            Assert.Equal(
                new[] { "Mode", "OptionalCount", "Payload", "Pose", "Samples" },
                shape.Fields.Select(field => field.JsonName));
            Assert.True(Assert.Single(shape.Fields, field => field.JsonName == "OptionalCount").IsNullable);
            Assert.Equal(
                FoxRunTypeShapeKind.Enum,
                Assert.Single(shape.Fields, field => field.JsonName == "Mode").TypeShape.Kind);
            Assert.True(
                Assert.Single(shape.Fields, field => field.JsonName == "Payload").TypeShape.IsBinary);
            Assert.Equal(
                FoxRunCollectionKind.List,
                Assert.Single(shape.Fields, field => field.JsonName == "Samples").TypeShape.CollectionKind);

            var pose = Assert.Single(shape.Fields, field => field.JsonName == "Pose").TypeShape;
            Assert.Equal(FoxRunTypeShapeKind.Object, pose.Kind);
            Assert.Equal(new[] { "Position", "Valid" }, pose.Fields.Select(field => field.JsonName));
            Assert.Equal(
                "UnityEngine.Vector3",
                Assert.Single(pose.Fields, field => field.JsonName == "Position").TypeShape.TypeName);
        }

        [Fact]
        public void RoslynAndReflectionDiscoverTheSameNestedMessagePackShape()
        {
            const string source = @"
using System.Collections.Generic;
namespace Demo
{
    public enum Mode { Idle = -1, Active = 2 }
    public sealed class Pose { public int X { get; set; } }
    public sealed class Envelope
    {
        public Mode Mode { get; set; }
        public int? OptionalCount { get; set; }
        public byte[] Payload { get; set; }
        public Pose Pose { get; set; }
        public List<float> Samples { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185MessagePackShapeParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName("Demo.Envelope");

            Assert.NotNull(symbol);
            Assert.True(FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(typeof(ParityEnvelope));

            Assert.Equal(ShapeSignature(reflection), ShapeSignature(roslyn));
        }

        private static string ShapeSignature(FoxRunTypeShape shape)
            => shape.Kind + "|" + shape.CanonicalType + "|" + shape.CollectionKind + "|"
               + string.Join(
                   ";",
                   shape.Fields.Select(
                       field => field.JsonName + ":" + field.IsNullable + ":"
                                + ShapeSignature(field.TypeShape)))
               + (shape.ElementShape == null ? string.Empty : "[]" + ShapeSignature(shape.ElementShape));

        private static MetadataReference[] TrustedPlatformReferences()
            => ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();

        private static void AssertPrefix(
            Action<FoxgloveMsgPackWriter> write,
            byte[] expected)
        {
            using var writer = new FoxgloveMsgPackWriter();
            write(writer);
            Assert.Equal(expected, writer.ToArray().Take(expected.Length).ToArray());
        }

        private static void AssertHeader(
            Action<FoxgloveMsgPackWriter> write,
            byte[] expected)
        {
            using var writer = new FoxgloveMsgPackWriter();
            write(writer);
            Assert.Equal(expected, writer.ToArray());
        }

        private enum Mode
        {
            Idle = -1,
            Active = 2
        }

        private sealed class Pose
        {
            public UnityEngine.Vector3 Position { get; set; }
            public bool Valid { get; set; }
        }

        private sealed class Envelope
        {
            public Mode Mode { get; set; }
            public int? OptionalCount { get; set; }
            public byte[] Payload { get; set; }
            public Pose Pose { get; set; }
            public List<float> Samples { get; set; }
        }

        private enum ParityMode
        {
            Idle = -1,
            Active = 2
        }

        private sealed class ParityPose
        {
            public int X { get; set; }
        }

        private sealed class ParityEnvelope
        {
            public ParityMode Mode { get; set; }
            public int? OptionalCount { get; set; }
            public byte[] Payload { get; set; }
            public ParityPose Pose { get; set; }
            public List<float> Samples { get; set; }
        }
    }
}
