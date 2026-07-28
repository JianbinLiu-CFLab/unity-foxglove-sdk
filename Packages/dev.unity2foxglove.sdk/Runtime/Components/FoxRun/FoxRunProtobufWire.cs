// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Allocation-bounded primitive Protobuf wire helpers used by generated FoxRun code.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Primitive wire writer used by generated FoxRun Protobuf publishers.</summary>
    public static class FoxRunProtobufWire
    {
        public static void WriteBool(List<byte> buffer, int fieldNumber, bool value)
        {
            if (value)
                WriteVarintField(buffer, fieldNumber, 1UL);
        }

        public static void WriteInt32(List<byte> buffer, int fieldNumber, int value)
        {
            if (value != 0)
                WriteVarintField(buffer, fieldNumber, unchecked((ulong)(long)value));
        }

        public static void WriteUInt32(List<byte> buffer, int fieldNumber, uint value)
        {
            if (value != 0)
                WriteVarintField(buffer, fieldNumber, value);
        }

        public static void WriteInt64(List<byte> buffer, int fieldNumber, long value)
        {
            if (value != 0)
                WriteVarintField(buffer, fieldNumber, unchecked((ulong)value));
        }

        public static void WriteUInt64(List<byte> buffer, int fieldNumber, ulong value)
        {
            if (value != 0)
                WriteVarintField(buffer, fieldNumber, value);
        }

        public static void WriteFloat(List<byte> buffer, int fieldNumber, float value)
        {
            if (value == 0f)
                return;
            WriteTag(buffer, fieldNumber, 5);
            WriteFixed32(buffer, new FloatBits { Float = value }.UInt32);
        }

        public static void WriteDouble(List<byte> buffer, int fieldNumber, double value)
        {
            if (value == 0d)
                return;
            WriteTag(buffer, fieldNumber, 1);
            WriteFixed64(buffer, new DoubleBits { Double = value }.UInt64);
        }

        public static void WriteString(List<byte> buffer, int fieldNumber, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;
            WriteBytes(buffer, fieldNumber, Encoding.UTF8.GetBytes(value));
        }

        public static void WriteVector2(List<byte> buffer, int fieldNumber, Vector2 value)
        {
            var nested = new List<byte>(10);
            WriteFloat(nested, 1, value.x);
            WriteFloat(nested, 2, value.y);
            WriteBytes(buffer, fieldNumber, nested);
        }

        public static void WriteVector3(List<byte> buffer, int fieldNumber, Vector3 value)
        {
            var nested = new List<byte>(15);
            WriteFloat(nested, 1, value.x);
            WriteFloat(nested, 2, value.y);
            WriteFloat(nested, 3, value.z);
            WriteBytes(buffer, fieldNumber, nested);
        }

        public static void WriteQuaternion(List<byte> buffer, int fieldNumber, Quaternion value)
        {
            var nested = new List<byte>(20);
            WriteFloat(nested, 1, value.x);
            WriteFloat(nested, 2, value.y);
            WriteFloat(nested, 3, value.z);
            WriteFloat(nested, 4, value.w);
            WriteBytes(buffer, fieldNumber, nested);
        }

        public static void WriteColor(List<byte> buffer, int fieldNumber, Color value)
        {
            var nested = new List<byte>(20);
            WriteFloat(nested, 1, value.r);
            WriteFloat(nested, 2, value.g);
            WriteFloat(nested, 3, value.b);
            WriteFloat(nested, 4, value.a);
            WriteBytes(buffer, fieldNumber, nested);
        }

        public static void WriteBytes(List<byte> buffer, int fieldNumber, IList<byte> value)
        {
            if (value == null || value.Count == 0)
                return;
            WriteTag(buffer, fieldNumber, 2);
            WriteVarint(buffer, (ulong)value.Count);
            for (var i = 0; i < value.Count; i++)
                buffer.Add(value[i]);
        }

        internal static void WriteTag(List<byte> buffer, int fieldNumber, int wireType)
        {
            if (fieldNumber <= 0)
                throw new ArgumentOutOfRangeException(nameof(fieldNumber));
            WriteVarint(buffer, ((ulong)fieldNumber << 3) | (uint)wireType);
        }

        internal static void WriteVarint(List<byte> buffer, ulong value)
        {
            while (value >= 0x80)
            {
                buffer.Add((byte)((value & 0x7f) | 0x80));
                value >>= 7;
            }
            buffer.Add((byte)value);
        }

        private static void WriteVarintField(List<byte> buffer, int fieldNumber, ulong value)
        {
            WriteTag(buffer, fieldNumber, 0);
            WriteVarint(buffer, value);
        }

        private static void WriteFixed32(List<byte> buffer, uint value)
        {
            buffer.Add((byte)value);
            buffer.Add((byte)(value >> 8));
            buffer.Add((byte)(value >> 16));
            buffer.Add((byte)(value >> 24));
        }

        private static void WriteFixed64(List<byte> buffer, ulong value)
        {
            for (var i = 0; i < 8; i++)
                buffer.Add((byte)(value >> (8 * i)));
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public uint UInt32;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits
        {
            [FieldOffset(0)] public double Double;
            [FieldOffset(0)] public ulong UInt64;
        }
    }

    /// <summary>One decoded field occurrence used by generated FoxRun Protobuf readers.</summary>
    public readonly struct FoxRunProtobufField
    {
        internal FoxRunProtobufField(int number, int wireType, byte[] value)
        {
            Number = number;
            WireType = wireType;
            Value = value ?? Array.Empty<byte>();
        }

        public int Number { get; }
        public int WireType { get; }
        public byte[] Value { get; }
    }

    /// <summary>Primitive wire reader used by generated FoxRun Protobuf inputs.</summary>
    public static class FoxRunInboundProtobuf
    {
        public static bool TryReadFields(byte[] payload, IList<FoxRunProtobufField> fields, out string error)
        {
            if (fields == null)
                throw new ArgumentNullException(nameof(fields));

            fields.Clear();
            payload ??= Array.Empty<byte>();
            var position = 0;
            while (position < payload.Length)
            {
                if (!TryReadVarint(payload, ref position, out var tag) || tag == 0)
                {
                    error = "Malformed Protobuf field tag.";
                    return false;
                }

                var number = (int)(tag >> 3);
                var wireType = (int)(tag & 7);
                if (number <= 0 || !TryReadValue(payload, ref position, wireType, out var value))
                {
                    error = "Malformed Protobuf field value.";
                    return false;
                }

                fields.Add(new FoxRunProtobufField(number, wireType, value));
            }

            error = string.Empty;
            return true;
        }

        public static bool TryReadMessage(byte[] payload, int fieldNumber, out byte[] value, out string error)
            => TryReadBytes(payload, fieldNumber, out value, out error);

        public static bool TryDecodeBool(FoxRunProtobufField field, out bool value, out string error)
            => TryDecodeVarint(field, out value, raw => raw != 0, out error);

        public static bool TryDecodeInt32(FoxRunProtobufField field, out int value, out string error)
            => TryDecodeVarint(field, out value, raw => unchecked((int)raw), out error);

        public static bool TryDecodeInt8(FoxRunProtobufField field, out sbyte value, out string error)
        {
            value = default;
            return TryDecodeInt32(field, out var raw, out error)
                   && TryAssignInt8(raw, out value, out error);
        }

        public static bool TryDecodeInt16(FoxRunProtobufField field, out short value, out string error)
        {
            value = default;
            return TryDecodeInt32(field, out var raw, out error)
                   && TryAssignInt16(raw, out value, out error);
        }

        public static bool TryDecodeUInt32(FoxRunProtobufField field, out uint value, out string error)
            => TryDecodeVarint(field, out value, raw => unchecked((uint)raw), out error);

        public static bool TryDecodeUInt8(FoxRunProtobufField field, out byte value, out string error)
        {
            value = default;
            return TryDecodeUInt32(field, out var raw, out error)
                   && TryAssignUInt8(raw, out value, out error);
        }

        public static bool TryDecodeUInt16(FoxRunProtobufField field, out ushort value, out string error)
        {
            value = default;
            return TryDecodeUInt32(field, out var raw, out error)
                   && TryAssignUInt16(raw, out value, out error);
        }

        public static bool TryDecodeInt64(FoxRunProtobufField field, out long value, out string error)
            => TryDecodeVarint(field, out value, raw => unchecked((long)raw), out error);

        public static bool TryDecodeUInt64(FoxRunProtobufField field, out ulong value, out string error)
            => TryDecodeVarint(field, out value, raw => raw, out error);

        public static bool TryDecodeFloat(FoxRunProtobufField field, out float value, out string error)
        {
            value = default;
            if (field.WireType != 5 || field.Value == null || field.Value.Length != 4)
            {
                error = "Protobuf wire type does not match float.";
                return false;
            }
            var raw = (uint)(field.Value[0] | field.Value[1] << 8 | field.Value[2] << 16 | field.Value[3] << 24);
            value = new FloatBits { UInt32 = raw }.Float;
            error = string.Empty;
            return true;
        }

        public static bool TryDecodeDouble(FoxRunProtobufField field, out double value, out string error)
        {
            value = default;
            if (field.WireType != 1 || field.Value == null || field.Value.Length != 8)
            {
                error = "Protobuf wire type does not match double.";
                return false;
            }
            ulong raw = 0;
            for (var i = 0; i < 8; i++) raw |= (ulong)field.Value[i] << (8 * i);
            value = new DoubleBits { UInt64 = raw }.Double;
            error = string.Empty;
            return true;
        }

        public static bool TryDecodeString(FoxRunProtobufField field, out string value, out string error)
        {
            value = string.Empty;
            if (field.WireType != 2)
            {
                error = "Protobuf wire type does not match string.";
                return false;
            }
            try
            {
                value = Encoding.UTF8.GetString(field.Value ?? Array.Empty<byte>());
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = "Invalid Protobuf UTF-8 string: " + ex.Message;
                return false;
            }
        }

        public static bool TryDecodeMessage(FoxRunProtobufField field, out byte[] value, out string error)
        {
            value = field.Value ?? Array.Empty<byte>();
            if (field.WireType == 2)
            {
                error = string.Empty;
                return true;
            }
            error = "Protobuf wire type does not match message.";
            return false;
        }

        public static bool TryDecodeVector3(FoxRunProtobufField field, out Vector3 value, out string error)
        {
            value = default;
            if (!TryDecodeMessage(field, out var payload, out error))
                return false;
            if (!TryReadOptionalFloat(payload, 1, out var x, out error)
                || !TryReadOptionalFloat(payload, 2, out var y, out error)
                || !TryReadOptionalFloat(payload, 3, out var z, out error))
                return false;
            value = new Vector3 { x = x, y = y, z = z };
            return true;
        }

        public static bool TryDecodeVector2(FoxRunProtobufField field, out Vector2 value, out string error)
        {
            value = default;
            if (!TryDecodeMessage(field, out var payload, out error)
                || !TryReadOptionalFloat(payload, 1, out var x, out error)
                || !TryReadOptionalFloat(payload, 2, out var y, out error))
                return false;
            value = new Vector2 { x = x, y = y };
            return true;
        }

        public static bool TryDecodeQuaternion(FoxRunProtobufField field, out Quaternion value, out string error)
        {
            value = default;
            if (!TryDecodeMessage(field, out var payload, out error)
                || !TryReadOptionalFloat(payload, 1, out var x, out error)
                || !TryReadOptionalFloat(payload, 2, out var y, out error)
                || !TryReadOptionalFloat(payload, 3, out var z, out error)
                || !TryReadOptionalFloat(payload, 4, out var w, out error))
                return false;
            value = new Quaternion { x = x, y = y, z = z, w = w };
            return true;
        }

        public static bool TryDecodeColor(FoxRunProtobufField field, out Color value, out string error)
        {
            value = default;
            if (!TryDecodeMessage(field, out var payload, out error)
                || !TryReadOptionalFloat(payload, 1, out var r, out error)
                || !TryReadOptionalFloat(payload, 2, out var g, out error)
                || !TryReadOptionalFloat(payload, 3, out var b, out error)
                || !TryReadOptionalFloat(payload, 4, out var a, out error))
                return false;
            value = new Color { r = r, g = g, b = b, a = a };
            return true;
        }

        public static bool TryReadRepeatedFloat(FoxRunProtobufField field, IList<float> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (field.WireType == 5)
            {
                if (!TryDecodeFloat(field, out var value, out error)) return false;
                values.Add(value);
                return true;
            }
            if (field.WireType != 2 || field.Value == null || field.Value.Length % 4 != 0)
            {
                error = "Protobuf wire type does not match repeated float.";
                return false;
            }
            for (var offset = 0; offset < field.Value.Length; offset += 4)
            {
                var raw = (uint)(field.Value[offset] | field.Value[offset + 1] << 8 | field.Value[offset + 2] << 16 | field.Value[offset + 3] << 24);
                values.Add(new FloatBits { UInt32 = raw }.Float);
            }
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedDouble(FoxRunProtobufField field, IList<double> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (field.WireType == 1)
            {
                if (!TryDecodeDouble(field, out var value, out error)) return false;
                values.Add(value);
                return true;
            }
            if (field.WireType != 2 || field.Value == null || field.Value.Length % 8 != 0)
            {
                error = "Protobuf wire type does not match repeated double.";
                return false;
            }
            for (var offset = 0; offset < field.Value.Length; offset += 8)
            {
                ulong raw = 0;
                for (var i = 0; i < 8; i++) raw |= (ulong)field.Value[offset + i] << (8 * i);
                values.Add(new DoubleBits { UInt64 = raw }.Double);
            }
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedBool(FoxRunProtobufField field, IList<bool> values, out string error)
            => TryReadRepeatedVarint(field, values, raw => raw != 0, "bool", out error);

        public static bool TryReadRepeatedInt32(FoxRunProtobufField field, IList<int> values, out string error)
            => TryReadRepeatedVarint(field, values, raw => unchecked((int)raw), "int32", out error);

        public static bool TryReadRepeatedInt8(FoxRunProtobufField field, IList<sbyte> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var rawValues = new List<int>();
            if (!TryReadRepeatedInt32(field, rawValues, out error))
                return false;
            for (var index = 0; index < rawValues.Count; index++)
            {
                if (rawValues[index] < sbyte.MinValue || rawValues[index] > sbyte.MaxValue)
                {
                    error = "Protobuf int8 value is out of range.";
                    return false;
                }
            }
            for (var index = 0; index < rawValues.Count; index++)
                values.Add((sbyte)rawValues[index]);
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedInt16(FoxRunProtobufField field, IList<short> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var rawValues = new List<int>();
            if (!TryReadRepeatedInt32(field, rawValues, out error))
                return false;
            for (var index = 0; index < rawValues.Count; index++)
            {
                if (rawValues[index] < short.MinValue || rawValues[index] > short.MaxValue)
                {
                    error = "Protobuf int16 value is out of range.";
                    return false;
                }
            }
            for (var index = 0; index < rawValues.Count; index++)
                values.Add((short)rawValues[index]);
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedUInt32(FoxRunProtobufField field, IList<uint> values, out string error)
            => TryReadRepeatedVarint(field, values, raw => unchecked((uint)raw), "uint32", out error);

        public static bool TryReadRepeatedUInt8(FoxRunProtobufField field, IList<byte> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var rawValues = new List<uint>();
            if (!TryReadRepeatedUInt32(field, rawValues, out error))
                return false;
            for (var index = 0; index < rawValues.Count; index++)
            {
                if (rawValues[index] > byte.MaxValue)
                {
                    error = "Protobuf uint8 value is out of range.";
                    return false;
                }
            }
            for (var index = 0; index < rawValues.Count; index++)
                values.Add((byte)rawValues[index]);
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedUInt16(FoxRunProtobufField field, IList<ushort> values, out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var rawValues = new List<uint>();
            if (!TryReadRepeatedUInt32(field, rawValues, out error))
                return false;
            for (var index = 0; index < rawValues.Count; index++)
            {
                if (rawValues[index] > ushort.MaxValue)
                {
                    error = "Protobuf uint16 value is out of range.";
                    return false;
                }
            }
            for (var index = 0; index < rawValues.Count; index++)
                values.Add((ushort)rawValues[index]);
            error = string.Empty;
            return true;
        }

        public static bool TryReadRepeatedInt64(FoxRunProtobufField field, IList<long> values, out string error)
            => TryReadRepeatedVarint(field, values, raw => unchecked((long)raw), "int64", out error);

        public static bool TryReadRepeatedUInt64(FoxRunProtobufField field, IList<ulong> values, out string error)
            => TryReadRepeatedVarint(field, values, raw => raw, "uint64", out error);

        private static bool TryReadRepeatedVarint<T>(
            FoxRunProtobufField field,
            IList<T> values,
            Func<ulong, T> convert,
            string typeName,
            out string error)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (field.WireType == 0)
            {
                if (!TryDecodeVarint(field, out T value, convert, out error)) return false;
                values.Add(value);
                return true;
            }
            if (field.WireType != 2 || field.Value == null)
            {
                error = "Protobuf wire type does not match repeated " + typeName + ".";
                return false;
            }
            var position = 0;
            while (position < field.Value.Length)
            {
                if (!TryReadVarint(field.Value, ref position, out var raw))
                {
                    error = "Malformed packed Protobuf " + typeName + " value.";
                    return false;
                }
                values.Add(convert(raw));
            }
            error = string.Empty;
            return true;
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out bool value, out string error)
            => TryReadVarint(payload, fieldNumber, out var raw, out value, rawValue => rawValue != 0, out error);
        public static bool TryRead(byte[] payload, int fieldNumber, out int value, out string error)
            => TryReadVarint(payload, fieldNumber, out var raw, out value, rawValue => unchecked((int)rawValue), out error);
        public static bool TryRead(byte[] payload, int fieldNumber, out sbyte value, out string error)
        {
            value = default;
            return TryRead(payload, fieldNumber, out int raw, out error)
                   && TryAssignInt8(raw, out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out short value, out string error)
        {
            value = default;
            return TryRead(payload, fieldNumber, out int raw, out error)
                   && TryAssignInt16(raw, out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out uint value, out string error)
            => TryReadVarint(payload, fieldNumber, out var raw, out value, rawValue => unchecked((uint)rawValue), out error);
        public static bool TryRead(byte[] payload, int fieldNumber, out byte value, out string error)
        {
            value = default;
            return TryRead(payload, fieldNumber, out uint raw, out error)
                   && TryAssignUInt8(raw, out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out ushort value, out string error)
        {
            value = default;
            return TryRead(payload, fieldNumber, out uint raw, out error)
                   && TryAssignUInt16(raw, out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out long value, out string error)
            => TryReadVarint(payload, fieldNumber, out var raw, out value, rawValue => unchecked((long)rawValue), out error);
        public static bool TryRead(byte[] payload, int fieldNumber, out ulong value, out string error)
            => TryReadVarint(payload, fieldNumber, out var raw, out value, rawValue => rawValue, out error);
        public static bool TryRead(byte[] payload, int fieldNumber, out float value, out string error)
        {
            value = default;
            return TryReadFixed32(payload, fieldNumber, out var raw, out error)
                   && Assign(out value, new FloatBits { UInt32 = raw }.Float, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out double value, out string error)
        {
            value = default;
            return TryReadFixed64(payload, fieldNumber, out var raw, out error)
                   && Assign(out value, new DoubleBits { UInt64 = raw }.Double, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out string value, out string error)
        {
            value = string.Empty;
            if (!TryReadBytes(payload, fieldNumber, out var bytes, out error))
                return false;
            try
            {
                value = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = "Invalid Protobuf UTF-8 string: " + ex.Message;
                return false;
            }
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out Vector2 value, out string error)
        {
            value = default;
            return TryReadBytes(payload, fieldNumber, out var nested, out error)
                   && TryDecodeVector2(new FoxRunProtobufField(fieldNumber, 2, nested), out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out Vector3 value, out string error)
        {
            value = default;
            return TryReadBytes(payload, fieldNumber, out var nested, out error)
                   && TryDecodeVector3(new FoxRunProtobufField(fieldNumber, 2, nested), out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out Quaternion value, out string error)
        {
            value = default;
            return TryReadBytes(payload, fieldNumber, out var nested, out error)
                   && TryDecodeQuaternion(new FoxRunProtobufField(fieldNumber, 2, nested), out value, out error);
        }
        public static bool TryRead(byte[] payload, int fieldNumber, out Color value, out string error)
        {
            value = default;
            return TryReadBytes(payload, fieldNumber, out var nested, out error)
                   && TryDecodeColor(new FoxRunProtobufField(fieldNumber, 2, nested), out value, out error);
        }

        private static bool TryReadVarint<T>(byte[] payload, int fieldNumber, out ulong raw, out T value, Func<ulong, T> convert, out string error)
        {
            raw = 0; value = default;
            if (!TryFindField(payload, fieldNumber, 0, out var data, out error)) return false;
            if (data.Length == 0) { error = string.Empty; return true; }
            var position = 0;
            if (!TryReadVarint(data, ref position, out raw) || position != data.Length)
            {
                error = "Malformed Protobuf varint value.";
                return false;
            }
            value = convert(raw); error = string.Empty; return true;
        }

        private static bool TryDecodeVarint<T>(FoxRunProtobufField field, out T value, Func<ulong, T> convert, out string error)
        {
            value = default;
            if (field.WireType != 0)
            {
                error = "Protobuf wire type does not match varint.";
                return false;
            }
            var position = 0;
            if (!TryReadVarint(field.Value ?? Array.Empty<byte>(), ref position, out var raw) || position != (field.Value?.Length ?? 0))
            {
                error = "Malformed Protobuf varint value.";
                return false;
            }
            value = convert(raw);
            error = string.Empty;
            return true;
        }

        private static bool TryReadFixed32(byte[] payload, int fieldNumber, out uint value, out string error)
        {
            value = 0;
            if (!TryFindField(payload, fieldNumber, 5, out var data, out error)) return false;
            if (data.Length == 0) { error = string.Empty; return true; }
            if (data.Length != 4) { error = "Malformed Protobuf fixed32 value."; return false; }
            value = (uint)(data[0] | data[1] << 8 | data[2] << 16 | data[3] << 24); error = string.Empty; return true;
        }

        private static bool TryReadFixed64(byte[] payload, int fieldNumber, out ulong value, out string error)
        {
            value = 0;
            if (!TryFindField(payload, fieldNumber, 1, out var data, out error)) return false;
            if (data.Length == 0) { error = string.Empty; return true; }
            if (data.Length != 8) { error = "Malformed Protobuf fixed64 value."; return false; }
            for (var i = 0; i < 8; i++) value |= (ulong)data[i] << (8 * i);
            error = string.Empty; return true;
        }

        private static bool TryReadBytes(byte[] payload, int fieldNumber, out byte[] value, out string error)
            => TryFindField(payload, fieldNumber, 2, out value, out error);

        private static bool TryReadOptionalFloat(byte[] payload, int fieldNumber, out float value, out string error)
        {
            value = default;
            var fields = new List<FoxRunProtobufField>();
            if (!TryReadFields(payload, fields, out error)) return false;
            for (var index = fields.Count - 1; index >= 0; index--)
            {
                if (fields[index].Number != fieldNumber) continue;
                return TryDecodeFloat(fields[index], out value, out error);
            }
            error = string.Empty;
            return true;
        }

        private static bool TryFindField(byte[] payload, int fieldNumber, int expectedWireType, out byte[] value, out string error)
        {
            value = Array.Empty<byte>(); error = string.Empty;
            payload ??= Array.Empty<byte>();
            var position = 0; var found = false;
            while (position < payload.Length)
            {
                if (!TryReadVarint(payload, ref position, out var tag) || tag == 0)
                { error = "Malformed Protobuf field tag."; return false; }
                var number = (int)(tag >> 3); var wireType = (int)(tag & 7);
                if (!TryReadValue(payload, ref position, wireType, out var candidate))
                { error = "Malformed Protobuf field value."; return false; }
                if (number != fieldNumber) continue;
                if (wireType != expectedWireType)
                { error = "Protobuf wire type does not match the FoxRun contract."; return false; }
                value = candidate; found = true;
            }
            if (!found) { error = string.Empty; return true; }
            return true;
        }

        private static bool TryReadValue(byte[] payload, ref int position, int wireType, out byte[] value)
        {
            value = Array.Empty<byte>();
            switch (wireType)
            {
                case 0:
                    var start = position;
                    if (!TryReadVarint(payload, ref position, out _)) return false;
                    value = Slice(payload, start, position - start); return true;
                case 1:
                    if (position + 8 > payload.Length) return false;
                    value = Slice(payload, position, 8); position += 8; return true;
                case 2:
                    if (!TryReadVarint(payload, ref position, out var length) || length > (ulong)(payload.Length - position)) return false;
                    value = Slice(payload, position, (int)length); position += (int)length; return true;
                case 5:
                    if (position + 4 > payload.Length) return false;
                    value = Slice(payload, position, 4); position += 4; return true;
                default: return false;
            }
        }

        private static bool TryReadVarint(byte[] payload, ref int position, out ulong value)
        {
            value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (position >= payload.Length) return false;
                var b = payload[position++]; value |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }

        private static byte[] Slice(byte[] value, int offset, int count)
        {
            var result = new byte[count];
            Buffer.BlockCopy(value, offset, result, 0, count);
            return result;
        }

        private static bool Assign<T>(out T target, T value, out string error)
        {
            target = value; error = string.Empty; return true;
        }

        private static bool TryAssignInt8(int raw, out sbyte value, out string error)
        {
            value = default;
            if (raw < sbyte.MinValue || raw > sbyte.MaxValue)
            {
                error = "Protobuf int8 value is out of range.";
                return false;
            }
            value = (sbyte)raw;
            error = string.Empty;
            return true;
        }

        private static bool TryAssignInt16(int raw, out short value, out string error)
        {
            value = default;
            if (raw < short.MinValue || raw > short.MaxValue)
            {
                error = "Protobuf int16 value is out of range.";
                return false;
            }
            value = (short)raw;
            error = string.Empty;
            return true;
        }

        private static bool TryAssignUInt8(uint raw, out byte value, out string error)
        {
            value = default;
            if (raw > byte.MaxValue)
            {
                error = "Protobuf uint8 value is out of range.";
                return false;
            }
            value = (byte)raw;
            error = string.Empty;
            return true;
        }

        private static bool TryAssignUInt16(uint raw, out ushort value, out string error)
        {
            value = default;
            if (raw > ushort.MaxValue)
            {
                error = "Protobuf uint16 value is out of range.";
                return false;
            }
            value = (ushort)raw;
            error = string.Empty;
            return true;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits { [FieldOffset(0)] public float Float; [FieldOffset(0)] public uint UInt32; }
        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits { [FieldOffset(0)] public double Double; [FieldOffset(0)] public ulong UInt64; }
    }
}
