// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: FoxgloveSession partial — Parameters get/set/subscribe handlers.
// Client-initiated parameter changes are broadcast to other subscribed
// clients so all Foxglove instances see the updated values.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveSession
    {
        /// <summary>
        /// Respond with current parameter values. If parameterNames is empty,
        /// return all registered parameters.
        /// </summary>
        private void HandleGetParameters(uint clientId, string json)
        {
            GetParameters msg;
            try { msg = JsonConvert.DeserializeObject<GetParameters>(json); }
            catch { _logger.LogWarning($"getParameters parse error from client {clientId}"); return; }

            var list = _parameters.GetWireParameters(msg.ParameterNames?.Count > 0 ? msg.ParameterNames : null);
            var resp = new ParameterValues { Parameters = list, Id = msg.Id };
            _transport.SendText(clientId, JsonConvert.SerializeObject(resp));
        }

        /// <summary>
        /// Apply client parameter changes. Only writable parameters are
        /// modified. Returns the current state of all requested parameters
        /// back to the caller. Changed values are broadcast to other
        /// subscribed clients so they see the update in real time.
        /// </summary>
        private void HandleSetParameters(uint clientId, string json)
        {
            SetParameters msg;
            try { msg = JsonConvert.DeserializeObject<SetParameters>(json); }
            catch { _logger.LogWarning($"setParameters parse error from client {clientId}"); return; }

            var changedNames = new List<string>();
            foreach (var p in msg.Parameters ?? new List<Parameter>())
            {
                if (p != null && p.Name != null && _parameters.TrySetFromClient(p.Name, p.Value))
                    changedNames.Add(p.Name);
            }

            var names = msg.Parameters?.Select(p => p?.Name).Where(n => n != null);
            var current = _parameters.GetWireParameters(names);
            var resp = new ParameterValues { Parameters = current, Id = msg.Id };
            _transport.SendText(clientId, JsonConvert.SerializeObject(resp));

            if (changedNames.Count > 0)
            {
                var broadcast = new ParameterValues { Parameters = _parameters.GetWireParameters(changedNames) };
                var broadcastJson = JsonConvert.SerializeObject(broadcast);
                foreach (var subscribedClientId in GetParamSubscribersForChanged(changedNames, clientId))
                    _transport.SendText(subscribedClientId, broadcastJson);
            }
        }

        private IEnumerable<uint> GetParamSubscribersForChanged(List<string> names, uint excludeClient)
        {
            foreach (var subscribedClientId in _paramSubs.GetSubscribedClientIds())
            {
                if (subscribedClientId == excludeClient) continue;
                foreach (var parameterName in names)
                {
                    if (_paramSubs.IsSubscribed(subscribedClientId, parameterName))
                    { yield return subscribedClientId; break; }
                }
            }
        }

        /// <summary>
        /// Broadcast current parameter values to clients subscribed via
        /// subscribeParameterUpdates. Intended for runtime-owned parameter
        /// changes that do not originate from a Foxglove setParameters request.
        /// </summary>
        public void BroadcastParameterValues(IEnumerable<string> parameterNames)
        {
            var names = parameterNames?
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();
            if (names == null || names.Count == 0)
                return;

            var parameters = _parameters.GetWireParameters(names);
            if (parameters.Count == 0)
                return;

            var broadcastJson = JsonConvert.SerializeObject(new ParameterValues { Parameters = parameters });
            foreach (var cid in GetParamSubscribersForChanged(names, 0))
                _transport.SendText(cid, broadcastJson);
        }

        private void HandleSubscribeParameterUpdates(uint clientId, string json)
        {
            SubscribeParameterUpdates msg;
            try { msg = JsonConvert.DeserializeObject<SubscribeParameterUpdates>(json); }
            catch { _logger.LogWarning($"subscribeParameterUpdates parse error from client {clientId}"); return; }

            _paramSubs.Subscribe(clientId, msg.ParameterNames);
        }

        private void HandleUnsubscribeParameterUpdates(uint clientId, string json)
        {
            UnsubscribeParameterUpdates msg;
            try { msg = JsonConvert.DeserializeObject<UnsubscribeParameterUpdates>(json); }
            catch { _logger.LogWarning($"unsubscribeParameterUpdates parse error from client {clientId}"); return; }

            _paramSubs.Unsubscribe(clientId, msg.ParameterNames);
        }
    }
}
