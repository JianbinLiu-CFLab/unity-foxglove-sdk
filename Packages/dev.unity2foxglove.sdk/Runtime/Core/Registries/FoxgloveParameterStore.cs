// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Registries
// Purpose: Thread-safe parameter store. Parameters must be explicitly
// registered before they can be read or written by Foxglove clients.

using System;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>
        /// Fired when a parameter value changes (name, new value, type).
        /// The event is raised outside the store lock with an immutable-by-contract clone.
        /// </summary>
        public event Action<string, JToken, string> OnParameterChanged;

        /// <summary>
        /// A registration lease that can remove only the entry created by that
        /// lease. Disposing an older lease never removes a newer replacement.
        /// </summary>
        public sealed class ParameterRegistration : IDisposable
        {
            private readonly FoxgloveParameterStore _store;
            private readonly string _name;
            private int _disposed;

            internal ParameterRegistration(FoxgloveParameterStore store, string name)
            {
                _store = store;
                _name = name;
            }

            internal string Name => _name;
            internal bool BelongsTo(FoxgloveParameterStore store) => ReferenceEquals(_store, store);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    _store.UnregisterOwned(this);
            }
        }

        /// <summary>Register a parameter. Overwrites if already exists. Fires OnParameterChanged.</summary>
        public void Register(string name, JToken value, string type, bool writable)
        {
            var normalizedType = NormalizeParameterType(type);
            if (!IsSupportedParameterType(normalizedType))
                throw new ArgumentException($"Unsupported parameter type: {normalizedType}", nameof(type));

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
            handler?.Invoke(name, CloneValue(normalizedValue), normalizedType);
        }

        /// <summary>
        /// Register a parameter and return an ownership lease for this exact
        /// registration. A later registration under the same name supersedes
        /// the old entry without being removable by the old lease.
        /// </summary>
        public ParameterRegistration RegisterOwned(string name, JToken value, string type, bool writable)
        {
            var normalizedType = NormalizeParameterType(type);
            if (!IsSupportedParameterType(normalizedType))
                throw new ArgumentException($"Unsupported parameter type: {normalizedType}", nameof(type));

            if (!TryNormalizeValueForType(normalizedType, value, out var normalizedValue))
            {
                _logger?.LogWarning(
                    $"Parameter '{name}' value does not match declared type '{normalizedType}'; using the type default.");
                normalizedValue = DefaultValueForType(normalizedType);
            }

            var registration = new ParameterRegistration(this, name);
            lock (_lock)
            {
                _params[name] = new ParameterEntry
                {
                    Value = normalizedValue,
                    Type = normalizedType,
                    Writable = writable,
                    Owner = registration
                };
            }

            var handler = OnParameterChanged;
            handler?.Invoke(name, CloneValue(normalizedValue), normalizedType);
            return registration;
        }

        /// <summary>Unregister a parameter.</summary>
        public bool Unregister(string name)
        {
            lock (_lock) { return _params.Remove(name); }
        }

        /// <summary>Remove an entry only when it is still owned by the supplied lease.</summary>
        public bool UnregisterOwned(ParameterRegistration registration)
        {
            if (registration == null || !registration.BelongsTo(this))
                return false;
            lock (_lock)
            {
                if (!_params.TryGetValue(registration.Name, out var entry)
                    || !ReferenceEquals(entry.Owner, registration))
                    return false;
                return _params.Remove(registration.Name);
            }
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
            handler?.Invoke(name, CloneValue(normalizedValue), type);
            return true;
        }

        public static string NormalizeParameterType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return "number";

            var trimmed = type.Trim();
            return string.Equals(trimmed, "bool", StringComparison.OrdinalIgnoreCase)
                ? "boolean"
                : trimmed;
        }

        public static bool IsSupportedParameterType(string type)
        {
            switch (NormalizeParameterType(type))
            {
                case "number":
                case "string":
                case "boolean":
                case "number[]":
                    return true;
                default:
                    return false;
            }
        }

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
                    return new JValue(0);
                default:
                    throw new ArgumentException($"Unsupported parameter type: {type}", nameof(type));
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
                        normalized = value;
                        return true;
                    }
                    return false;
                case "string":
                    if (value.Type == JTokenType.String)
                    {
                        normalized = value;
                        return true;
                    }
                    return false;
                case "boolean":
                    if (value.Type == JTokenType.Boolean)
                    {
                        normalized = value;
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
                    return false;
            }
        }

        /// <summary>Get a single parameter as a wire Parameter DTO, or null.</summary>
        public Parameter GetWireParameter(string name)
        {
            lock (_lock)
            {
                if (!_params.TryGetValue(name, out var entry)) return null;
                return ToWireParameter(name, entry);
            }
        }

        /// <summary>Get all registered parameters as wire DTOs.</summary>
        public List<Parameter> GetAllWireParameters()
        {
            lock (_lock)
            {
                var result = new List<Parameter>(_params.Count);
                foreach (var (name, entry) in _params)
                    result.Add(ToWireParameter(name, entry));
                return result;
            }
        }

        /// <summary>Get a set of parameters matching the given names. Empty/null names returns all.</summary>
        public List<Parameter> GetWireParameters(IEnumerable<string> names)
        {
            if (names is IReadOnlyList<string> namesList)
                return GetWireParameters(namesList);

            List<string> requestedNames = null;
            if (names != null)
            {
                requestedNames = new List<string>();
                foreach (var name in names)
                    requestedNames.Add(name);
            }

            return GetWireParameters((IReadOnlyList<string>)requestedNames);
        }

        /// <summary>Get a set of parameters matching the given names. Empty/null names returns all.</summary>
        public List<Parameter> GetWireParameters(IReadOnlyList<string> names)
        {
            lock (_lock)
            {
                var result = names == null
                    ? new List<Parameter>(_params.Count)
                    : new List<Parameter>(names.Count);
                if (names == null)
                {
                    foreach (var (n, e) in _params)
                        result.Add(ToWireParameter(n, e));
                }
                else
                {
                    foreach (var n in names)
                    {
                        if (_params.TryGetValue(n, out var entry))
                            result.Add(ToWireParameter(n, entry));
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
            public ParameterRegistration Owner;
        }

        private static Parameter ToWireParameter(string name, ParameterEntry entry)
            => new Parameter { Name = name, Value = CloneValue(entry.Value), Type = entry.Type };

        private static JToken CloneValue(JToken value) => value?.DeepClone();
    }
}
