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

        private readonly List<RemoteGatewayEvent> _drainScratch = new List<RemoteGatewayEvent>();
        private RemoteGatewayEventQueue _events;
        private RemoteGatewayCallbacks _callbacks;
        private RemoteGatewayHandle _handle;
        private RemoteGatewayMirrorSink _mirrorSink;
        private IntPtr _context;
        private string _connectionStatus = "Shutdown";
        private bool _warnedMissingToken;
        private bool _warnedMissingManager;
        private bool _starting;
        private bool _startupFaulted;
        private IRemoteGatewayStartupNativeApi _startupNativeApi = RemoteGatewayStartupNativeApi.Instance;
        private IRemoteGatewayStartupNativeApi _activeContextNativeApi;

        public bool EnableRemoteGateway
        {
            get => _enableRemoteGateway;
            set
            {
                _enableRemoteGateway = value;
                if (!value)
                    _startupFaulted = false;
            }
        }

        public string ConnectionStatus => _connectionStatus;
        public long MirroredMessageCount => _mirrorSink?.MirroredMessageCount ?? 0L;
        public long DroppedMessageCount => _mirrorSink?.DroppedMessageCount ?? 0L;
        internal bool StartupFaultedForTests => _startupFaulted;
        internal bool HasOwnedResourcesForTests
            => _events != null || _callbacks != null || _handle != null || _mirrorSink != null || _context != IntPtr.Zero;
        internal IRemoteGatewayStartupNativeApi StartupNativeApiForTests
        {
            set
            {
                if (HasOwnedResourcesForTests)
                    throw new InvalidOperationException("Cannot replace the startup native API while gateway resources are owned.");

                _startupNativeApi = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        private void Reset()
        {
            _manager = GetComponent<FoxgloveManager>();
        }

        private void OnEnable()
        {
            _startupFaulted = false;
            EnsureManager();
        }

        private void Update()
        {
            DrainGatewayEvents();

            if (_enableRemoteGateway)
                TryStartGateway();
            else
            {
                StopGateway();
                _startupFaulted = false;
            }
        }

        private void OnDisable()
        {
            try
            {
                StopGateway();
            }
            finally
            {
                _startupFaulted = false;
            }
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
            if (_handle != null || _starting || _startupFaulted)
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

            TryStartGatewayWithToken(token);
        }

        internal void TryStartGatewayWithToken(string deviceToken)
        {
            if (_handle != null || _starting || _startupFaulted)
                return;

            _starting = true;
            try
            {
                StartGateway(deviceToken);
            }
            catch (Exception exception) when (IsNativeStartupException(exception))
            {
                RecordStartupFault("native binding threw " + exception.GetType().Name);
            }
            finally
            {
                _starting = false;
            }
        }

        private void StartGateway(string deviceToken)
        {
            var nativeApi = _startupNativeApi;
            var events = new RemoteGatewayEventQueue(Math.Max(1, _eventQueueCapacity));
            RemoteGatewayCallbacks callbacks = null;
            RemoteGatewayHandle handle = null;
            RemoteGatewayMirrorSink mirrorSink = null;
            var context = IntPtr.Zero;
            var mirrorAttached = false;
            var committed = false;

            try
            {
                context = nativeApi.ContextNew();
                if (context == IntPtr.Zero)
                {
                    RecordStartupFault("native context allocation returned null");
                    return;
                }

                callbacks = new RemoteGatewayCallbacks(events);
                var nativeCallbacks = callbacks.CreateNative();

                using (var name = PinnedUtf8String.Create(string.IsNullOrWhiteSpace(_deviceName) ? "Unity2Foxglove" : _deviceName.Trim()))
                using (var token = PinnedUtf8String.Create(deviceToken))
                using (var apiUrl = PinnedUtf8String.Create(string.Empty))
                using (var encodings = PinnedStringArray.Create("json", "protobuf", "cdr"))
                using (var callbackPtr = NativeStructPointer.Create(nativeCallbacks))
                {
                    var options = new RemoteGatewayNativeMethods.FoxgloveGatewayOptions
                    {
                        Context = context,
                        Name = name.Value,
                        DeviceToken = token.Value,
                        Callbacks = callbackPtr.Pointer,
                        Capabilities = RemoteGatewayCapabilityPolicy.CreateOutboundOnlyCapabilities(),
                        SupportedEncodings = encodings.Pointer,
                        SupportedEncodingsCount = (UIntPtr)encodings.Count,
                        FoxgloveApiUrl = apiUrl.Value
                    };

                    var error = nativeApi.GatewayStart(ref options, out var nativeGateway);
                    if (nativeGateway != IntPtr.Zero)
                        handle = new RemoteGatewayHandle(nativeGateway);
                    if (error != RemoteGatewayNativeMethods.FoxgloveError.Ok || handle == null)
                    {
                        RecordStartupFault("native gateway returned " + error);
                        return;
                    }
                }

                var activeHandle = handle;
                var registry = new RemoteGatewayChannelRegistry(context, () => activeHandle.SinkId);
                mirrorSink = new RemoteGatewayMirrorSink(registry);
                var connectionStatus = handle.ConnectionStatus.ToString();
                mirrorSink.Enable();
                _manager.SetMirrorSink(mirrorSink);
                mirrorAttached = true;

                _events = events;
                _callbacks = callbacks;
                _handle = handle;
                _mirrorSink = mirrorSink;
                _context = context;
                _activeContextNativeApi = nativeApi;
                _connectionStatus = connectionStatus;
                committed = true;
                Debug.Log("[Foxglove] Remote gateway started. Publishing to Foxglove Cloud.");
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        if (mirrorAttached)
                            _manager?.SetMirrorSink(null);
                    }
                    finally
                    {
                        DisposeStagedStart(mirrorSink, handle, context, callbacks, nativeApi);
                    }
                }
            }
        }

        private void StopGateway()
        {
            if (_handle == null && _mirrorSink == null && _context == IntPtr.Zero && _callbacks == null && _events == null)
                return;
            if (!RemoteGatewayLifecycleGate.CanStopNativeGateway())
                return;

            var mirrorSink = _mirrorSink;
            var handle = _handle;
            var context = _context;
            var callbacks = _callbacks;
            var nativeApi = _activeContextNativeApi ?? _startupNativeApi;
            _mirrorSink = null;
            _handle = null;
            _context = IntPtr.Zero;
            _callbacks = null;
            _activeContextNativeApi = null;
            _connectionStatus = "ShuttingDown";

            try
            {
                try
                {
                    _manager?.SetMirrorSink(null);
                }
                finally
                {
                    mirrorSink?.Dispose();
                }
            }
            finally
            {
                try
                {
                    // GatewayStop is blocking; callback roots must outlive it across reload/quit paths.
                    handle?.Dispose();
                }
                finally
                {
                    try
                    {
                        if (context != IntPtr.Zero)
                            nativeApi.ContextFree(context);
                    }
                    finally
                    {
                        callbacks?.Dispose();
                        _events = null;
                        _connectionStatus = "Shutdown";
                    }
                }
            }
        }

        private void RecordStartupFault(string reason)
        {
            if (_startupFaulted)
                return;

            _startupFaulted = true;
            _connectionStatus = "Faulted";
            Debug.LogWarning(
                "[Foxglove] Remote gateway startup failed and is blocked until disabled and re-enabled: " + reason);
        }

        private static bool IsNativeStartupException(Exception exception)
            => exception is DllNotFoundException
               || exception is EntryPointNotFoundException
               || exception is BadImageFormatException
               || exception is MarshalDirectiveException
               || exception is SEHException;

        private static void DisposeStagedStart(
            RemoteGatewayMirrorSink mirrorSink,
            RemoteGatewayHandle handle,
            IntPtr context,
            RemoteGatewayCallbacks callbacks,
            IRemoteGatewayStartupNativeApi nativeApi)
        {
            try
            {
                mirrorSink?.Dispose();
            }
            finally
            {
                try
                {
                    // A nonzero gateway returned alongside an error still owns a
                    // blocking stop before its context and callback root can retire.
                    handle?.Dispose();
                }
                finally
                {
                    try
                    {
                        if (context != IntPtr.Zero)
                            nativeApi.ContextFree(context);
                    }
                    finally
                    {
                        callbacks?.Dispose();
                    }
                }
            }
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
                // V1 is outbound-only. Subscription, parameter, client publish,
                // and connection-graph callbacks are drained so the bounded
                // native callback queue stays healthy, but they intentionally
                // do not mutate Unity state until those capabilities are enabled.
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

            return string.Empty;
        }

        private bool EnsureManager()
        {
            if (_manager != null)
                return true;

            // Manager creation/loading can be later than this component's
            // OnEnable. Keep discovery retryable; warning throttling is
            // independent from the lookup itself.
            _manager = GetComponent<FoxgloveManager>();
            if (_manager != null)
                return true;

            _manager = FindObjectOfType<FoxgloveManager>();
            if (_manager != null)
                return true;

            if (!_warnedMissingManager)
            {
                _warnedMissingManager = true;
                Debug.LogWarning("[Foxglove] Remote gateway is enabled but no FoxgloveManager was found.");
            }

            return false;
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
