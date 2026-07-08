// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.RemoteGateway.Native;

namespace Unity.FoxgloveSDK.RemoteGateway
{
    internal sealed class RemoteGatewayChannelRegistry : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<uint, ChannelHandle> _channels = new Dictionary<uint, ChannelHandle>();
        private readonly IntPtr _context;
        private readonly Func<ulong> _gatewaySinkId;
        private readonly ulong[] _logTimeScratch = new ulong[1];
        private bool _disposed;

        internal RemoteGatewayChannelRegistry(IntPtr context, Func<ulong> gatewaySinkId)
        {
            _context = context;
            _gatewaySinkId = gatewaySinkId ?? throw new ArgumentNullException(nameof(gatewaySinkId));
        }

        internal ulong GatewaySinkId => _gatewaySinkId();

        internal bool RegisterChannel(AdvertiseChannel channel)
        {
            if (channel == null)
                return false;

            lock (_gate)
            {
                if (_disposed)
                    return false;

                UnregisterChannelLocked(channel.Id);

                var created = TryCreateNativeChannel(channel, out var nativeChannel);
                if (!created)
                    return false;

                _channels[channel.Id] = new ChannelHandle(nativeChannel);
                return true;
            }
        }

        internal void UnregisterChannel(uint channelId)
        {
            lock (_gate)
                UnregisterChannelLocked(channelId);
        }

        internal bool Publish(uint channelId, ulong logTimeNs, byte[] payload)
        {
            if (payload == null || payload.Length == 0)
                return false;

            lock (_gate)
            {
                if (_disposed || !_channels.TryGetValue(channelId, out var channel))
                    return false;

                var sinkId = GatewaySinkId;
                if (sinkId == 0UL)
                    return false;

                // Keep the native channel handle live until ChannelLog has returned.
                _logTimeScratch[0] = logTimeNs;
                var payloadHandle = GCHandle.Alloc(payload, GCHandleType.Pinned);
                var logTimeHandle = GCHandle.Alloc(_logTimeScratch, GCHandleType.Pinned);
                try
                {
                    var error = RemoteGatewayNativeMethods.ChannelLog(
                        channel.Pointer,
                        payloadHandle.AddrOfPinnedObject(),
                        (UIntPtr)payload.Length,
                        logTimeHandle.AddrOfPinnedObject(),
                        sinkId);
                    return error == RemoteGatewayNativeMethods.FoxgloveError.Ok;
                }
                finally
                {
                    logTimeHandle.Free();
                    payloadHandle.Free();
                    _logTimeScratch[0] = 0UL;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                foreach (var channel in _channels.Values)
                    channel.Dispose();
                _channels.Clear();
            }
        }

        private bool TryCreateNativeChannel(AdvertiseChannel channel, out IntPtr nativeChannel)
        {
            nativeChannel = IntPtr.Zero;
            using (var topic = PinnedUtf8String.Create(channel.Topic))
            using (var encoding = PinnedUtf8String.Create(channel.Encoding))
            using (var schema = NativeSchema.TryCreate(channel))
            {
                if (schema == null)
                    return false;

                var error = RemoteGatewayNativeMethods.RawChannelCreate(
                    topic.Value,
                    encoding.Value,
                    schema.Pointer,
                    _context,
                    IntPtr.Zero,
                    out nativeChannel);
                return error == RemoteGatewayNativeMethods.FoxgloveError.Ok && nativeChannel != IntPtr.Zero;
            }
        }

        private void UnregisterChannelLocked(uint channelId)
        {
            if (!_channels.TryGetValue(channelId, out var channel))
                return;

            _channels.Remove(channelId);
            channel.Dispose();
        }

        private sealed class ChannelHandle : IDisposable
        {
            private IntPtr _pointer;

            internal ChannelHandle(IntPtr pointer)
            {
                _pointer = pointer;
            }

            internal IntPtr Pointer => _pointer;

            public void Dispose()
            {
                var pointer = _pointer;
                if (pointer == IntPtr.Zero)
                    return;

                _pointer = IntPtr.Zero;
                RemoteGatewayNativeMethods.ChannelClose(pointer);
                RemoteGatewayNativeMethods.ChannelFree(pointer);
            }
        }

        private sealed class NativeSchema : IDisposable
        {
            private readonly PinnedUtf8String _name;
            private readonly PinnedUtf8String _encoding;
            private readonly PinnedBytes _data;
            private IntPtr _pointer;

            private NativeSchema(PinnedUtf8String name, PinnedUtf8String encoding, PinnedBytes data, IntPtr pointer)
            {
                _name = name;
                _encoding = encoding;
                _data = data;
                _pointer = pointer;
            }

            internal IntPtr Pointer => _pointer;

            internal static NativeSchema TryCreate(AdvertiseChannel channel)
            {
                if (string.IsNullOrEmpty(channel.SchemaName)
                    && string.IsNullOrEmpty(channel.SchemaEncoding)
                    && string.IsNullOrEmpty(channel.Schema))
                {
                    return Empty();
                }

                var schemaBytes = TryBuildSchemaBytes(channel.SchemaEncoding, channel.Schema);
                if (schemaBytes == null)
                    return null;

                var name = PinnedUtf8String.Create(channel.SchemaName);
                var encoding = PinnedUtf8String.Create(channel.SchemaEncoding);
                var data = PinnedBytes.Create(schemaBytes);
                var native = new RemoteGatewayNativeMethods.FoxgloveSchema
                {
                    Name = name.Value,
                    Encoding = encoding.Value,
                    Data = data.Pointer,
                    DataLength = (UIntPtr)data.Length
                };
                var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(RemoteGatewayNativeMethods.FoxgloveSchema)));
                Marshal.StructureToPtr(native, pointer, false);
                return new NativeSchema(name, encoding, data, pointer);
            }

