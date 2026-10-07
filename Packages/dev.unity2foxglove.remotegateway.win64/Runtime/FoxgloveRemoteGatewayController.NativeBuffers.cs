// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using System.Text;
using Unity.FoxgloveSDK.RemoteGateway.Native;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    public sealed partial class FoxgloveRemoteGatewayController
    {
        private sealed class NativeStructPointer : IDisposable
        {
            private NativeStructPointer(IntPtr pointer)
            {
                Pointer = pointer;
            }

            internal IntPtr Pointer { get; private set; }

            internal static NativeStructPointer Create<T>(T value)
            {
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));
                Marshal.StructureToPtr(value, pointer, false);
                return new NativeStructPointer(pointer);
            }

            public void Dispose()
            {
                if (Pointer == IntPtr.Zero)
                    return;

                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        private sealed class PinnedUtf8String : IDisposable
        {
            private readonly byte[] _bytes;
            private readonly GCHandle _handle;

            private PinnedUtf8String(string value)
            {
                _bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(value);
                if (_bytes.Length > 0)
                    _handle = GCHandle.Alloc(_bytes, GCHandleType.Pinned);
            }

            internal RemoteGatewayNativeMethods.FoxgloveString Value
                => new RemoteGatewayNativeMethods.FoxgloveString
                {
                    Data = _bytes.Length == 0 ? IntPtr.Zero : _handle.AddrOfPinnedObject(),
                    Length = (UIntPtr)_bytes.Length
                };

            internal static PinnedUtf8String Create(string value)
                => new PinnedUtf8String(value);

            public void Dispose()
            {
                if (_handle.IsAllocated)
                    _handle.Free();
            }
        }

        private sealed class PinnedStringArray : IDisposable
        {
            private readonly PinnedUtf8String[] _strings;

            private PinnedStringArray(PinnedUtf8String[] strings, IntPtr pointer)
            {
                _strings = strings;
                Pointer = pointer;
            }

            internal IntPtr Pointer { get; private set; }
            internal int Count => _strings.Length;

            internal static PinnedStringArray Create(params string[] values)
            {
                var strings = new PinnedUtf8String[values.Length];
                var itemSize = Marshal.SizeOf(typeof(RemoteGatewayNativeMethods.FoxgloveString));
                var pointer = Marshal.AllocHGlobal(itemSize * values.Length);
                for (var i = 0; i < values.Length; i++)
                {
                    strings[i] = PinnedUtf8String.Create(values[i]);
                    Marshal.StructureToPtr(strings[i].Value, IntPtr.Add(pointer, i * itemSize), false);
                }

                return new PinnedStringArray(strings, pointer);
            }

            public void Dispose()
            {
                if (Pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(Pointer);
                    Pointer = IntPtr.Zero;
                }

                foreach (var item in _strings)
                    item?.Dispose();
            }
        }
    }
}
