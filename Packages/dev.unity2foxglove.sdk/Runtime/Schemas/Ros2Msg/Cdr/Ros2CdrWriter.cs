// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Cdr
// Purpose: Minimal XCDR1 little-endian writer for ROS 2 .msg smoke payloads.

using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>
    /// Writes a minimal ROS 2 CDR payload using XCDR1 plain little-endian rules.
    /// The payload starts with the RTPS serialized-payload encapsulation header
    /// <c>00 01 00 00</c>; member alignment is measured from byte offset 4.
    /// </summary>
    public sealed class Ros2CdrWriter
    {
        private const int AlignmentOrigin = 4;
        private readonly List<byte> _buffer = new List<byte>();

        /// <summary>Create a writer initialized with a little-endian CDR encapsulation header.</summary>
        public Ros2CdrWriter()
        {
            _buffer.Add(0x00);
            _buffer.Add(0x01);
            _buffer.Add(0x00);
            _buffer.Add(0x00);
        }

        /// <summary>Current write offset in bytes from the start of the payload.</summary>
        public int Position => _buffer.Count;

        /// <summary>Write a ROS 2 bool as one byte.</summary>
        public void WriteBool(bool value)
        {
            WriteUInt8(value ? (byte)1 : (byte)0);
        }

        /// <summary>Write an unsigned 8-bit integer.</summary>
        public void WriteUInt8(byte value)
        {
            _buffer.Add(value);
        }

        /// <summary>Write a signed 32-bit integer.</summary>
        public void WriteInt32(int value)
        {
            Align(4);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write an unsigned 32-bit integer.</summary>
        public void WriteUInt32(uint value)
        {
            Align(4);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write a signed 64-bit integer.</summary>
        public void WriteInt64(long value)
        {
            Align(8);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write an unsigned 64-bit integer.</summary>
        public void WriteUInt64(ulong value)
        {
            Align(8);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write a 32-bit floating-point value.</summary>
        public void WriteFloat32(float value)
        {
            Align(4);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write a 64-bit floating-point value.</summary>
        public void WriteFloat64(double value)
        {
            Align(8);
            WriteLittleEndian(BitConverter.GetBytes(value));
        }

        /// <summary>Write a ROS 2 string, encoded as uint32 length including trailing NUL.</summary>
        public void WriteString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteUInt32(checked((uint)bytes.Length + 1U));
            _buffer.AddRange(bytes);
            _buffer.Add(0x00);
        }

        /// <summary>Write a uint8 sequence.</summary>
        public void WriteByteArray(byte[] value)
        {
            value ??= Array.Empty<byte>();
            WriteUInt32(checked((uint)value.Length));
            _buffer.AddRange(value);
        }

        /// <summary>Write a float64 sequence.</summary>
        public void WriteFloat64Sequence(IReadOnlyList<double> values)
        {
            values ??= Array.Empty<double>();
            WriteUInt32(checked((uint)values.Count));
            for (var i = 0; i < values.Count; i++)
                WriteFloat64(values[i]);
        }

        /// <summary>Write a uint32 sequence.</summary>
        public void WriteUInt32Sequence(IReadOnlyList<uint> values)
        {
            values ??= Array.Empty<uint>();
            WriteUInt32(checked((uint)values.Count));
            for (var i = 0; i < values.Count; i++)
                WriteUInt32(values[i]);
        }

        /// <summary>Write a fixed-size float64 array.</summary>
        public void WriteFloat64Fixed(IReadOnlyList<double> values, int expectedLength, string fieldName)
        {
            if (values == null)
                throw new ArgumentNullException(fieldName ?? nameof(values));
            if (values.Count != expectedLength)
                throw new ArgumentException($"{fieldName ?? "float64 array"} must contain exactly {expectedLength} values.", fieldName ?? nameof(values));

            for (var i = 0; i < values.Count; i++)
                WriteFloat64(values[i]);
        }

        /// <summary>Write a sequence of composite elements.</summary>
        public void WriteSequenceLength(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Sequence length cannot be negative.");
            WriteUInt32((uint)count);
        }

        /// <summary>Return the completed payload bytes.</summary>
        public byte[] ToArray()
        {
            return _buffer.ToArray();
        }

        private void Align(int alignment)
        {
            var relative = (_buffer.Count - AlignmentOrigin) % alignment;
            if (relative == 0)
                return;

            var padding = alignment - relative;
            for (var i = 0; i < padding; i++)
                _buffer.Add(0x00);
        }

        private void WriteLittleEndian(byte[] bytes)
        {
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            _buffer.AddRange(bytes);
        }
    }
}
