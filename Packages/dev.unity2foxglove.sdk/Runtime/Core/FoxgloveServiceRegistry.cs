using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private readonly object _lock = new();
        private uint _nextServiceId = 1;

        /// <summary>Register a service. Returns the assigned service ID.</summary>
        public uint Register(ServiceDescriptor descriptor)
        {
            lock (_lock)
            {
                var id = _nextServiceId++;
                descriptor.Id = id;
                _services[id] = descriptor;
                return id;
            }
        }

        public bool Unregister(uint serviceId)
        {
            lock (_lock) { return _services.Remove(serviceId); }
        }

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

        public const int MaxPayloadBytes = 1_048_576; // 1 MiB
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

        /// <summary>Enqueue a new service call using the client-provided callId.</summary>
        public FoxgloveServiceCall Enqueue(uint serviceId, uint callId, uint clientId, string encoding, byte[] payload)
        {
            lock (_lock)
            {
                var call = new FoxgloveServiceCall
                {
                    ServiceId = serviceId,
                    CallId = callId,
                    ClientId = clientId,
                    Encoding = encoding,
                    Payload = payload,
                    CreatedAt = DateTime.UtcNow
                };
                _pending[(clientId, callId)] = call;
                return call;
            }
        }

        /// <summary>Complete a pending call with a response payload.</summary>
        public void CompleteResponse(uint clientId, uint callId, string encoding, byte[] payload)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue((clientId, callId), out var call))
                {
                    call.ResponseEncoding = encoding;
                    call.ResponsePayload = payload;
                    Interlocked.Exchange(ref call.Completed, 1);
                }
            }
        }

        /// <summary>Fail a pending call with a message.</summary>
        public void Fail(uint clientId, uint callId, string message)
        {
            lock (_lock)
            {
                if (_pending.TryGetValue((clientId, callId), out var call))
                {
                    call.FailureMessage = message;
                    Interlocked.Exchange(ref call.Completed, 1);
                }
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
                foreach (var (key, call) in _pending.ToList())
                {
                    if (call.IsCompleted)
                    {
                        completed.Add(call);
                        _pending.Remove(key);
                    }
                }
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
                    {
                        call.FailureMessage = $"Service call timed out after {timeout.TotalSeconds:F0}s";
                        Interlocked.Exchange(ref call.Completed, 1);
                    }
                }
            }
        }

        /// <summary>Remove all pending calls for a client (on disconnect).</summary>
        public void RemoveClientCalls(uint clientId)
        {
            lock (_lock)
            {
                foreach (var (key, call) in _pending.ToList())
                {
                    if (call.ClientId == clientId)
                    {
                        call.FailureMessage = "Client disconnected";
                        Interlocked.Exchange(ref call.Completed, 1);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_lock) { _services.Clear(); _pending.Clear(); }
        }
    }
}
