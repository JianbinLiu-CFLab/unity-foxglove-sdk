// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Covers direct FoxRun Protobuf inbound wire decoding behavior.

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxRunProtobufInboundTests
    {
        [Fact]
        public void ProtobufWireRoundTripsVectorWithoutJsonEnvelope()
        {
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteVector3(payload, 17, new UnityEngine.Vector3 { x = 1f, y = -2f, z = 3.5f });

            Assert.True(FoxRunInboundProtobuf.TryRead(payload.ToArray(), 17, out UnityEngine.Vector3 value, out var error));
            Assert.Empty(error);
            Assert.Equal(1f, value.x);
            Assert.Equal(-2f, value.y);
            Assert.Equal(3.5f, value.z);
            Assert.DoesNotContain((byte)'{', payload);
        }

        [Fact]
        public void ProtobufWireWritesCanonicalTagForLargeLegalFieldNumber()
        {
            const int fieldNumber = 276_595_399;
            var payload = new System.Collections.Generic.List<byte>();

            FoxRunProtobufWire.WriteInt32(payload, fieldNumber, 1);

            Assert.Equal(new byte[] { 0xb8, 0xac, 0x90, 0x9f, 0x08, 0x01 }, payload);
        }

        [Fact]
        public void MalformedProtobufDoesNotProduceAnInboundValue()
        {
            var malformed = new byte[] { 0x88, 0x01, 0x80 };

            Assert.False(FoxRunInboundProtobuf.TryRead(malformed, 17, out int value, out var error));
            Assert.Equal(0, value);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void ProtobufFieldReaderPreservesRepeatedFieldOccurrences()
        {
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteFloat(payload, 4, 1.5f);
            FoxRunProtobufWire.WriteFloat(payload, 4, 2.5f);
            var fields = new System.Collections.Generic.List<FoxRunProtobufField>();

            Assert.True(FoxRunInboundProtobuf.TryReadFields(payload.ToArray(), fields, out var error));
            Assert.Empty(error);
            Assert.Equal(2, fields.Count);
            Assert.All(fields, field => Assert.Equal(4, field.Number));
        }

        [Fact]
        public void FieldDecoderRejectsWrongWireTypeWithoutProducingValue()
        {
            var field = new FoxRunProtobufField(4, 2, new byte[] { 1, 2, 3 });

            Assert.False(FoxRunInboundProtobuf.TryDecodeFloat(field, out var value, out var error));
            Assert.Equal(0f, value);
            Assert.Contains("wire type", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PackedRepeatedScalarsAndDefaultVectorComponentsDecode()
        {
            var packed = new FoxRunProtobufField(4, 2, new byte[] { 1, 0x96, 0x01 });
            var values = new System.Collections.Generic.List<int>();

            Assert.True(FoxRunInboundProtobuf.TryReadRepeatedInt32(packed, values, out var packedError));
            Assert.Empty(packedError);
            Assert.Equal(new[] { 1, 150 }, values);

            var vectorPayload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteVector3(vectorPayload, 7, new UnityEngine.Vector3 { y = 2f });

            Assert.True(FoxRunInboundProtobuf.TryRead(vectorPayload.ToArray(), 7, out UnityEngine.Vector3 vector, out var vectorError));
            Assert.Empty(vectorError);
            Assert.Equal(0f, vector.x);
            Assert.Equal(2f, vector.y);
            Assert.Equal(0f, vector.z);
        }

        [Fact]
        public void UnpackedRepeatedVarintsAppendAfterSuccessfulDecode()
        {
            var field = new FoxRunProtobufField(3, 0, new byte[] { 0x96, 0x01 });
            var values = new System.Collections.Generic.List<int> { 7 };

            Assert.True(FoxRunInboundProtobuf.TryReadRepeatedInt32(field, values, out var error));
            Assert.Empty(error);
            Assert.Equal(new[] { 7, 150 }, values);
        }

        [Fact]
        public void NarrowIntegerReadersPreserveValidValuesAndRejectOverflowAtomically()
        {
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteUInt32(payload, 3, byte.MaxValue);

            Assert.True(FoxRunInboundProtobuf.TryRead(payload.ToArray(), 3, out byte scalar, out var scalarError));
            Assert.Empty(scalarError);
            Assert.Equal(byte.MaxValue, scalar);

            var validPacked = new FoxRunProtobufField(3, 2, new byte[] { 1, 0xff, 0x01 });
            var validValues = new System.Collections.Generic.List<byte>();
            Assert.True(FoxRunInboundProtobuf.TryReadRepeatedUInt8(validPacked, validValues, out var validError));
            Assert.Empty(validError);
            Assert.Equal(new byte[] { 1, byte.MaxValue }, validValues);

            var overflowPacked = new FoxRunProtobufField(3, 2, new byte[] { 1, 0x80, 0x02 });
            var unchangedValues = new System.Collections.Generic.List<byte> { 7 };
            Assert.False(FoxRunInboundProtobuf.TryReadRepeatedUInt8(
                overflowPacked,
                unchangedValues,
                out var overflowError));
            Assert.Equal(new byte[] { 7 }, unchangedValues);
            Assert.Equal("Protobuf uint8 value is out of range.", overflowError);

            var overflowScalar = new FoxRunProtobufField(3, 0, new byte[] { 0x80, 0x02 });
            Assert.False(FoxRunInboundProtobuf.TryDecodeUInt8(
                overflowScalar,
                out var rejected,
                out var rejectedError));
            Assert.Equal(0, rejected);
            Assert.Equal("Protobuf uint8 value is out of range.", rejectedError);
        }

        [Fact]
        public void RepeatedVarintReaderLeavesDestinationUnchangedOnMalformedPackedValue()
        {
            var malformedPacked = new FoxRunProtobufField(3, 2, new byte[] { 1, 0x80 });
            var values = new System.Collections.Generic.List<int> { 7 };

            Assert.False(FoxRunInboundProtobuf.TryReadRepeatedInt32(
                malformedPacked,
                values,
                out var error));
            Assert.Equal(new[] { 7 }, values);
            Assert.Equal("Malformed packed Protobuf int32 value.", error);
        }

        [Fact]
        public void FieldReaderLeavesDestinationUnchangedOnMalformedLaterField()
        {
            var fields = new System.Collections.Generic.List<FoxRunProtobufField>
            {
                new FoxRunProtobufField(9, 0, new byte[] { 7 })
            };
            var malformed = new byte[] { 0x08, 0x01, 0x10, 0x80 };

            Assert.False(FoxRunInboundProtobuf.TryReadFields(malformed, fields, out var error));
            Assert.Single(fields);
            Assert.Equal(9, fields[0].Number);
            Assert.Equal(0, fields[0].WireType);
            Assert.Equal(new byte[] { 7 }, fields[0].Value);
            Assert.Equal("Malformed Protobuf field value.", error);
        }

        [Fact]
        public void StringReadersRejectInvalidUtf8WithoutReplacement()
        {
            var invalidSequences = new[]
            {
                new byte[] { 0xff },
                new byte[] { 0x80 },
                new byte[] { 0xe2, 0x82 },
                new byte[] { 0xc0, 0xaf }
            };

            foreach (var invalidUtf8 in invalidSequences)
            {
                var field = new FoxRunProtobufField(1, 2, invalidUtf8);

                Assert.False(FoxRunInboundProtobuf.TryDecodeString(field, out var decodedField, out var fieldError));
                Assert.Empty(decodedField);
                Assert.Contains("UTF-8", fieldError, StringComparison.OrdinalIgnoreCase);

                var payload = new byte[invalidUtf8.Length + 2];
                payload[0] = 0x0a;
                payload[1] = (byte)invalidUtf8.Length;
                Buffer.BlockCopy(invalidUtf8, 0, payload, 2, invalidUtf8.Length);

                Assert.False(FoxRunInboundProtobuf.TryRead(payload, 1, out string decodedPayload, out var payloadError));
                Assert.Empty(decodedPayload);
                Assert.Contains("UTF-8", payloadError, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void StringReadersPreserveValidSupplementaryPlaneUtf8()
        {
            const string Expected = "robot \U0001f916";
            var bytes = System.Text.Encoding.UTF8.GetBytes(Expected);
            var field = new FoxRunProtobufField(1, 2, bytes);
            var payload = new System.Collections.Generic.List<byte>();
            FoxRunProtobufWire.WriteString(payload, 1, Expected);

            Assert.True(FoxRunInboundProtobuf.TryDecodeString(field, out var decodedField, out var fieldError));
            Assert.Empty(fieldError);
            Assert.Equal(Expected, decodedField);
            Assert.True(FoxRunInboundProtobuf.TryRead(payload.ToArray(), 1, out string decodedPayload, out var payloadError));
            Assert.Empty(payloadError);
            Assert.Equal(Expected, decodedPayload);
        }

        [Fact]
        public void VarintReaderRejectsPayloadBitsBeyondUInt64()
        {
            var payload = new byte[]
            {
                0x08,
                0x80, 0x80, 0x80, 0x80, 0x80,
                0x80, 0x80, 0x80, 0x80, 0x02
            };

            Assert.False(FoxRunInboundProtobuf.TryRead(payload, 1, out ulong value, out var error));
            Assert.Equal(0UL, value);
            Assert.NotEmpty(error);
        }

        [Fact]
        public void VarintReaderAcceptsTheMaximumUInt64Value()
        {
            var payload = new byte[]
            {
                0x08,
                0xff, 0xff, 0xff, 0xff, 0xff,
                0xff, 0xff, 0xff, 0xff, 0x01
            };

            Assert.True(FoxRunInboundProtobuf.TryRead(payload, 1, out ulong value, out var error));
            Assert.Empty(error);
            Assert.Equal(ulong.MaxValue, value);
        }

        [Fact]
        public void FieldReaderEnforcesTheProtobufFieldNumberLimit()
        {
            var legalFields = new System.Collections.Generic.List<FoxRunProtobufField>();
            var illegalFields = new System.Collections.Generic.List<FoxRunProtobufField>();

            Assert.True(FoxRunInboundProtobuf.TryReadFields(
                new byte[] { 0xf8, 0xff, 0xff, 0xff, 0x0f, 0x01 },
                legalFields,
                out var legalError));
            Assert.Empty(legalError);
            Assert.Single(legalFields);
            Assert.Equal(536_870_911, legalFields[0].Number);

            Assert.False(FoxRunInboundProtobuf.TryReadFields(
                new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10, 0x01 },
                illegalFields,
                out var illegalError));
            Assert.Empty(illegalFields);
            Assert.Contains("tag", illegalError, StringComparison.OrdinalIgnoreCase);

            Assert.False(FoxRunInboundProtobuf.TryReadFields(
                new byte[] { 0x88, 0x80, 0x80, 0x80, 0x80, 0x01, 0x01 },
                illegalFields,
                out var wrappingError));
            Assert.Empty(illegalFields);
            Assert.Contains("tag", wrappingError, StringComparison.OrdinalIgnoreCase);

            Assert.False(FoxRunInboundProtobuf.TryRead(
                new byte[] { 0x88, 0x80, 0x80, 0x80, 0x80, 0x01, 0x01 },
                1,
                out ulong wrappingValue,
                out var generatedReaderError));
            Assert.Equal(0UL, wrappingValue);
            Assert.Contains("tag", generatedReaderError, StringComparison.OrdinalIgnoreCase);
        }
    }
}