            public void Dispose()
            {
                if (_pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pointer);
                    _pointer = IntPtr.Zero;
                }

                _data?.Dispose();
                _encoding?.Dispose();
                _name?.Dispose();
            }

            private static NativeSchema Empty()
                => new NativeSchema(null, null, null, IntPtr.Zero);

            private static byte[] TryBuildSchemaBytes(string schemaEncoding, string schema)
            {
                if (string.IsNullOrEmpty(schema))
                    return Array.Empty<byte>();

                if (string.Equals(schemaEncoding, "protobuf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return Convert.FromBase64String(schema);
                    }
                    catch (FormatException)
                    {
                        return null;
                    }
                }

                return Encoding.UTF8.GetBytes(schema);
            }
        }

        private sealed class PinnedUtf8String : IDisposable
        {
            private readonly GCHandle _handle;

            private PinnedUtf8String(byte[] bytes)
            {
                Bytes = bytes ?? Array.Empty<byte>();
                if (Bytes.Length > 0)
                    _handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            }

            private byte[] Bytes { get; }

            internal RemoteGatewayNativeMethods.FoxgloveString Value
                => new RemoteGatewayNativeMethods.FoxgloveString
                {
                    Data = Bytes.Length == 0 ? IntPtr.Zero : _handle.AddrOfPinnedObject(),
                    Length = (UIntPtr)Bytes.Length
                };

            internal static PinnedUtf8String Create(string value)
                => new PinnedUtf8String(string.IsNullOrEmpty(value)
                    ? Array.Empty<byte>()
                    : Encoding.UTF8.GetBytes(value));

            public void Dispose()
            {
                if (_handle.IsAllocated)
                    _handle.Free();
            }
        }

        private sealed class PinnedBytes : IDisposable
        {
            private readonly GCHandle _handle;

            private PinnedBytes(byte[] bytes)
            {
                Bytes = bytes ?? Array.Empty<byte>();
                if (Bytes.Length > 0)
                    _handle = GCHandle.Alloc(Bytes, GCHandleType.Pinned);
            }

            private byte[] Bytes { get; }
            internal int Length => Bytes.Length;
            internal IntPtr Pointer => Bytes.Length == 0 ? IntPtr.Zero : _handle.AddrOfPinnedObject();

            internal static PinnedBytes Create(byte[] value)
                => new PinnedBytes(value);

            public void Dispose()
            {
                if (_handle.IsAllocated)
                    _handle.Free();
            }
        }
    }
}
