// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Cdr
// Purpose: Minimal XCDR1 little-endian writer for ROS 2 .msg smoke payloads.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg
{
    /// <summary>
    /// Writes a minimal ROS 2 CDR payload using XCDR1 plain little-endian rules.
    /// The payload starts with the RTPS serialized-payload encapsulation header
    /// <c>00 01 00 00</c>; member alignment is measured from byte offset 4.
    /// </summary>
    public sealed class Ros2CdrWriter
    {
        private const int AlignmentOrigin = 4;
        private byte[] _buffer;
        private int _position;
        private readonly int _maximumBytes;

        /// <summary>Create a writer initialized with a little-endian CDR encapsulation header.</summary>
        public Ros2CdrWriter()
            : this(AlignmentOrigin)
        {
        }

        /// <summary>Create a writer with an approximate output capacity hint.</summary>
        public Ros2CdrWriter(int capacityBytes)
            : this(capacityBytes, int.MaxValue)
        {
        }

        /// <summary>
        /// Create a writer whose backing buffer and final clone can never
        /// exceed <paramref name="maximumBytes"/>.
        /// </summary>
        public Ros2CdrWriter(int capacityBytes, int maximumBytes)
        {
            if (maximumBytes < AlignmentOrigin)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            var initialCapacity = Math.Max(AlignmentOrigin, capacityBytes);
            if (initialCapacity > maximumBytes)
                throw new ArgumentOutOfRangeException(nameof(capacityBytes));

            _maximumBytes = maximumBytes;
            _buffer = new byte[initialCapacity];
            _buffer[0] = 0x00;
            _buffer[1] = 0x01;
            _buffer[2] = 0x00;
            _buffer[3] = 0x00;
            _position = AlignmentOrigin;
        }

        /// <summary>Current write offset in bytes from the start of the payload.</summary>
        public int Position => _position;

        /// <summary>Write a ROS 2 bool as one byte.</summary>
        public void WriteBool(bool value)
        {
            WriteUInt8(value ? (byte)1 : (byte)0);
        }

        /// <summary>Write an unsigned 8-bit integer.</summary>
        public void WriteUInt8(byte value)
        {
            EnsureCapacity(1);
            _buffer[_position++] = value;
        }

        /// <summary>Write a signed 16-bit integer.</summary>
        public void WriteInt16(short value)
        {
            Align(2);
            EnsureCapacity(2);
            BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_position, 2), value);
            _position += 2;
        }

        /// <summary>Write an unsigned 16-bit integer.</summary>
        public void WriteUInt16(ushort value)
        {
            Align(2);
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_position, 2), value);
            _position += 2;
        }

        /// <summary>Write a signed 32-bit integer.</summary>
        public void WriteInt32(int value)
        {
            Align(4);
            EnsureCapacity(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), value);
            _position += 4;
        }

        /// <summary>Write an unsigned 32-bit integer.</summary>
        public void WriteUInt32(uint value)
        {
            Align(4);
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_position, 4), value);
            _position += 4;
        }

        /// <summary>Write a signed 64-bit integer.</summary>
        public void WriteInt64(long value)
        {
            Align(8);
            EnsureCapacity(8);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), value);
            _position += 8;
        }

        /// <summary>Write an unsigned 64-bit integer.</summary>
        public void WriteUInt64(ulong value)
        {
            Align(8);
            EnsureCapacity(8);
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_position, 8), value);
            _position += 8;
        }

        /// <summary>Write a 32-bit floating-point value.</summary>
        public void WriteFloat32(float value)
        {
            Align(4);
            EnsureCapacity(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_position, 4), BitConverter.SingleToInt32Bits(value));
            _position += 4;
        }

        /// <summary>Write a 64-bit floating-point value.</summary>
        public void WriteFloat64(double value)
        {
            Align(8);
            EnsureCapacity(8);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_position, 8), BitConverter.DoubleToInt64Bits(value));
            _position += 8;
        }

        /// <summary>Write a ROS 2 string, encoded as uint32 length including trailing NUL. Null strings are encoded as empty strings.</summary>
        public void WriteString(string value)
        {
            value ??= string.Empty;
            Align(4);
            var lengthPosition = _position;
            EnsureCapacity(4 + Encoding.UTF8.GetByteCount(value) + 1);
            _position += 4;
            var byteCount = Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _position);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(lengthPosition, 4), checked((uint)byteCount + 1U));
            _position += byteCount;
            _buffer[_position++] = 0x00;
        }

        /// <summary>Write a required uint8 sequence from an array.</summary>
        public void WriteByteArray(byte[] value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            WriteByteArray(value.AsSpan());
        }

        /// <summary>Write a uint8 sequence from a span without requiring an intermediate array.</summary>
        public void WriteByteArray(ReadOnlySpan<byte> value)
        {
            WriteUInt32(checked((uint)value.Length));
            EnsureCapacity(value.Length);
            value.CopyTo(_buffer.AsSpan(_position, value.Length));
            _position += value.Length;
        }

        /// <summary>Write a required float64 sequence.</summary>
        public void WriteFloat64Sequence(IReadOnlyList<double> values)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            WriteUInt32(checked((uint)values.Count));
            for (var i = 0; i < values.Count; i++)
                WriteFloat64(values[i]);
        }

        /// <summary>Write a uint32 sequence. Null lists are encoded as empty sequences; builders must reject null when the field is required.</summary>
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
            FoxgloveProfiler.Global.BeginSample("Ros2CdrWriter.ToArray");
            try
            {
                if (_position > _maximumBytes)
                    throw new Ros2CdrWriterBudgetExceededException(_maximumBytes);
                var result = new byte[_position];
                Buffer.BlockCopy(_buffer, 0, result, 0, _position);
                return result;
            }
            finally
            {
                FoxgloveProfiler.Global.EndSample();
            }
        }

        private void Align(int alignment)
        {
            var relative = (_position - AlignmentOrigin) % alignment;
            if (relative == 0)
                return;

            var padding = alignment - relative;
            EnsureCapacity(padding);
            _buffer.AsSpan(_position, padding).Clear();
            _position += padding;
        }

        private void EnsureCapacity(int additionalBytes)
        {
            var required = checked(_position + additionalBytes);
            if (required > _maximumBytes)
                throw new Ros2CdrWriterBudgetExceededException(_maximumBytes);
            if (required <= _buffer.Length)
                return;

            var doubled = _buffer.Length <= int.MaxValue / 2 ? _buffer.Length * 2 : int.MaxValue;
            var newLength = Math.Min(_maximumBytes, Math.Max(doubled, required));

            Array.Resize(ref _buffer, newLength);
        }
    }

    /// <summary>Typed signal for a bounded CDR writer rejecting a payload.</summary>
    public sealed class Ros2CdrWriterBudgetExceededException : InvalidOperationException
    {
        internal Ros2CdrWriterBudgetExceededException(int maximumBytes)
            : this($"ROS 2 CDR payload exceeds the {maximumBytes}-byte budget.")
        {
        }

        internal Ros2CdrWriterBudgetExceededException(string message)
            : base(message)
        {
        }
    }
}
