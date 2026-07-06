// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;

namespace Unity.FoxgloveSDK.RemoteGateway.Native
{
    internal static class RemoteGatewayNativeMethods
    {
        private const string LibraryName = "foxglove";

        [DllImport(LibraryName, EntryPoint = "foxglove_gateway_start", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FoxgloveError GatewayStart(ref FoxgloveGatewayOptions options, out IntPtr gateway);

        [DllImport(LibraryName, EntryPoint = "foxglove_gateway_stop", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FoxgloveError GatewayStop(IntPtr gateway);

        [DllImport(LibraryName, EntryPoint = "foxglove_gateway_connection_status", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FoxgloveConnectionStatus GatewayConnectionStatus(IntPtr gateway);

        [DllImport(LibraryName, EntryPoint = "foxglove_gateway_sink_id", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong GatewaySinkId(IntPtr gateway);

        [DllImport(LibraryName, EntryPoint = "foxglove_context_new", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr ContextNew();

        [DllImport(LibraryName, EntryPoint = "foxglove_context_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ContextFree(IntPtr context);

        [DllImport(LibraryName, EntryPoint = "foxglove_raw_channel_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FoxgloveError RawChannelCreate(
            FoxgloveString topic,
            FoxgloveString messageEncoding,
            IntPtr schema,
            IntPtr context,
            IntPtr metadata,
            out IntPtr channel);

        [DllImport(LibraryName, EntryPoint = "foxglove_channel_log", CallingConvention = CallingConvention.Cdecl)]
        internal static extern FoxgloveError ChannelLog(
            IntPtr channel,
            IntPtr data,
            UIntPtr dataLength,
            IntPtr logTime,
            ulong sinkId);

        [DllImport(LibraryName, EntryPoint = "foxglove_channel_close", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ChannelClose(IntPtr channel);

        [DllImport(LibraryName, EntryPoint = "foxglove_channel_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void ChannelFree(IntPtr channel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ConnectionStatusChangedCallback(
            IntPtr context,
            FoxgloveConnectionStatus status);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ChannelCallback(
            IntPtr context,
            uint clientId,
            IntPtr channel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void MessageDataCallback(
            IntPtr context,
            uint clientId,
            IntPtr channel,
            IntPtr payload,
            UIntPtr payloadLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr GetParametersCallback(
            IntPtr context,
            uint clientId,
            IntPtr requestId,
            IntPtr parameterNames,
            UIntPtr parameterNameCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr SetParametersCallback(
            IntPtr context,
            uint clientId,
            IntPtr requestId,
            IntPtr parameters);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ParametersCallback(
            IntPtr context,
            IntPtr parameterNames,
            UIntPtr parameterNameCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ConnectionGraphCallback(IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal delegate bool SinkChannelFilterCallback(IntPtr context, IntPtr channel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate FoxgloveQosProfile QosClassifierCallback(IntPtr context, IntPtr channel);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void FetchAssetCallback(IntPtr context, IntPtr uri, IntPtr responder);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveGatewayOptions
        {
            public IntPtr Context;
            public FoxgloveString Name;
            public FoxgloveString DeviceToken;
            public IntPtr Callbacks;
            public FoxgloveGatewayCapability Capabilities;
            public IntPtr SupportedEncodings;
            public UIntPtr SupportedEncodingsCount;
            public IntPtr ServerInfo;
            public UIntPtr ServerInfoCount;
            public IntPtr SinkChannelFilterContext;
            public SinkChannelFilterCallback SinkChannelFilter;
            public IntPtr QosClassifierContext;
            public QosClassifierCallback QosClassifier;
            public IntPtr FetchAssetContext;
            public FetchAssetCallback FetchAsset;
            public FoxgloveString FoxgloveApiUrl;
            public IntPtr FoxgloveApiTimeoutSecs;
            public IntPtr MessageBacklogSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveGatewayCallbacks
        {
            public IntPtr Context;
            public ConnectionStatusChangedCallback OnConnectionStatusChanged;
            public ChannelCallback OnSubscribe;
            public ChannelCallback OnUnsubscribe;
            public MessageDataCallback OnMessageData;
            public ChannelCallback OnClientAdvertise;
            public ChannelCallback OnClientUnadvertise;
            public GetParametersCallback OnGetParameters;
            public SetParametersCallback OnSetParameters;
            public ParametersCallback OnParametersSubscribe;
            public ParametersCallback OnParametersUnsubscribe;
            public ConnectionGraphCallback OnConnectionGraphSubscribe;
            public ConnectionGraphCallback OnConnectionGraphUnsubscribe;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveString
        {
            public IntPtr Data;
            public UIntPtr Length;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveKeyValue
        {
            public FoxgloveString Key;
            public FoxgloveString Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveChannelMetadata
        {
            public IntPtr Items;
            public UIntPtr Count;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveSchema
        {
            public FoxgloveString Name;
            public FoxgloveString Encoding;
            public IntPtr Data;
            public UIntPtr DataLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveQosProfile
        {
            public FoxgloveString Reliability;
            public FoxgloveString Durability;
            public FoxgloveString Profile;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FoxgloveGatewayCapability
        {
            public byte Flags;
        }

        internal enum FoxgloveConnectionStatus : byte
        {
            Connecting = 0,
            Connected = 1,
            ShuttingDown = 2,
            Shutdown = 3
        }

        internal enum FoxgloveError : byte
        {
            Ok = 0,
            Unspecified = 1,
            ValueError = 2,
            Utf8Error = 3,
            SinkClosed = 4,
            SchemaRequired = 5,
            MessageEncodingRequired = 6,
            ServerAlreadyStarted = 7,
            Bind = 8,
            DuplicateService = 9,
            MissingRequestEncoding = 10,
            ServicesNotSupported = 11,
            ConnectionGraphNotSupported = 12,
            IoError = 13,
            McapError = 14,
            EncodeError = 15,
            BufferTooShort = 16,
            Base64DecodeError = 17,
            ConfigurationError = 18
        }
    }
}
