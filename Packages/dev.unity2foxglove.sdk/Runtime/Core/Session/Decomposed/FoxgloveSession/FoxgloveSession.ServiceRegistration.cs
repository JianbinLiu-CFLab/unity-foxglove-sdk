// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Session
// Purpose: Foxglove WebSocket session — owns channel/advertise/subscribe
// lifecycle, client messaging dispatch, connection graph, and MCAP recording
// integration. Split into partial classes for readability (Connection,
// Parameters, Services).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Core session that handles Foxglove WebSocket protocol logic:
    /// channel lifecycle, subscription routing, binary message encoding,
    /// client text/binary dispatch, time broadcast, connection graph,
    /// and serverInfo on connect.
    ///
    /// Split across source files for readability:
    /// FoxgloveSession.Connection.cs (subscribe/unsubscribe,
    /// ConnectionGraph, ClientPublish, PlaybackControl, Assets),
    /// FoxgloveSession.Parameters.cs, FoxgloveSession.Services.cs, and
    /// the Phase192 lifecycle, channel, publishing, service, time, and
    /// transport sections.
    /// </summary>
    /// <remarks>Phase192 source sections: FoxgloveSession.Lifecycle.cs, FoxgloveSession.Channels.cs, FoxgloveSession.SchemaChannels.cs, FoxgloveSession.Publishing.cs, FoxgloveSession.ServiceRegistration.cs, FoxgloveSession.TimeAndEncoding.cs, FoxgloveSession.TransportEvents.cs</remarks>
    public partial class FoxgloveSession : IDisposable
    {
        /// <summary>
        /// Register a service endpoint, advertise it to all clients, and
        /// update the connection graph. Returns the assigned service ID.
        /// </summary>
        public uint RegisterService(Protocol.ServiceDescriptor descriptor)
        {
            lock (_serviceLifecycleLock)
            {
                var id = _services.Register(descriptor);
                try
                {
                    AdvertiseRegisteredServiceCore(id);
                    return id;
                }
                catch
                {
                    // Publication is part of registration. Do not leave a
                    // service in the registry when its first advertisement
                    // failed; the core also retracts a possibly partial frame.
                    _services.Unregister(id);
                    throw;
                }
            }
        }

        /// <summary>
        /// Register a runtime-owned service while holding the same publication
        /// lock as the session-facing overload. This closes the gap between
        /// inserting the descriptor and advertising it to connected clients.
        /// </summary>
        internal uint RegisterServiceFromRuntime(
            Protocol.ServiceDescriptor descriptor,
            Func<JToken, JToken> handler)
        {
            lock (_serviceLifecycleLock)
            {
                var id = handler != null
                    ? _services.Register(descriptor, handler)
                    : _services.Register(descriptor);
                try
                {
                    AdvertiseRegisteredServiceCore(id);
                    return id;
                }
                catch
                {
                    _services.Unregister(id);
                    throw;
                }
            }
        }

        /// <summary>
        /// Unregister a service endpoint, unadvertise it from clients, and
        /// remove its connection-graph provider edge.
        /// </summary>
        public bool UnregisterService(uint serviceId)
        {
            lock (_serviceLifecycleLock)
            {
                if (!_services.TryRemove(serviceId, out var service, out var handler))
                    return false;

                var graphRemoved = false;
                // Mark the attempt before invoking the transport: a custom
                // transport may write a frame and then throw.
                var unadvertiseAttempted = false;
                var unadvertiseJson = JsonConvert.SerializeObject(new UnadvertiseServices
                {
                    ServiceIds = new List<uint> { serviceId }
                });
                var advertiseJson = JsonConvert.SerializeObject(new AdvertiseServices
                {
                    Services = new List<ServiceDescriptor> { service }
                });

                try
                {
                    // Remove the server-side admission entry first. The lock
                    // also protects the service-call admission check below.
                    _graph.RemoveAdvertisedService(service.Name);
                    graphRemoved = true;
                    _graph.BroadcastUpdate();
                    unadvertiseAttempted = true;
                    _transport.BroadcastText(unadvertiseJson);
                    return true;
                }
                catch
                {
                    if (unadvertiseAttempted)
                        TryBroadcastServiceCompensation(advertiseJson, "advertise");

                    if (graphRemoved)
                    {
                        try
                        {
                            _graph.AddAdvertisedService(service.Name);
                            _graph.BroadcastUpdate();
                        }
                        catch (Exception compensationFailure)
                        {
                            _logger.LogWarning(
                                $"Service graph rollback failed for {serviceId}: " +
                                $"{compensationFailure.GetType().Name}: {compensationFailure.Message}");
                        }
                    }

                    try
                    {
                        _services.Restore(serviceId, service, handler);
                    }
                    catch (Exception restoreFailure)
                    {
                        _logger.LogError(
                            $"Service registry rollback failed for {serviceId}: " +
                            $"{restoreFailure.GetType().Name}: {restoreFailure.Message}");
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Advertise an already-registered service and update the connection graph.
        /// Used by runtime-owned service registrations that share this session registry.
        /// </summary>
        internal void AdvertiseRegisteredService(uint serviceId)
        {
            lock (_serviceLifecycleLock)
            {
                AdvertiseRegisteredServiceCore(serviceId);
            }
        }

        private void AdvertiseRegisteredServiceCore(uint serviceId)
        {
            var service = _services.GetById(serviceId);
            if (service == null)
                return;

            var advertiseJson = JsonConvert.SerializeObject(
                new Protocol.AdvertiseServices { Services = new List<ServiceDescriptor> { service } });
            var graphAdded = false;
            var advertiseAttempted = false;
            try
            {
                // Validate and publish the graph entry before advertising the
                // service to clients. A malformed service name must fail
                // before any client-visible advertise frame is emitted.
                _graph.AddAdvertisedService(service.Name);
                graphAdded = true;
                _graph.BroadcastUpdate();
                // A transport is allowed to make a partial write before
                // reporting failure, so every attempted advertise gets a
                // best-effort client-visible compensation.
                advertiseAttempted = true;
                _transport.BroadcastText(advertiseJson);
            }
            catch
            {
                if (advertiseAttempted)
                    TryBroadcastServiceCompensation(
                        JsonConvert.SerializeObject(new UnadvertiseServices
                        {
                            ServiceIds = new List<uint> { serviceId }
                        }),
                        "unadvertise");

                if (graphAdded)
                {
                    try
                    {
                        _graph.RemoveAdvertisedService(service.Name);
                        _graph.BroadcastUpdate();
                    }
                    catch (Exception compensationFailure)
                    {
                        _logger.LogWarning(
                            $"Service graph rollback failed for {serviceId}: " +
                            $"{compensationFailure.GetType().Name}: {compensationFailure.Message}");
                    }
                }
                throw;
            }
        }

        private void TryBroadcastServiceCompensation(string json, string operation)
        {
            try
            {
                _transport.BroadcastText(json);
            }
            catch (Exception compensationFailure)
            {
                _logger.LogWarning(
                    $"Service {operation} compensation failed: " +
                    $"{compensationFailure.GetType().Name}: {compensationFailure.Message}");
            }
        }

    }
}
