// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Builders
// Purpose: Low-allocation hand-written serializer for unity2foxglove.Imu.

using System;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace Foxglove.Schemas
{
    /// <summary>
    /// Build serialized <c>unity2foxglove.Imu</c> protobuf payloads without generated DTOs.
    /// </summary>
    public static class ImuMessageBuilder
    {
        private const int CovarianceCount = 9;

        private static readonly double[] s_zeroCovariance =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0
        };

        private static readonly double[] s_unknownOrientationCovariance =
        {
            -1, 0, 0, 0, 0, 0, 0, 0, 0
        };

        /// <summary>
        /// Serialize a unity2foxglove.Imu sample to protobuf bytes.
        /// </summary>
        /// <param name="unixNs">Unix timestamp in nanoseconds.</param>
        /// <param name="frameId">Frame identifier for the IMU sample.</param>
        /// <param name="linearAcceleration">Body-frame linear acceleration, in m/s^2.</param>
        /// <param name="angularVelocity">Body-frame angular velocity, in rad/s.</param>
        /// <param name="orientation">Body orientation as Foxglove-space quaternion.</param>
        /// <param name="includeOrientation">
        /// True to include the <c>orientation</c> message; false keeps message fields
        /// minimal but still exposes orientation covariance as unknown.
        /// </param>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            UnityEngine.Vector3 linearAcceleration,
            UnityEngine.Vector3 angularVelocity,
            UnityEngine.Quaternion orientation,
            bool includeOrientation)
            => Serialize(
                unixNs,
                frameId,
                linearAcceleration,
                angularVelocity,
                orientation,
                includeOrientation,
                s_zeroCovariance,
                s_zeroCovariance,
                s_zeroCovariance);

        /// <summary>
        /// Serialize a unity2foxglove.Imu sample to protobuf bytes with explicit covariance values.
        /// </summary>
        /// <param name="unixNs">Unix timestamp in nanoseconds.</param>
        /// <param name="frameId">Frame identifier for the IMU sample.</param>
        /// <param name="linearAcceleration">Body-frame linear acceleration, in m/s^2.</param>
        /// <param name="angularVelocity">Body-frame angular velocity, in rad/s.</param>
        /// <param name="orientation">Body orientation as Foxglove-space quaternion.</param>
        /// <param name="includeOrientation">
        /// True to include the <c>orientation</c> message; false keeps message fields
        /// minimal but still exposes orientation covariance as unknown.
        /// </param>
        /// <param name="orientationCovariance">Orientation covariance values.</param>
        /// <param name="angularVelocityCovariance">Angular velocity covariance values.</param>
        /// <param name="linearAccelerationCovariance">Linear acceleration covariance values.</param>
        public static byte[] Serialize(
            ulong unixNs,
            string frameId,
            UnityEngine.Vector3 linearAcceleration,
            UnityEngine.Vector3 angularVelocity,
            UnityEngine.Quaternion orientation,
            bool includeOrientation,
            IReadOnlyList<double> orientationCovariance,
            IReadOnlyList<double> angularVelocityCovariance,
            IReadOnlyList<double> linearAccelerationCovariance)
        {
            if (frameId == null)
                frameId = string.Empty;

            if (includeOrientation)
                ValidateCovariance(orientationCovariance, nameof(orientationCovariance));
            ValidateCovariance(angularVelocityCovariance, nameof(angularVelocityCovariance));
            ValidateCovariance(linearAccelerationCovariance, nameof(linearAccelerationCovariance));

            var buffer = new byte[ComputeSerializedSize(unixNs, frameId, includeOrientation)];
            using var output = new CodedOutputStream(buffer);

            WriteTimestamp(output, unixNs);
            WriteStringField(output, 2, frameId);
            WriteVec3Field(output, 3, linearAcceleration);
            WriteVec3Field(output, 4, angularVelocity);

            if (includeOrientation)
                WriteQuatField(output, 5, orientation);

            WriteDoublePackedArray(output, 6, includeOrientation
                ? orientationCovariance
                : s_unknownOrientationCovariance);
            WriteDoublePackedArray(output, 7, angularVelocityCovariance);
            WriteDoublePackedArray(output, 8, linearAccelerationCovariance);

            output.Flush();
            output.CheckNoSpaceLeft();
            return buffer;
        }

        private static void WriteTimestamp(CodedOutputStream output, ulong unixNs)
        {
            var seconds = (long)(unixNs / 1_000_000_000UL);
            var nanos = (int)(unixNs % 1_000_000_000UL);

            var secondsSize = CodedOutputStream.ComputeInt64Size(seconds);
            var nanosSize = CodedOutputStream.ComputeInt32Size(nanos);
            var payloadSize = CodedOutputStream.ComputeTagSize(1) + secondsSize
                + CodedOutputStream.ComputeTagSize(2) + nanosSize;

            output.WriteTag(1, WireFormat.WireType.LengthDelimited);
            output.WriteLength(payloadSize);
            output.WriteTag(1, WireFormat.WireType.Varint);
            output.WriteInt64(seconds);
            output.WriteTag(2, WireFormat.WireType.Varint);
            output.WriteInt32(nanos);
        }

        private static void WriteVec3Field(CodedOutputStream output, int fieldNumber, UnityEngine.Vector3 value)
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteLength(ComputeDoubleMessagePayloadSize(3));
            output.WriteTag(1, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.x);
            output.WriteTag(2, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.y);
            output.WriteTag(3, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.z);
        }

        private static void WriteQuatField(CodedOutputStream output, int fieldNumber, UnityEngine.Quaternion value)
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteLength(ComputeDoubleMessagePayloadSize(4));
            output.WriteTag(1, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.x);
            output.WriteTag(2, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.y);
            output.WriteTag(3, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.z);
            output.WriteTag(4, WireFormat.WireType.Fixed64);
            output.WriteDouble(value.w);
        }

        private static int ComputeDoubleMessagePayloadSize(int fieldCount)
        {
            var size = 0;
            for (var fieldNumber = 1; fieldNumber <= fieldCount; fieldNumber++)
                size += CodedOutputStream.ComputeTagSize(fieldNumber) + CodedOutputStream.ComputeDoubleSize(0d);
            return size;
        }

        private static int ComputeSerializedSize(ulong unixNs, string frameId, bool includeOrientation)
        {
            var size = ComputeTimestampFieldSize(unixNs);
            size += CodedOutputStream.ComputeTagSize(2) + CodedOutputStream.ComputeStringSize(frameId);
            size += ComputeDoubleMessageFieldSize(3);
            size += ComputeDoubleMessageFieldSize(4);
            if (includeOrientation)
                size += ComputeDoubleMessageFieldSize(5);
            size += ComputePackedDoubleFieldSize(6);
            size += ComputePackedDoubleFieldSize(7);
            size += ComputePackedDoubleFieldSize(8);
            return size;
        }

        private static int ComputeTimestampFieldSize(ulong unixNs)
        {
            var seconds = (long)(unixNs / 1_000_000_000UL);
            var nanos = (int)(unixNs % 1_000_000_000UL);
            var payloadSize = CodedOutputStream.ComputeTagSize(1) + CodedOutputStream.ComputeInt64Size(seconds)
                + CodedOutputStream.ComputeTagSize(2) + CodedOutputStream.ComputeInt32Size(nanos);
            return CodedOutputStream.ComputeTagSize(1)
                + CodedOutputStream.ComputeLengthSize(payloadSize)
                + payloadSize;
        }

        private static int ComputeDoubleMessageFieldSize(int fieldNumber)
        {
            var payloadSize = ComputeDoubleMessagePayloadSize(fieldNumber == 5 ? 4 : 3);
            return CodedOutputStream.ComputeTagSize(fieldNumber)
                + CodedOutputStream.ComputeLengthSize(payloadSize)
                + payloadSize;
        }

        private static int ComputePackedDoubleFieldSize(int fieldNumber)
        {
            var payloadSize = CovarianceCount * sizeof(double);
            return CodedOutputStream.ComputeTagSize(fieldNumber)
                + CodedOutputStream.ComputeLengthSize(payloadSize)
                + payloadSize;
        }

        private static void WriteStringField(CodedOutputStream output, int fieldNumber, string value)
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteString(value);
        }

        private static void WriteDoublePackedArray(CodedOutputStream output, int fieldNumber, IReadOnlyList<double> values)
        {
            ValidateCovariance(values, nameof(values));

            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteLength(CovarianceCount * sizeof(double));
            for (var i = 0; i < CovarianceCount; i++)
                output.WriteDouble(values[i]);
        }

        private static void ValidateCovariance(IReadOnlyList<double> values, string parameterName)
        {
            if (values == null)
                throw new ArgumentNullException(parameterName);
            if (values.Count != CovarianceCount)
                throw new ArgumentException($"Expected {CovarianceCount} covariance values.", parameterName);
        }
    }
}
