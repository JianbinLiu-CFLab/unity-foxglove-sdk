using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    public partial class FoxgloveSession
    {
        private void HandleGetParameters(uint clientId, string json)
        {
            GetParameters msg;
            try { msg = JsonConvert.DeserializeObject<GetParameters>(json); }
            catch { _logger.LogWarning($"getParameters parse error from client {clientId}"); return; }

            var list = _parameters.GetWireParameters(msg.ParameterNames?.Count > 0 ? msg.ParameterNames : null);
            var resp = new ParameterValues { Parameters = list, Id = msg.Id };
            _transport.SendText(clientId, JsonConvert.SerializeObject(resp));
        }

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
                foreach (var cid in GetParamSubscribersForChanged(changedNames, clientId))
                    _transport.SendText(cid, broadcastJson);
            }
        }

        private IEnumerable<uint> GetParamSubscribersForChanged(List<string> names, uint excludeClient)
        {
            foreach (var cid in _paramSubs.GetSubscribedClientIds())
            {
                if (cid == excludeClient) continue;
                foreach (var n in names)
                {
                    if (_paramSubs.IsSubscribed(cid, n))
                    { yield return cid; break; }
                }
            }
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
