// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MsgPack
// Purpose: Small SDK-owned MessagePack writer for custom Foxglove raw channels.

using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Unity.FoxgloveSDK.Schemas.MsgPack
{
    /// <summary>
    /// Minimal MessagePack writer for Foxglove custom raw channels.
    /// </summary>
    public sealed class FoxgloveMsgPackWriter : IDisposable
    {
        private readonly MemoryStream _stream;

        public FoxgloveMsgPackWriter()
            : this(256)
        {
        }

        public FoxgloveMsgPackWriter(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _stream = new MemoryStream(capacity);
        }

        public int Length => checked((int)_stream.Length);

        public void Clear()
        {
            _stream.SetLength(0);
        }

        public byte[] ToArray()
        {
            return _stream.ToArray();
        }

        /// <summary>
        /// Return the writer-owned backing buffer and the valid byte length.
        /// The returned array is invalidated by further writes, Clear(), or Dispose().
        /// </summary>
        public byte[] GetBuffer(out int length)
        {
            length = checked((int)_stream.Length);
            return _stream.GetBuffer();
        }

        public void Dispose()
        {
            _stream.Dispose();
        }

        public void WriteNil()
        {
            WriteByte(0xc0);
        }

        public void WriteBool(bool value)
        {
            WriteByte(value ? (byte)0xc3 : (byte)0xc2);
        }

        public void WriteInt32(int value)
        {
            WriteInt64(value);
        }

        public void WriteInt64(long value)
        {
            if (value >= 0)
            {
                WriteUInt64((ulong)value);
                return;
            }

            if (value >= -32)
            {
                WriteByte((byte)(0xe0 | (value + 32)));
            }
            else if (value >= sbyte.MinValue)
            {
                WriteByte(0xd0);
                WriteByte(unchecked((byte)(sbyte)value));
            }
            else if (value >= short.MinValue)
            {
                WriteByte(0xd1);
                WriteBigEndianInt16(unchecked((short)value));
            }
            else if (value >= int.MinValue)
            {
                WriteByte(0xd2);
                WriteBigEndianInt32(unchecked((int)value));
            }
            else
            {
                WriteByte(0xd3);
                WriteBigEndianInt64(value);
            }
        }

        public void WriteUInt32(uint value)
        {
            WriteUInt64(value);
        }

        public void WriteUInt64(ulong value)
        {
            if (value <= 0x7f)
            {
                WriteByte((byte)value);
            }
            else if (value <= byte.MaxValue)
            {
                WriteByte(0xcc);
                WriteByte((byte)value);
            }
            else if (value <= ushort.MaxValue)
            {
                WriteByte(0xcd);
                WriteBigEndianUInt16((ushort)value);
            }
            else if (value <= uint.MaxValue)
            {
                WriteByte(0xce);
                WriteBigEndianUInt32((uint)value);
            }
            else
            {
                WriteByte(0xcf);
                WriteBigEndianUInt64(value);
            }
        }

        public void WriteFloat(float value)
        {
            WriteByte(0xca);
            WriteBigEndianUInt32(FloatToUInt32Bits(value));
        }

        public void WriteDouble(double value)
        {
            WriteByte(0xcb);
            WriteBigEndianUInt64(DoubleToUInt64Bits(value));
        }

        public void WriteString(string value)
        {
            if (value == null)
            {
                WriteNil();
                return;
            }

            var byteCount = Encoding.UTF8.GetByteCount(value);
            WriteStringHeader(byteCount);
            if (byteCount == 0)
                return;

            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
                _stream.Write(buffer, 0, written);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void WriteBinary(byte[] value)
        {
            if (value == null)
            {
                WriteNil();
                return;
            }

            WriteBinaryHeader(value.Length);
            _stream.Write(value, 0, value.Length);
        }

        public void WriteArrayHeader(int count)
        {
            ValidateContainerCount(count, nameof(count));
            if (count <= 15)
            {
                WriteByte((byte)(0x90 | count));
            }
            else if (count <= ushort.MaxValue)
            {
                WriteByte(0xdc);
                WriteBigEndianUInt16((ushort)count);
            }
            else
            {
                WriteByte(0xdd);
                WriteBigEndianUInt32((uint)count);
            }
        }

        public void WriteMapHeader(int count)
        {
            ValidateContainerCount(count, nameof(count));
            if (count <= 15)
            {
                WriteByte((byte)(0x80 | count));
            }
            else if (count <= ushort.MaxValue)
            {
                WriteByte(0xde);
                WriteBigEndianUInt16((ushort)count);
            }
            else
            {
                WriteByte(0xdf);
                WriteBigEndianUInt32((uint)count);
            }
        }

        private void WriteStringHeader(int length)
        {
            if (length <= 31)
            {
                WriteByte((byte)(0xa0 | length));
            }
            else if (length <= byte.MaxValue)
            {
                WriteByte(0xd9);
                WriteByte((byte)length);
            }
            else if (length <= ushort.MaxValue)
            {
                WriteByte(0xda);
                WriteBigEndianUInt16((ushort)length);
            }
            else
            {
                WriteByte(0xdb);
                WriteBigEndianUInt32((uint)length);
            }
        }

        private void WriteBinaryHeader(int length)
        {
            if (length <= byte.MaxValue)
            {
                WriteByte(0xc4);
                WriteByte((byte)length);
            }
            else if (length <= ushort.MaxValue)
            {
                WriteByte(0xc5);
                WriteBigEndianUInt16((ushort)length);
            }
            else
            {
                WriteByte(0xc6);
                WriteBigEndianUInt32((uint)length);
            }
        }

        private static void ValidateContainerCount(int count, string paramName)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(paramName);
        }

        private void WriteBigEndianInt16(short value)
        {
            WriteBigEndianUInt16(unchecked((ushort)value));
        }

        private void WriteBigEndianUInt16(ushort value)
        {
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        private void WriteBigEndianInt32(int value)
        {
            WriteBigEndianUInt32(unchecked((uint)value));
        }

        private void WriteBigEndianUInt32(uint value)
        {
            WriteByte((byte)(value >> 24));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        private void WriteBigEndianInt64(long value)
        {
            WriteBigEndianUInt64(unchecked((ulong)value));
        }

        private void WriteBigEndianUInt64(ulong value)
        {
            WriteByte((byte)(value >> 56));
            WriteByte((byte)(value >> 48));
            WriteByte((byte)(value >> 40));
            WriteByte((byte)(value >> 32));
            WriteByte((byte)(value >> 24));
            WriteByte((byte)(value >> 16));
            WriteByte((byte)(value >> 8));
            WriteByte((byte)value);
        }

        private void WriteByte(byte value)
        {
            _stream.WriteByte(value);
        }

        private static uint FloatToUInt32Bits(float value)
        {
            var bits = new FloatBits { Value = value };
            return bits.UInt32;
        }

        private static ulong DoubleToUInt64Bits(double value)
        {
            var bits = new DoubleBits { Value = value };
            return bits.UInt64;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Value;
            [FieldOffset(0)] public uint UInt32;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleBits
        {
            [FieldOffset(0)] public double Value;
            [FieldOffset(0)] public ulong UInt64;
        }
    }
}
