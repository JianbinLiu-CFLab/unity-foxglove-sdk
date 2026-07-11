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
    }
}
