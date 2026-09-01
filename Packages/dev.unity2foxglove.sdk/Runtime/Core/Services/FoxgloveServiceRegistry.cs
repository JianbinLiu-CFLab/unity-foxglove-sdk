// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Services
// Purpose: Thread-safe registry of advertised Foxglove services and pending
// service calls. Provides handler dispatch, timeout sweep, and drain.

using System;
using System.Collections.Generic;
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
        private readonly List<(uint clientId, uint callId)> _completedKeysScratch = new();
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
            if (string.IsNullOrWhiteSpace(descriptor.Name))
                throw new ArgumentException("Service name is required.", nameof(descriptor));

            lock (_lock)
            {
                var id = _nextServiceId++;
                _services[id] = CloneDescriptorWithId(descriptor, id);
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

        /// <summary>
        /// Atomically remove a service while retaining its descriptor and handler
        /// so a caller can compensate an external publication failure.
        /// </summary>
        internal bool TryRemove(
            uint serviceId,
            out ServiceDescriptor descriptor,
            out Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler)
        {
            lock (_lock)
            {
                if (!_services.TryGetValue(serviceId, out var stored))
                {
                    descriptor = null;
                    handler = null;
                    return false;
                }

                descriptor = CloneDescriptor(stored);
                _services.Remove(serviceId);
                _handlers.TryGetValue(serviceId, out handler);
                _handlers.Remove(serviceId);
                return true;
            }
        }

        /// <summary>Restore a previously removed service with its original ID and handler.</summary>
        internal void Restore(
            uint serviceId,
            ServiceDescriptor descriptor,
            Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Name))
                throw new ArgumentException("Service name is required.", nameof(descriptor));

            lock (_lock)
            {
                if (_services.ContainsKey(serviceId))
                    throw new InvalidOperationException($"Service {serviceId} is already registered.");

                _services[serviceId] = CloneDescriptorWithId(descriptor, serviceId);
                if (handler != null)
                    _handlers[serviceId] = handler;
                if (_nextServiceId <= serviceId)
                    _nextServiceId = serviceId + 1;
            }
        }

        /// <summary>Get a service descriptor by ID, or null.</summary>
        public ServiceDescriptor GetById(uint serviceId)
        {
            lock (_lock) { return _services.TryGetValue(serviceId, out var s) ? CloneDescriptor(s) : null; }
        }

        /// <summary>Try to get a service descriptor by ID.</summary>
        public bool TryGet(uint serviceId, out ServiceDescriptor descriptor)
        {
            lock (_lock)
            {
                if (_services.TryGetValue(serviceId, out var stored))
                {
                    descriptor = CloneDescriptor(stored);
                    return true;
                }

                descriptor = null;
                return false;
            }
        }

        /// <summary>Snapshot of all registered services for advertise.</summary>
        public List<ServiceDescriptor> GetAll()
        {
            lock (_lock)
            {
                var result = new List<ServiceDescriptor>(_services.Count);
                foreach (var descriptor in _services.Values)
                    result.Add(CloneDescriptor(descriptor));
                return result;
            }
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
            if (payload != null && payload.Length > MaxPayloadBytes)
            {
                call = null;
                error = $"Service call payload exceeds {MaxPayloadBytes} bytes";
                return false;
            }

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
            var result = new List<FoxgloveServiceCall>();
            CopyPendingCallsTo(result);
            return result;
        }

        /// <summary>Copy pending (not yet completed) calls into a caller-owned list.</summary>
        public void CopyPendingCallsTo(List<FoxgloveServiceCall> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (_lock)
            {
                destination.Clear();
                foreach (var call in _pending.Values)
                    if (!call.IsCompleted)
                        destination.Add(call);
            }
        }

        /// <summary>
        /// Drain all completed calls (success or failure) and remove from pending.
        /// Returns calls that need a response sent. Caller must actually send the response/failure.
        /// </summary>
        public List<FoxgloveServiceCall> DrainCompleted()
        {
            var completed = new List<FoxgloveServiceCall>();
            DrainCompletedTo(completed);
            return completed;
        }

        /// <summary>Drain all completed calls into a caller-owned list and remove them from pending.</summary>
        public void DrainCompletedTo(List<FoxgloveServiceCall> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (_lock)
            {
                destination.Clear();
                _completedKeysScratch.Clear();
                foreach (var (key, call) in _pending)
                {
                    if (call.IsCompleted)
                    {
                        destination.Add(call);
                        _completedKeysScratch.Add(key);
                    }
                }
                try
                {
                    foreach (var key in _completedKeysScratch)
                        RemovePendingCall(key);
                }
                finally
                {
                    _completedKeysScratch.Clear();
                }
            }
        }

        /// <summary>
        /// Timeout and fail calls that exceed the timeout duration.
        /// Completed timeout failures remain pending until DrainCompleted sends
        /// the failure response and removes them from the pending-call budget.
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
                _completedKeysScratch.Clear();
                try
                {
                    foreach (var (key, call) in _pending)
                        if (call.ClientId == clientId)
                            _completedKeysScratch.Add(key);
                    foreach (var key in _completedKeysScratch)
                        RemovePendingCall(key);
                }
                finally
                {
                    _completedKeysScratch.Clear();
                }
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

        private static ServiceDescriptor CloneDescriptorWithId(ServiceDescriptor descriptor, uint id)
        {
            var clone = CloneDescriptor(descriptor);
            clone.Id = id;
            return clone;
        }

        private static ServiceDescriptor CloneDescriptor(ServiceDescriptor descriptor)
        {
            if (descriptor == null)
                return null;

            return new ServiceDescriptor
            {
                Id = descriptor.Id,
                Name = descriptor.Name,
                Type = descriptor.Type,
                Request = CloneSchemaDescriptor(descriptor.Request),
                Response = CloneSchemaDescriptor(descriptor.Response)
            };
        }

        private static ServiceSchemaDescriptor CloneSchemaDescriptor(ServiceSchemaDescriptor descriptor)
        {
            if (descriptor == null)
                return null;

            return new ServiceSchemaDescriptor
            {
                Encoding = descriptor.Encoding,
                SchemaName = descriptor.SchemaName,
                Schema = descriptor.Schema
            };
        }
    }
}
