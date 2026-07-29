// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/MsgPack
// Purpose: Strict bounded reader for untrusted FoxRun MessagePack input.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Unity.FoxgloveSDK.Schemas.MsgPack
{
    /// <summary>
    /// Small SDK-owned MessagePack reader with sticky, bounded failures.
    /// </summary>
    public sealed class FoxgloveMsgPackReader
    {
        private const int MaxErrorLength = 160;
        private static readonly UTF8Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        private readonly byte[] _payload;
        private readonly FoxgloveMsgPackReadLimits _limits;
        private readonly int[] _remainingByDepth;
        private int _cursor;
        private int _stackCount;
        private int _containerItems;
        private bool _rootConsumed;
        private string _error = string.Empty;

        public FoxgloveMsgPackReader(
            byte[] payload,
            FoxgloveMsgPackReadLimits limits)
        {
            _payload = payload ?? Array.Empty<byte>();
            _limits = limits
                      ?? throw new ArgumentNullException(nameof(limits));
            _remainingByDepth = new int[_limits.MaxDepth];
        }

        public int Position => _cursor;
        public int RemainingBytes => _payload.Length - _cursor;
        public string Error => _error;
        public bool HasError => _error.Length != 0;

        /// <summary>
        /// Consume nil when present. A non-nil marker is left untouched and
        /// reported through <paramref name="isNil"/>.
        /// </summary>
        public bool TryReadNil(out bool isNil)
        {
            isNil = false;
            if (HasError)
                return false;
            if (_cursor >= _payload.Length)
                return Fail("MessagePack payload is truncated.");
            if (_payload[_cursor] != 0xc0)
                return true;

            if (!TryBeginValue(out _))
                return false;
            isNil = true;
            return true;
        }

        public bool TryReadBoolean(out bool value)
        {
            value = false;
            if (!TryBeginValue(out var marker))
                return false;
            if (marker == 0xc2)
                return true;
            if (marker == 0xc3)
            {
                value = true;
                return true;
            }
            return Fail("MessagePack value has the wrong boolean marker.");
        }

        public bool TryReadSByte(out sbyte value)
        {
            value = 0;
            if (!TryReadInt64(out var decoded))
                return false;
            if (decoded < sbyte.MinValue || decoded > sbyte.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (sbyte)decoded;
            return true;
        }

        public bool TryReadByte(out byte value)
        {
            value = 0;
            if (!TryReadUInt64(out var decoded))
                return false;
            if (decoded > byte.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (byte)decoded;
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            value = 0;
            if (!TryReadInt64(out var decoded))
                return false;
            if (decoded < short.MinValue || decoded > short.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (short)decoded;
            return true;
        }

        public bool TryReadUInt16(out ushort value)
        {
            value = 0;
            if (!TryReadUInt64(out var decoded))
                return false;
            if (decoded > ushort.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (ushort)decoded;
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            value = 0;
            if (!TryReadInt64(out var decoded))
                return false;
            if (decoded < int.MinValue || decoded > int.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (int)decoded;
            return true;
        }

        public bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (!TryReadUInt64(out var decoded))
                return false;
            if (decoded > uint.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (uint)decoded;
            return true;
        }

        public bool TryReadInt64(out long value)
        {
            value = 0;
            if (!TryReadInteger(
                    out var negative,
                    out var signed,
                    out var unsigned))
            {
                return false;
            }
            if (negative)
            {
                value = signed;
                return true;
            }
            if (unsigned > long.MaxValue)
                return Fail("MessagePack integer is outside the requested range.");
            value = (long)unsigned;
            return true;
        }

        public bool TryReadUInt64(out ulong value)
        {
            value = 0;
            if (!TryReadInteger(
                    out var negative,
                    out _,
                    out var unsigned))
            {
                return false;
            }
            if (negative)
                return Fail("MessagePack integer is outside the requested range.");
            value = unsigned;
            return true;
        }

        public bool TryReadSingle(out float value)
        {
            value = 0f;
            if (!TryBeginValue(out var marker))
                return false;
            if (marker != 0xca)
                return Fail("MessagePack value has the wrong float32 marker.");
            if (!TryReadUInt32Bytes(out var bits))
                return false;
            value = new FloatBits { UInt32 = bits }.Value;
            return true;
        }

        public bool TryReadDouble(out double value)
        {
            value = 0d;
            if (!TryBeginValue(out var marker))
                return false;
            if (marker != 0xcb)
                return Fail("MessagePack value has the wrong float64 marker.");
            if (!TryReadUInt64Bytes(out var bits))
                return false;
            value = new DoubleBits { UInt64 = bits }.Value;
            return true;
        }

        public bool TryReadString(out string value)
        {
            value = null;
            if (!TryBeginValue(out var marker))
                return false;
            if (!TryReadStringLength(marker, out var length))
                return false;
            if (!TryEnsureAvailable(length))
                return false;

            try
            {
                value = StrictUtf8.GetString(_payload, _cursor, length);
            }
            catch (DecoderFallbackException)
            {
                return Fail("MessagePack string contains invalid UTF-8.");
            }

            _cursor += length;
            return true;
        }

        public bool TryReadBinary(out byte[] value)
        {
            value = null;
            if (!TryBeginValue(out var marker))
                return false;
            if (!TryReadBinaryLength(marker, out var length))
                return false;
            if (!TryEnsureAvailable(length))
                return false;

            value = new byte[length];
            if (length > 0)
                Buffer.BlockCopy(_payload, _cursor, value, 0, length);
            _cursor += length;
            return true;
        }

        public bool TryReadArrayHeader(out int count)
        {
            count = 0;
            if (!TryBeginValue(out var marker))
                return false;
            if (!TryReadArrayCount(marker, out count))
                return false;
            return TryEnterContainer(count, count);
        }

        public bool TryReadMapHeader(out int count)
        {
            count = 0;
            if (!TryBeginValue(out var marker))
                return false;
            if (!TryReadMapCount(marker, out count))
                return false;
            if (count > int.MaxValue / 2)
                return Fail("MessagePack map length exceeds the supported length.");
            return TryEnterContainer(count, count * 2);
        }

        /// <summary>Skip exactly one bounded MessagePack value.</summary>
        public bool TrySkipValue()
        {
            if (!TryBeginValue(out var marker))
                return false;

            if (marker <= 0x7f || marker >= 0xe0
                || marker == 0xc0 || marker == 0xc2 || marker == 0xc3)
            {
                return true;
            }

            if ((marker & 0xe0) == 0xa0)
                return TrySkipString(marker);
            if ((marker & 0xf0) == 0x90)
                return TrySkipContainer(marker & 0x0f, isMap: false);
            if ((marker & 0xf0) == 0x80)
                return TrySkipContainer(marker & 0x0f, isMap: true);

            switch (marker)
            {
                case 0xcc:
                case 0xd0:
                    return TrySkipBytes(1, 1);
                case 0xcd:
                case 0xd1:
                    return TrySkipBytes(2, 2);
                case 0xca:
                case 0xce:
                case 0xd2:
                    return TrySkipBytes(4, 4);
                case 0xcb:
                case 0xcf:
                case 0xd3:
                    return TrySkipBytes(8, 8);
                case 0xd9:
                case 0xda:
                case 0xdb:
                    return TrySkipString(marker);
                case 0xc4:
                case 0xc5:
                case 0xc6:
                    return TrySkipBinary(marker);
                case 0xdc:
                case 0xdd:
                    return TrySkipArray(marker);
                case 0xde:
                case 0xdf:
                    return TrySkipMap(marker);
                default:
                    return Fail(
                        "MessagePack marker is reserved, unsupported, or an extension marker.");
            }
        }

        /// <summary>Require one complete value and no trailing bytes.</summary>
        public bool TryComplete()
        {
            if (HasError)
                return false;
            CollapseCompletedContainers();
            if (!_rootConsumed)
                return Fail("MessagePack payload does not contain a value.");
            if (_stackCount != 0)
                return Fail("MessagePack payload is truncated inside a container.");
            if (_cursor != _payload.Length)
                return Fail("MessagePack payload contains trailing bytes.");
            return true;
        }

        private bool TryBeginValue(out byte marker)
        {
            marker = 0;
            if (HasError)
                return false;

            CollapseCompletedContainers();
            if (_stackCount == 0)
            {
                if (_rootConsumed)
                    return Fail("MessagePack payload contains trailing bytes.");
                _rootConsumed = true;
            }
            else
            {
                _remainingByDepth[_stackCount - 1]--;
            }

            if (_cursor >= _payload.Length)
                return Fail("MessagePack payload is truncated.");
            marker = _payload[_cursor++];
            return true;
        }

        private void CollapseCompletedContainers()
        {
            while (_stackCount > 0
                   && _remainingByDepth[_stackCount - 1] == 0)
            {
                _stackCount--;
            }
        }

        private bool TryEnterContainer(int itemBudget, int childValues)
        {
            if (itemBudget < 0
                || _containerItems > _limits.MaxContainerItems - itemBudget)
            {
                return Fail("MessagePack aggregate container budget was exceeded.");
            }
            _containerItems += itemBudget;

            var depth = _stackCount + 1;
            if (depth > _limits.MaxDepth)
                return Fail("MessagePack container depth was exceeded.");
            if (childValues == 0)
                return true;
            _remainingByDepth[_stackCount++] = childValues;
            return true;
        }

        private bool TryReadInteger(
            out bool negative,
            out long signed,
            out ulong unsigned)
        {
            negative = false;
            signed = 0;
            unsigned = 0;
            if (!TryBeginValue(out var marker))
                return false;

            if (marker <= 0x7f)
            {
                unsigned = marker;
                return true;
            }
            if (marker >= 0xe0)
            {
                negative = true;
                signed = unchecked((sbyte)marker);
                return true;
            }

            switch (marker)
            {
                case 0xcc:
                    if (!TryReadByteRaw(out var uint8))
                        return false;
                    unsigned = uint8;
                    return true;
                case 0xcd:
                    if (!TryReadUInt16Bytes(out var uint16))
                        return false;
                    unsigned = uint16;
                    return true;
                case 0xce:
                    if (!TryReadUInt32Bytes(out var uint32))
                        return false;
                    unsigned = uint32;
                    return true;
                case 0xcf:
                    return TryReadUInt64Bytes(out unsigned);
                case 0xd0:
                    if (!TryReadByteRaw(out var int8))
                        return false;
                    negative = unchecked((sbyte)int8) < 0;
                    signed = unchecked((sbyte)int8);
                    unsigned = unchecked((byte)(sbyte)int8);
                    return true;
                case 0xd1:
                    if (!TryReadUInt16Bytes(out var int16))
                        return false;
                    signed = unchecked((short)int16);
                    negative = signed < 0;
                    unsigned = int16;
                    return true;
                case 0xd2:
                    if (!TryReadUInt32Bytes(out var int32))
                        return false;
                    signed = unchecked((int)int32);
                    negative = signed < 0;
                    unsigned = int32;
                    return true;
                case 0xd3:
                    if (!TryReadUInt64Bytes(out var int64))
                        return false;
                    signed = unchecked((long)int64);
                    negative = signed < 0;
                    unsigned = int64;
                    return true;
                default:
                    return Fail("MessagePack value has the wrong integer marker.");
            }
        }

        private bool TryReadStringLength(byte marker, out int length)
        {
            length = 0;
            if ((marker & 0xe0) == 0xa0)
                length = marker & 0x1f;
            else if (marker == 0xd9)
            {
                if (!TryReadByteRaw(out var value))
                    return false;
                length = (int)value;
            }
            else if (marker == 0xda)
            {
                if (!TryReadUInt16Bytes(out var value))
                    return false;
                length = (int)value;
            }
            else if (marker == 0xdb)
            {
                if (!TryReadUInt32Bytes(out var value)
                    || !TryBoundLength(
                        value,
                        _limits.MaxStringBytes,
                        "MessagePack string length exceeds the configured length.",
                        out length))
                {
                    return false;
                }
                return true;
            }
            else
            {
                return Fail("MessagePack value has the wrong string marker.");
            }

            return TryBoundLength(
                (uint)length,
                _limits.MaxStringBytes,
                "MessagePack string length exceeds the configured length.",
                out length);
        }

        private bool TryReadBinaryLength(byte marker, out int length)
        {
            length = 0;
            uint decoded;
            switch (marker)
            {
                case 0xc4:
                    if (!TryReadByteRaw(out var byteLength))
                        return false;
                    decoded = (uint)byteLength;
                    break;
                case 0xc5:
                    if (!TryReadUInt16Bytes(out var shortLength))
                        return false;
                    decoded = (uint)shortLength;
                    break;
                case 0xc6:
                    if (!TryReadUInt32Bytes(out decoded))
                        return false;
                    break;
                default:
                    return Fail("MessagePack value has the wrong binary marker.");
            }

            return TryBoundLength(
                decoded,
                _limits.MaxBinaryBytes,
                "MessagePack binary length exceeds the configured length.",
                out length);
        }

        private bool TryReadArrayCount(byte marker, out int count)
        {
            count = 0;
            uint decoded;
            if ((marker & 0xf0) == 0x90)
                decoded = (uint)(marker & 0x0f);
            else if (marker == 0xdc)
            {
                if (!TryReadUInt16Bytes(out var value))
                    return false;
                decoded = (uint)value;
            }
            else if (marker == 0xdd)
            {
                if (!TryReadUInt32Bytes(out decoded))
                    return false;
            }
            else
            {
                return Fail("MessagePack value has the wrong array marker.");
            }

            if (decoded > int.MaxValue)
                return Fail("MessagePack array length exceeds the supported length.");
            count = (int)decoded;
            return true;
        }

        private bool TryReadMapCount(byte marker, out int count)
        {
            count = 0;
            uint decoded;
            if ((marker & 0xf0) == 0x80)
                decoded = (uint)(marker & 0x0f);
            else if (marker == 0xde)
            {
                if (!TryReadUInt16Bytes(out var value))
                    return false;
                decoded = (uint)value;
            }
            else if (marker == 0xdf)
            {
                if (!TryReadUInt32Bytes(out decoded))
                    return false;
            }
            else
            {
                return Fail("MessagePack value has the wrong map marker.");
            }

            if (decoded > int.MaxValue)
                return Fail("MessagePack map length exceeds the supported length.");
            count = (int)decoded;
            return true;
        }

        private bool TrySkipString(byte marker)
        {
            if (!TryReadStringLength(marker, out var length))
                return false;
            if (!TryEnsureAvailable(length))
                return false;
            try
            {
                StrictUtf8.GetCharCount(_payload, _cursor, length);
            }
            catch (DecoderFallbackException)
            {
                return Fail("MessagePack string contains invalid UTF-8.");
            }
            _cursor += length;
            return true;
        }

        private bool TrySkipBinary(byte marker)
        {
            if (!TryReadBinaryLength(marker, out var length))
                return false;
            return TrySkipBytes(length, _limits.MaxBinaryBytes);
        }

        private bool TrySkipArray(byte marker)
        {
            if (!TryReadArrayCount(marker, out var count))
                return false;
            return TrySkipContainer(count, isMap: false);
        }

        private bool TrySkipMap(byte marker)
        {
            if (!TryReadMapCount(marker, out var count))
                return false;
            return TrySkipContainer(count, isMap: true);
        }

        private bool TrySkipContainer(int count, bool isMap)
        {
            if (isMap && count > int.MaxValue / 2)
                return Fail("MessagePack map length exceeds the supported length.");
            var childValues = isMap ? count * 2 : count;
            if (!TryEnterContainer(count, childValues))
                return false;
            for (var index = 0; index < childValues; index++)
            {
                if (!TrySkipValue())
                    return false;
            }
            return true;
        }

        private bool TrySkipBytes(int length, int maximum)
        {
            if (length < 0 || length > maximum)
                return Fail("MessagePack value length exceeds the configured length.");
            if (!TryEnsureAvailable(length))
                return false;
            _cursor += length;
            return true;
        }

        private bool TryBoundLength(
            uint decoded,
            int maximum,
            string error,
            out int length)
        {
            length = 0;
            if (decoded > int.MaxValue || decoded > (uint)maximum)
                return Fail(error);
            length = (int)decoded;
            return true;
        }

        private bool TryEnsureAvailable(int count)
        {
            if (count < 0 || count > _payload.Length - _cursor)
                return Fail("MessagePack payload is truncated.");
            return true;
        }

        private bool TryReadByteRaw(out byte value)
        {
            value = 0;
            if (!TryEnsureAvailable(1))
                return false;
            value = _payload[_cursor++];
            return true;
        }

        private bool TryReadUInt16Bytes(out ushort value)
        {
            value = 0;
            if (!TryEnsureAvailable(2))
                return false;
            value = (ushort)((_payload[_cursor] << 8)
                    | _payload[_cursor + 1]);
            _cursor += 2;
            return true;
        }

        private bool TryReadUInt32Bytes(out uint value)
        {
            value = 0;
            if (!TryEnsureAvailable(4))
                return false;
            value = ((uint)_payload[_cursor] << 24)
                    | ((uint)_payload[_cursor + 1] << 16)
                    | ((uint)_payload[_cursor + 2] << 8)
                    | (uint)_payload[_cursor + 3];
            _cursor += 4;
            return true;
        }

        private bool TryReadUInt64Bytes(out ulong value)
        {
            value = 0;
            if (!TryEnsureAvailable(8))
                return false;
            value = ((ulong)_payload[_cursor] << 56)
                    | ((ulong)_payload[_cursor + 1] << 48)
                    | ((ulong)_payload[_cursor + 2] << 40)
                    | ((ulong)_payload[_cursor + 3] << 32)
                    | ((ulong)_payload[_cursor + 4] << 24)
                    | ((ulong)_payload[_cursor + 5] << 16)
                    | ((ulong)_payload[_cursor + 6] << 8)
                    | _payload[_cursor + 7];
            _cursor += 8;
            return true;
        }

        private bool Fail(string message)
        {
            if (HasError)
                return false;
            message ??= "MessagePack input failed.";
            _error = message.Length <= MaxErrorLength
                ? message
                : message.Substring(0, MaxErrorLength);
            return false;
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
