// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Services
// Purpose: Thread-safe registry of advertised Foxglove services and pending
// service calls. Provides handler dispatch, timeout sweep, and drain.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Registry of available services and pending service calls.
    /// </summary>
    public class FoxgloveServiceRegistry
    {
        private readonly Dictionary<uint, ServiceDescriptor> _services = new();
        // Key: (clientId, callId) — two clients may independently use the same callId
        private readonly Dictionary<(uint clientId, uint callId), FoxgloveServiceCall> _pending = new();
        private readonly Dictionary<uint, int> _pendingCountByClient = new();
        private readonly object _lock = new();
        private uint _nextServiceId = 1;
        private readonly Dictionary<uint, Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken>> _handlers = new();

        /// <summary>Register a service. Returns the assigned service ID.</summary>
        public uint Register(ServiceDescriptor descriptor)
            => Register(descriptor, handler: null);

        /// <summary>Register a service with a handler delegate.</summary>
        public uint Register(ServiceDescriptor descriptor, Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            lock (_lock)
            {
                var id = _nextServiceId++;
                descriptor.Id = id;
                _services[id] = descriptor;
                if (handler != null)
                    _handlers[id] = handler;
                return id;
            }
        }

        /// <summary>Get the handler delegate for a service.</summary>
        public Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> GetHandler(uint serviceId)
        {
            lock (_lock) { _handlers.TryGetValue(serviceId, out var h); return h; }
        }

        /// <summary>Unregister a service by ID.</summary>
        public bool Unregister(uint serviceId)
        {
            lock (_lock)
            {
                _handlers.Remove(serviceId);
                return _services.Remove(serviceId);
            }
        }

        /// <summary>Get a service descriptor by ID, or null.</summary>
        public ServiceDescriptor GetById(uint serviceId)
        {
            lock (_lock) { return _services.TryGetValue(serviceId, out var s) ? s : null; }
        }

        /// <summary>Try to get a service descriptor by ID.</summary>
        public bool TryGet(uint serviceId, out ServiceDescriptor descriptor)
        {
            lock (_lock) { return _services.TryGetValue(serviceId, out descriptor); }
        }

        /// <summary>Snapshot of all registered services for advertise.</summary>
        public List<ServiceDescriptor> GetAll()
        {
            lock (_lock) { return _services.Values.ToList(); }
        }

        // ── Pending calls ──

        /// <summary>Maximum service payload size in bytes (1 MiB).</summary>
        public const int MaxPayloadBytes = 1_048_576;
        /// <summary>Maximum pending service calls accepted from a single client.</summary>
        public const int MaxPendingCallsPerClient = 64;
        /// <summary>Maximum pending service calls accepted across all clients.</summary>
        public const int MaxPendingCallsTotal = 256;
        /// <summary>Default service call timeout (10 seconds).</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Enqueue a new service call using the client-provided callId.</summary>
        public FoxgloveServiceCall Enqueue(uint serviceId, uint callId, uint clientId, string encoding, byte[] payload)
        {
            if (TryEnqueue(serviceId, callId, clientId, encoding, payload, out var call, out var error))
                return call;
            throw new InvalidOperationException(error);
        }

        /// <summary>
        /// Try to enqueue a service call while enforcing pending-call budgets.
        /// </summary>
        public bool TryEnqueue(
            uint serviceId,
            uint callId,
            uint clientId,
            string encoding,
            byte[] payload,
            out FoxgloveServiceCall call,
            out string error)
        {
            return TryEnqueue(
                serviceId,
                callId,
                clientId,
                encoding,
                payload,
                jsonPayload: null,
                out call,
                out error);
        }

        /// <summary>
        /// Try to enqueue a service call and carry a parsed JSON payload from ingress.
        /// </summary>
        public bool TryEnqueue(
            uint serviceId,
            uint callId,
            uint clientId,
            string encoding,
            byte[] payload,
            JToken jsonPayload,
            out FoxgloveServiceCall call,
            out string error)
        {
            lock (_lock)
            {
                var key = (clientId, callId);
                if (_pending.ContainsKey(key))
                {
                    call = null;
                    error = $"Duplicate pending service call {callId} for client {clientId}";
                    return false;
                }

                _pendingCountByClient.TryGetValue(clientId, out var clientPending);
                if (clientPending >= MaxPendingCallsPerClient)
                {
                    call = null;
                    error = $"Too many pending service calls for client {clientId}";
                    return false;
                }

                if (_pending.Count >= MaxPendingCallsTotal)
                {
                    call = null;
                    error = "Too many pending service calls";
                    return false;
                }

                call = new FoxgloveServiceCall
                {
                    ServiceId = serviceId,
                    CallId = callId,
                    ClientId = clientId,
                    Encoding = encoding,
                    Payload = payload,
                    JsonPayload = jsonPayload,
                    CreatedAt = DateTime.UtcNow
                };
                _pending[key] = call;
                _pendingCountByClient[clientId] = clientPending + 1;
                error = null;
                return true;
            }
        }

        /// <summary>Complete a pending call with a response payload.</summary>
        public void CompleteResponse(uint clientId, uint callId, string encoding, byte[] payload)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue((clientId, callId), out var call))
                    call.Complete(encoding, payload);
            }
        }

        /// <summary>Fail a pending call with a message.</summary>
        public void Fail(uint clientId, uint callId, string message)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue((clientId, callId), out var call))
                    call.Fail(message);
            }
        }

        /// <summary>Snapshot of pending (not yet completed) calls, for Unity handler polling.</summary>
        public List<FoxgloveServiceCall> GetPendingCalls()
        {
            lock (_lock) { return _pending.Values.Where(c => !c.IsCompleted).ToList(); }
        }

        /// <summary>
        /// Drain all completed calls (success or failure) and remove from pending.
        /// Returns calls that need a response sent. Caller must actually send the response/failure.
        /// </summary>
        public List<FoxgloveServiceCall> DrainCompleted()
        {
            var completed = new List<FoxgloveServiceCall>();
            lock (_lock)
            {
                var completedKeys = new List<(uint clientId, uint callId)>();
                foreach (var (key, call) in _pending)
                {
                    if (call.IsCompleted)
                    {
                        completed.Add(call);
                        completedKeys.Add(key);
                    }
                }
                foreach (var key in completedKeys)
                    RemovePendingCall(key);
            }
            return completed;
        }

        /// <summary>
        /// Timeout and fail calls that exceed the timeout duration.
        /// </summary>
        public void SweepTimeouts(TimeSpan timeout)
        {
            lock (_lock)
            {
                foreach (var (_, call) in _pending)
                {
                    if (!call.IsCompleted && call.IsTimedOut(timeout))
                        call.Fail($"Service call timed out after {timeout.TotalSeconds:F0}s");
                }
            }
        }

        /// <summary>Remove all pending calls for a client (on disconnect).</summary>
        public void RemoveClientCalls(uint clientId)
        {
            lock (_lock)
            {
                var toRemove = new List<(uint, uint)>();
                foreach (var (key, call) in _pending)
                    if (call.ClientId == clientId)
                        toRemove.Add(key);
                foreach (var key in toRemove)
                    _pending.Remove(key);
                _pendingCountByClient.Remove(clientId);
            }
        }

        /// <summary>Remove all pending service calls while keeping registered service definitions and handlers.</summary>
        public void ClearPendingCalls()
        {
            lock (_lock)
            {
                _pending.Clear();
                _pendingCountByClient.Clear();
            }
        }

        /// <summary>Remove all services and pending calls.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _services.Clear();
                _pending.Clear();
                _pendingCountByClient.Clear();
                _handlers.Clear();
            }
        }

        private void RemovePendingCall((uint clientId, uint callId) key)
        {
            if (!_pending.Remove(key))
                return;

            if (!_pendingCountByClient.TryGetValue(key.clientId, out var count))
                return;

            if (count <= 1)
                _pendingCountByClient.Remove(key.clientId);
            else
                _pendingCountByClient[key.clientId] = count - 1;
        }
    }
}
