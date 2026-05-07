// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Thread-safe parameter store. Parameters must be explicitly
// registered before they can be read or written by Foxglove clients.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe parameter store. Parameters must be explicitly registered
    /// before they can be read/written by clients.
    /// </summary>
    public class FoxgloveParameterStore
    {
        private readonly Dictionary<string, ParameterEntry> _params = new();
        private readonly object _lock = new();

        /// <summary>Fired when a parameter value changes (name, new value, type).</summary>
        public event Action<string, JToken, string> OnParameterChanged;

        /// <summary>Register a parameter. Overwrites if already exists. Fires OnParameterChanged.</summary>
        public void Register(string name, JToken value, string type, bool writable)
        {
            lock (_lock)
            {
                _params[name] = new ParameterEntry { Value = value, Type = type, Writable = writable };
            }
            var handler = OnParameterChanged;
            handler?.Invoke(name, value, type);
        }

        /// <summary>Unregister a parameter.</summary>
        public bool Unregister(string name)
        {
            lock (_lock) { return _params.Remove(name); }
        }

        /// <summary>Set a parameter's value from a client request. Silently no-ops for unknown/read-only params.</summary>
        public bool TrySetFromClient(string name, JToken value)
        {
            string type;
            lock (_lock)
            {
                if (!_params.TryGetValue(name, out var entry) || !entry.Writable)
                    return false;
                entry.Value = value;
                type = entry.Type;
            }
            var handler = OnParameterChanged;
            handler?.Invoke(name, value, type);
            return true;
        }

        /// <summary>Get a single parameter as a wire Parameter DTO, or null.</summary>
        public Parameter GetWireParameter(string name)
        {
            lock (_lock)
            {
                if (!_params.TryGetValue(name, out var entry)) return null;
                return new Parameter { Name = name, Value = entry.Value, Type = entry.Type };
            }
        }

        /// <summary>Get all registered parameters as wire DTOs.</summary>
        public List<Parameter> GetAllWireParameters()
        {
            lock (_lock)
            {
                var result = new List<Parameter>(_params.Count);
                foreach (var (name, entry) in _params)
                    result.Add(new Parameter { Name = name, Value = entry.Value, Type = entry.Type });
                return result;
            }
        }

        /// <summary>Get a set of parameters matching the given names. Empty/null names returns all.</summary>
        public List<Parameter> GetWireParameters(IEnumerable<string> names)
        {
            lock (_lock)
            {
                var result = new List<Parameter>();
                if (names == null)
                {
                    foreach (var (n, e) in _params)
                        result.Add(new Parameter { Name = n, Value = e.Value, Type = e.Type });
                }
                else
                {
                    foreach (var n in names)
                    {
                        if (_params.TryGetValue(n, out var entry))
                            result.Add(new Parameter { Name = n, Value = entry.Value, Type = entry.Type });
                    }
                }
                return result;
            }
        }

        /// <summary>Remove all parameters.</summary>
        public void Clear()
        {
            lock (_lock) { _params.Clear(); }
        }

        private sealed class ParameterEntry
        {
            public JToken Value;
            public string Type;
            public bool Writable;
        }
    }
}
