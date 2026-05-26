// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
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
        private readonly IFoxgloveLogger _logger;

        public FoxgloveParameterStore(IFoxgloveLogger logger = null)
        {
            _logger = logger;
        }

        /// <summary>Fired when a parameter value changes (name, new value, type).</summary>
        public event Action<string, JToken, string> OnParameterChanged;

        /// <summary>Register a parameter. Overwrites if already exists. Fires OnParameterChanged.</summary>
        public void Register(string name, JToken value, string type, bool writable)
        {
            var normalizedType = NormalizeParameterType(type);
            if (!TryNormalizeValueForType(normalizedType, value, out var normalizedValue))
            {
                _logger?.LogWarning(
                    $"Parameter '{name}' value does not match declared type '{normalizedType}'; using the type default.");
                normalizedValue = DefaultValueForType(normalizedType);
            }

            lock (_lock)
            {
                _params[name] = new ParameterEntry { Value = normalizedValue, Type = normalizedType, Writable = writable };
            }
            var handler = OnParameterChanged;
            handler?.Invoke(name, normalizedValue, normalizedType);
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
            JToken normalizedValue;
            lock (_lock)
            {
                if (!_params.TryGetValue(name, out var entry) || !entry.Writable)
                    return false;
                if (!TryNormalizeValueForType(entry.Type, value, out normalizedValue))
                    return false;
                entry.Value = normalizedValue;
                type = entry.Type;
            }
            var handler = OnParameterChanged;
            handler?.Invoke(name, normalizedValue, type);
            return true;
        }

        public static string NormalizeParameterType(string type)
            => string.IsNullOrWhiteSpace(type) ? "number" : type.Trim();

        public static JToken DefaultValueForType(string type)
        {
            switch (NormalizeParameterType(type))
            {
                case "string":
                    return JValue.CreateString(string.Empty);
                case "boolean":
                    return new JValue(false);
                case "number[]":
                    return new JArray();
                case "number":
                default:
                    return new JValue(0);
            }
        }

        public static bool TryNormalizeValueForType(string type, JToken value, out JToken normalized)
        {
            normalized = null;
            value ??= DefaultValueForType(type);
            switch (NormalizeParameterType(type))
            {
                case "number":
                    if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                    {
                        normalized = value.DeepClone();
                        return true;
                    }
                    return false;
                case "string":
                    if (value.Type == JTokenType.String)
                    {
                        normalized = value.DeepClone();
                        return true;
                    }
                    return false;
                case "boolean":
                    if (value.Type == JTokenType.Boolean)
                    {
                        normalized = value.DeepClone();
                        return true;
                    }
                    return false;
                case "number[]":
                    if (value is JArray array)
                    {
                        var copy = new JArray();
                        foreach (var item in array)
                        {
                            if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float)
                                return false;
                            copy.Add(item.DeepClone());
                        }

                        normalized = copy;
                        return true;
                    }
                    return false;
                default:
                    normalized = value.DeepClone();
                    return true;
            }
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
            List<string> requestedNames = null;
            if (names != null)
            {
                requestedNames = new List<string>();
                foreach (var name in names)
                    requestedNames.Add(name);
            }

            lock (_lock)
            {
                var result = new List<Parameter>();
                if (requestedNames == null)
                {
                    foreach (var (n, e) in _params)
                        result.Add(new Parameter { Name = n, Value = e.Value, Type = e.Type });
                }
                else
                {
                    foreach (var n in requestedNames)
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
