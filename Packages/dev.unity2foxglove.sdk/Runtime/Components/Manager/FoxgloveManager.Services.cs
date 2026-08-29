// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Parameter and service registration facade for FoxgloveManager.

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        /// <summary>
        /// Registers a runtime parameter.
        /// </summary>
        /// <param name="name">Parameter path, for example "/cube/color".</param>
        /// <param name="value">Initial value as a JToken.</param>
        /// <param name="type">Foxglove type string, for example "number[]".</param>
        /// <param name="writable">Whether Foxglove clients can modify this parameter.</param>
        public void RegisterParameter(string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            _runtime?.RegisterParameter(name, value, type, writable);
        }

        /// <summary>Register a parameter and return a lease owned by this registration.</summary>
        public Unity.FoxgloveSDK.Core.FoxgloveParameterStore.ParameterRegistration RegisterParameterOwned(
            string name, Newtonsoft.Json.Linq.JToken value, string type, bool writable)
        {
            return _runtime?.RegisterParameterOwned(name, value, type, writable);
        }

        /// <summary>
        /// Unregisters a runtime parameter.
        /// </summary>
        /// <param name="name">Parameter path, for example "/cube/color".</param>
        /// <returns>True when a parameter was removed.</returns>
        public bool UnregisterParameter(string name)
        {
            return _runtime?.UnregisterParameter(name) ?? false;
        }

        /// <summary>
        /// Registers a service.
        /// </summary>
        /// <param name="descriptor">Service descriptor with name, type, request schemas, and response schemas.</param>
        /// <returns>The service identifier, or 0 when the runtime is not available.</returns>
        public uint RegisterService(Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor)
        {
            return _runtime?.RegisterService(descriptor) ?? 0;
        }

        /// <summary>
        /// Registers a service with a JSON request handler.
        /// </summary>
        /// <param name="descriptor">Service descriptor with name, type, request schemas, and response schemas.</param>
        /// <param name="handler">Handler invoked from the runtime tick on the Unity main thread.</param>
        /// <returns>The service identifier, or 0 when the runtime is not available.</returns>
        public uint RegisterService(
            Unity.FoxgloveSDK.Protocol.ServiceDescriptor descriptor,
            System.Func<Newtonsoft.Json.Linq.JToken, Newtonsoft.Json.Linq.JToken> handler)
        {
            return _runtime?.RegisterService(descriptor, handler) ?? 0;
        }

        /// <summary>
        /// Unregisters a service.
        /// </summary>
        /// <param name="serviceId">Service identifier returned by <see cref="RegisterService"/>.</param>
        /// <returns>True when the service was registered and removed.</returns>
        public bool UnregisterService(uint serviceId)
        {
            return _runtime?.UnregisterService(serviceId) == true;
        }
    }
}
