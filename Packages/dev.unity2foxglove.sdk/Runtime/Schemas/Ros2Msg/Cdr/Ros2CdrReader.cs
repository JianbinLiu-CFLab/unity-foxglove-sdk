// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Cdr
// Purpose: Minimal XCDR1 little-endian reader for packaged Foxglove ROS 2 .msg payloads.

using System;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.Schemas.Ros2Msg
{
    /// <summary>
    /// Reads the little-endian XCDR1 payloads emitted by <see cref="Ros2CdrWriter"/>.
    /// Member alignment is measured from byte offset 4, after the CDR encapsulation header.
    /// </summary>
    public sealed class Ros2CdrReader
    {
        private const int AlignmentOrigin = 4;
        private readonly byte[] _data;
        private int _offset;

        /// <summary>Create a reader over one ROS 2 CDR payload.</summary>
        public Ros2CdrReader(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (_data.Length < AlignmentOrigin)
                throw new InvalidDataException("ROS2 CDR payload is shorter than the four-byte encapsulation header.");
            if (_data[0] != 0x00 || _data[1] != 0x01)
                throw new NotSupportedException("Only little-endian XCDR1 payloads with encapsulation header 00 01 are supported.");
            if (_data[2] != 0x00 || _data[3] != 0x00)
                throw new NotSupportedException("CDR encapsulation option bytes must be zero.");

            LittleEndian = true;
            _offset = AlignmentOrigin;
        }

        /// <summary>Current read offset in bytes from the start of the payload.</summary>
        public int Offset => _offset;

        /// <summary>True when the encapsulation header selected little-endian CDR.</summary>
        public bool LittleEndian { get; }

        /// <summary>Read a ROS 2 bool encoded as one byte.</summary>
        public bool ReadBool()
        {
            var value = ReadUInt8();
            if (value > 1)
                throw new InvalidDataException("ROS2 CDR bool value must be 0 or 1.");
            return value != 0;
        }

        /// <summary>Read an unsigned 8-bit integer.</summary>
        public byte ReadUInt8()
        {
            Ensure(1);
            return _data[_offset++];
        }

        /// <summary>Read a signed 32-bit integer.</summary>
        public int ReadInt32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToInt32(_data, _offset)
                : BitConverter.ToInt32(CopyReversedBytes(4), 0);
            _offset += 4;
            return value;
        }

        /// <summary>Read an unsigned 32-bit integer.</summary>
        public uint ReadUInt32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToUInt32(_data, _offset)
                : BitConverter.ToUInt32(CopyReversedBytes(4), 0);
            _offset += 4;
            return value;
        }

        /// <summary>Read a signed 64-bit integer.</summary>
        public long ReadInt64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToInt64(_data, _offset)
                : BitConverter.ToInt64(CopyReversedBytes(8), 0);
            _offset += 8;
            return value;
        }

        /// <summary>Read an unsigned 64-bit integer.</summary>
        public ulong ReadUInt64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToUInt64(_data, _offset)
                : BitConverter.ToUInt64(CopyReversedBytes(8), 0);
            _offset += 8;
            return value;
        }

        /// <summary>Read a 32-bit floating-point value.</summary>
        public float ReadFloat32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToSingle(_data, _offset)
                : BitConverter.ToSingle(CopyReversedBytes(4), 0);
            _offset += 4;
            return value;
        }

        /// <summary>Read a 64-bit floating-point value.</summary>
        public double ReadFloat64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.IsLittleEndian
                ? BitConverter.ToDouble(_data, _offset)
                : BitConverter.ToDouble(CopyReversedBytes(8), 0);
            _offset += 8;
            return value;
        }

        /// <summary>Read a ROS 2 string, encoded as uint32 length including trailing NUL.</summary>
        public string ReadString()
        {
            var length = CheckedLength(ReadUInt32(), "ROS2 CDR string length");
            if (length == 0)
                throw new InvalidDataException("ROS2 CDR string length must include a trailing NUL byte.");
            Ensure(length);
            if (_data[_offset + length - 1] != 0x00)
                throw new InvalidDataException("ROS2 CDR string is missing the trailing NUL byte.");

            var value = Encoding.UTF8.GetString(_data, _offset, length - 1);
            _offset += length;
            return value;
        }

        /// <summary>Read a uint8 sequence.</summary>
        public byte[] ReadByteArray()
        {
            var length = CheckedLength(ReadUInt32(), "ROS2 CDR byte sequence length");
            Ensure(length);
            var value = new byte[length];
            Buffer.BlockCopy(_data, _offset, value, 0, length);
            _offset += length;
            return value;
        }

        /// <summary>Read a float64 sequence.</summary>
        public double[] ReadFloat64Sequence()
        {
            var length = CheckedLength(ReadUInt32(), "ROS2 CDR float64 sequence length", 8);
            var value = new double[length];
            for (var i = 0; i < length; i++)
                value[i] = ReadFloat64();
            return value;
        }

        /// <summary>Read a uint32 sequence.</summary>
        public uint[] ReadUInt32Sequence()
        {
            var length = CheckedLength(ReadUInt32(), "ROS2 CDR uint32 sequence length", 4);
            var value = new uint[length];
            for (var i = 0; i < length; i++)
                value[i] = ReadUInt32();
            return value;
        }

        /// <summary>Read a fixed-size float64 array.</summary>
        public double[] ReadFloat64Fixed(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Fixed array length cannot be negative.");

            var value = new double[length];
            for (var i = 0; i < length; i++)
                value[i] = ReadFloat64();
            return value;
        }

        /// <summary>Read a composite sequence length.</summary>
        public int ReadSequenceLength()
        {
            return CheckedLength(ReadUInt32(), "ROS2 CDR sequence length");
        }

        private int CheckedLength(uint value, string label, int minElementByteCount = 1)
        {
            if (value > int.MaxValue)
                throw new InvalidDataException(label + " exceeds the supported int32 range.");

            var length = (int)value;
            if (length > 0 && minElementByteCount > 0 &&
                (long)length * minElementByteCount > _data.Length - _offset)
                throw new InvalidDataException(label + " exceeds the remaining CDR payload bytes.");
            return length;
        }

        private void Align(int alignment)
        {
            var relative = (_offset - AlignmentOrigin) % alignment;
            if (relative == 0)
                return;

            _offset += alignment - relative;
        }

        private byte[] CopyReversedBytes(int length)
        {
            var bytes = new byte[length];
            Buffer.BlockCopy(_data, _offset, bytes, 0, length);
            Array.Reverse(bytes);
            return bytes;
        }

        private void Ensure(int count)
        {
            if (count < 0 || _offset > _data.Length - count)
                throw new EndOfStreamException("ROS2 CDR payload ended before the requested field could be read.");
        }
    }
}
