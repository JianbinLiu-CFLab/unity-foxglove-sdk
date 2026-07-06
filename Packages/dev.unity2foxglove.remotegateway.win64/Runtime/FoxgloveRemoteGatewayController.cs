// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.RemoteGateway.Native;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.FoxgloveSDK.RemoteGateway
{
    [DisallowMultipleComponent]
    public sealed class FoxgloveRemoteGatewayController : MonoBehaviour
    {
        private const string DeviceTokenEnvironmentVariable = "FOXGLOVE_DEVICE_TOKEN";
        private const string EditorUserSettingsTokenKey = "Unity2Foxglove.RemoteGateway.DeviceToken";
        private const int DefaultEventQueueCapacity = 1024;

        [Header("Remote Gateway")]
        [Tooltip("Default off. When enabled in Play Mode, publishes Unity scene data to Foxglove Cloud through the official Foxglove Remote Access Gateway.")]
        [SerializeField] private bool _enableRemoteGateway;
        [SerializeField] private FoxgloveManager _manager;
        [SerializeField] private string _deviceName = "Unity2Foxglove";
        [SerializeField, Min(1)] private int _eventQueueCapacity = DefaultEventQueueCapacity;

        [Header("Token Fallback")]
        [Tooltip("Prefer FOXGLOVE_DEVICE_TOKEN or EditorUserSettings. A token in a scene can end up in git.")]
        [SerializeField] private string _deviceToken;

        private readonly List<RemoteGatewayEvent> _drainScratch = new List<RemoteGatewayEvent>();
        private RemoteGatewayEventQueue _events;
        private RemoteGatewayCallbacks _callbacks;
        private RemoteGatewayHandle _handle;
        private RemoteGatewayMirrorSink _mirrorSink;
        private IntPtr _context;
        private string _connectionStatus = "Shutdown";
        private bool _warnedMissingToken;
        private bool _warnedSerializedToken;
        private bool _starting;

        public bool EnableRemoteGateway
        {
            get => _enableRemoteGateway;
            set => _enableRemoteGateway = value;
        }

        public string ConnectionStatus => _connectionStatus;
        public long MirroredMessageCount => _mirrorSink?.MirroredMessageCount ?? 0L;
        public long DroppedMessageCount => _mirrorSink?.DroppedMessageCount ?? 0L;

        private void Reset()
        {
            _manager = GetComponent<FoxgloveManager>();
        }

        private void OnEnable()
        {
            EnsureManager();
        }

        private void Update()
        {
            DrainGatewayEvents();

            if (_enableRemoteGateway)
                TryStartGateway();
            else
                StopGateway();
        }

        private void OnDisable()
        {
            StopGateway();
        }

        private void OnDestroy()
        {
            StopGateway();
        }

        private void OnApplicationQuit()
        {
            StopGateway();
        }

        private void TryStartGateway()
        {
            if (_handle != null || _starting)
                return;
            if (!RemoteGatewayLifecycleGate.CanStartNativeGateway())
                return;
            if (!EnsureManager() || !_manager.IsRunning)
                return;

            var token = ResolveDeviceToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                if (!_warnedMissingToken)
                {
                    _warnedMissingToken = true;
                    Debug.LogWarning("[Foxglove] Remote gateway is enabled but no device token was found.");
                }

                return;
            }

            _starting = true;
            try
            {
                StartGateway(token);
            }
            finally
            {
                _starting = false;
            }
        }

        private void StartGateway(string deviceToken)
        {
            _events = new RemoteGatewayEventQueue(Math.Max(1, _eventQueueCapacity));
            _callbacks = new RemoteGatewayCallbacks(_events);
            _context = RemoteGatewayNativeMethods.ContextNew();
            var callbacks = _callbacks.CreateNative();

            using (var name = PinnedUtf8String.Create(string.IsNullOrWhiteSpace(_deviceName) ? "Unity2Foxglove" : _deviceName.Trim()))
            using (var token = PinnedUtf8String.Create(deviceToken))
            using (var apiUrl = PinnedUtf8String.Create(string.Empty))
            using (var encodings = PinnedStringArray.Create("json", "protobuf", "cdr"))
            using (var callbackPtr = NativeStructPointer.Create(callbacks))
            {
                var options = new RemoteGatewayNativeMethods.FoxgloveGatewayOptions
                {
                    Context = _context,
                    Name = name.Value,
                    DeviceToken = token.Value,
                    Callbacks = callbackPtr.Pointer,
                    Capabilities = RemoteGatewayCapabilityPolicy.CreateOutboundOnlyCapabilities(),
                    SupportedEncodings = encodings.Pointer,
                    SupportedEncodingsCount = (UIntPtr)encodings.Count,
                    FoxgloveApiUrl = apiUrl.Value
                };

                var error = RemoteGatewayNativeMethods.GatewayStart(ref options, out var nativeGateway);
                if (error != RemoteGatewayNativeMethods.FoxgloveError.Ok || nativeGateway == IntPtr.Zero)
                {
                    CleanupFailedStart();
                    Debug.LogWarning("[Foxglove] Remote gateway failed to start: " + error);
                    return;
                }

                _handle = new RemoteGatewayHandle(nativeGateway);
                var registry = new RemoteGatewayChannelRegistry(_context, () => _handle?.SinkId ?? 0UL);
                _mirrorSink = new RemoteGatewayMirrorSink(registry);
                _mirrorSink.Enable();
                _manager.SetMirrorSink(_mirrorSink);
                _connectionStatus = _handle.ConnectionStatus.ToString();
                Debug.Log("[Foxglove] Remote gateway started. Publishing to Foxglove Cloud.");
            }
        }

        private void StopGateway()
        {
            if (_handle == null && _mirrorSink == null && _context == IntPtr.Zero && _callbacks == null && _events == null)
                return;
            if (!RemoteGatewayLifecycleGate.CanStopNativeGateway())
                return;

            _manager?.SetMirrorSink(null);
            _mirrorSink?.Dispose();
            _mirrorSink = null;

            var handle = _handle;
            var context = _context;
            var callbacks = _callbacks;
            _handle = null;
            _context = IntPtr.Zero;
            _callbacks = null;
            _connectionStatus = "ShuttingDown";

            try
            {
                // GatewayStop is blocking; callback roots must outlive it across reload/quit paths.
                handle?.Dispose();
            }
            finally
            {
                if (context != IntPtr.Zero)
                    RemoteGatewayNativeMethods.ContextFree(context);
                callbacks?.Dispose();
                _events = null;
                _connectionStatus = "Shutdown";
            }

        }

        private void CleanupFailedStart()
        {
            _mirrorSink?.Dispose();
            _mirrorSink = null;
            _handle?.Dispose();
            _handle = null;
            if (_context != IntPtr.Zero)
            {
                RemoteGatewayNativeMethods.ContextFree(_context);
                _context = IntPtr.Zero;
            }

            _callbacks?.Dispose();
            _callbacks = null;
            _events = null;
            _connectionStatus = "Shutdown";
        }

        private void DrainGatewayEvents()
        {
            if (_events == null)
                return;

            _drainScratch.Clear();
            _events.DrainTo(_drainScratch, 64);
            foreach (var item in _drainScratch)
            {
                if (item.Kind == RemoteGatewayEventKind.ConnectionStatusChanged)
                    _connectionStatus = item.ConnectionStatus.ToString();
            }
        }

        private string ResolveDeviceToken()
        {
            var token = Environment.GetEnvironmentVariable(DeviceTokenEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(token))
                return token;

#if UNITY_EDITOR
            token = EditorUserSettings.GetConfigValue(EditorUserSettingsTokenKey);
            if (!string.IsNullOrWhiteSpace(token))
                return token;
#endif

            if (!string.IsNullOrWhiteSpace(_deviceToken))
            {
                if (!_warnedSerializedToken)
                {
                    _warnedSerializedToken = true;
                    Debug.LogWarning("[Foxglove] Remote gateway is using a serialized token fallback. A token in a scene can end up in git.");
                }

                return _deviceToken;
            }

            return string.Empty;
        }

        private bool EnsureManager()
        {
            if (_manager != null)
                return true;

            _manager = GetComponent<FoxgloveManager>();
            if (_manager != null)
                return true;

            _manager = FindObjectOfType<FoxgloveManager>();
            return _manager != null;
        }

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
