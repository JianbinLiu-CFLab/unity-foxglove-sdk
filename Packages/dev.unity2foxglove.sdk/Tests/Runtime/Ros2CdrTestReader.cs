// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Test-only ROS 2 CDR reader used by Phase 91 byte-level checks.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Tests
{
    internal sealed class Ros2CdrTestReader
    {
        private const int AlignmentOrigin = 4;
        private readonly byte[] _data;
        private int _offset;

        public Ros2CdrTestReader(byte[] data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            if (_data.Length < 4 || _data[0] != 0 || _data[1] != 1 || _data[2] != 0 || _data[3] != 0)
                throw new ArgumentException("Payload is not a little-endian ROS 2 CDR payload.", nameof(data));
            _offset = AlignmentOrigin;
        }

        public int Offset => _offset;

        public byte ReadUInt8()
        {
            Ensure(1);
            return _data[_offset++];
        }

        public bool ReadBool()
        {
            return ReadUInt8() != 0;
        }

        public int ReadInt32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.ToInt32(_data, _offset);
            _offset += 4;
            return value;
        }

        public uint ReadUInt32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.ToUInt32(_data, _offset);
            _offset += 4;
            return value;
        }

        public long ReadInt64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.ToInt64(_data, _offset);
            _offset += 8;
            return value;
        }

        public ulong ReadUInt64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.ToUInt64(_data, _offset);
            _offset += 8;
            return value;
        }

        public float ReadFloat32()
        {
            Align(4);
            Ensure(4);
            var value = BitConverter.ToSingle(_data, _offset);
            _offset += 4;
            return value;
        }

        public double ReadFloat64()
        {
            Align(8);
            Ensure(8);
            var value = BitConverter.ToDouble(_data, _offset);
            _offset += 8;
            return value;
        }

        public string ReadString()
        {
            var length = checked((int)ReadUInt32());
            Ensure(length);
            if (length == 0)
                return string.Empty;

            var byteCount = _data[_offset + length - 1] == 0 ? length - 1 : length;
            var value = Encoding.UTF8.GetString(_data, _offset, byteCount);
            _offset += length;
            return value;
        }

        public byte[] ReadByteArray()
        {
            var length = checked((int)ReadUInt32());
            Ensure(length);
            var bytes = new byte[length];
            Array.Copy(_data, _offset, bytes, 0, length);
            _offset += length;
            return bytes;
        }

        public double[] ReadFloat64Sequence()
        {
            var length = checked((int)ReadUInt32());
            var values = new double[length];
            for (var i = 0; i < values.Length; i++)
                values[i] = ReadFloat64();
            return values;
        }

        public uint[] ReadUInt32Sequence()
        {
            var length = checked((int)ReadUInt32());
            var values = new uint[length];
            for (var i = 0; i < values.Length; i++)
                values[i] = ReadUInt32();
            return values;
        }

        public double[] ReadFloat64Fixed(int length)
        {
            var values = new double[length];
            for (var i = 0; i < values.Length; i++)
                values[i] = ReadFloat64();
            return values;
        }

        private void Align(int alignment)
        {
            var relative = (_offset - AlignmentOrigin) % alignment;
            if (relative != 0)
                _offset += alignment - relative;
        }

        private void Ensure(int count)
        {
            if (_offset + count > _data.Length)
                throw new InvalidOperationException("Attempted to read past the end of the CDR payload.");
        }
    }
}
