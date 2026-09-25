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
        private readonly Dictionary<string, ParameterRegistration> _clientUnsetRegistrations =
            new(StringComparer.Ordinal);
        private readonly object _lock = new();
        private readonly IFoxgloveLogger _logger;
        private readonly Func<bool> _mutationAllowed;

        public FoxgloveParameterStore(IFoxgloveLogger logger = null)
        {
            _logger = logger;
        }

        internal FoxgloveParameterStore(IFoxgloveLogger logger, Func<bool> mutationAllowed)
            : this(logger)
        {
            _mutationAllowed = mutationAllowed;
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
                    _store.UnregisterOwnedDuringCleanup(this);
            }
        }

        /// <summary>Register a parameter. Overwrites if already exists. Fires OnParameterChanged.</summary>
        public void Register(string name, JToken value, string type, bool writable)
        {
            ThrowIfMutationBlocked();
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
                _clientUnsetRegistrations.Remove(name);
                _params[name] = new ParameterEntry { Value = normalizedValue, Type = normalizedType, Writable = writable };
            }
            InvokeChangedHandlers(name, normalizedValue, normalizedType);
        }

        /// <summary>
        /// Register a parameter and return an ownership lease for this exact
        /// registration. A later registration under the same name supersedes
        /// the old entry without being removable by the old lease.
        /// </summary>
        public ParameterRegistration RegisterOwned(string name, JToken value, string type, bool writable)
        {
            ThrowIfMutationBlocked();
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
                _clientUnsetRegistrations.Remove(name);
                _params[name] = new ParameterEntry
                {
                    Value = normalizedValue,
                    Type = normalizedType,
                    Writable = writable,
                    Owner = registration
                };
            }

            InvokeChangedHandlers(name, normalizedValue, normalizedType);
            return registration;
        }

        /// <summary>Unregister a parameter.</summary>
        public bool Unregister(string name)
        {
            ThrowIfMutationBlocked();
            lock (_lock)
            {
                _clientUnsetRegistrations.Remove(name);
                return _params.Remove(name);
            }
        }

        /// <summary>Remove an entry only when it is still owned by the supplied lease.</summary>
        public bool UnregisterOwned(ParameterRegistration registration)
        {
            ThrowIfMutationBlocked();
            return UnregisterOwnedCore(registration);
        }

        private bool UnregisterOwnedDuringCleanup(ParameterRegistration registration)
            => UnregisterOwnedCore(registration);

        private bool UnregisterOwnedCore(ParameterRegistration registration)
        {
            if (registration == null || !registration.BelongsTo(this))
                return false;
            lock (_lock)
            {
                if (!_params.TryGetValue(registration.Name, out var entry))
                {
                    if (_clientUnsetRegistrations.TryGetValue(
                            registration.Name,
                            out var unsetRegistration)
                        && ReferenceEquals(unsetRegistration, registration))
                    {
                        _clientUnsetRegistrations.Remove(registration.Name);
                    }
                    return false;
                }
                if (!ReferenceEquals(entry.Owner, registration))
                    return false;
                _clientUnsetRegistrations.Remove(registration.Name);
                return _params.Remove(registration.Name);
            }
        }

        /// <summary>
        /// Set a parameter's value from a runtime/client value. Null is not an
        /// unset operation; protocol client unsets use the internal allow-unset
        /// path owned by FoxgloveSession.
        /// </summary>
        public bool TrySetFromClient(string name, JToken value)
            => TrySetFromClientCore(name, value, allowUnset: false);

        /// <summary>Apply a client setParameters value, including the protocol's null-as-unset form.</summary>
        internal bool TrySetFromClientAllowUnset(string name, JToken value)
            => TrySetFromClientCore(name, value, allowUnset: true);

        /// <summary>Return whether the named parameter was previously unset by a client.</summary>
        internal bool WasUnsetByClient(string name)
        {
            lock (_lock)
                return _clientUnsetRegistrations.ContainsKey(name);
        }

        private bool TrySetFromClientCore(string name, JToken value, bool allowUnset)
        {
            ThrowIfMutationBlocked();
            string type;
            JToken normalizedValue;
            lock (_lock)
            {
                if (!_params.TryGetValue(name, out var entry) || !entry.Writable)
                    return false;

                if (value == null || value.Type == JTokenType.Null)
                {
                    if (!allowUnset)
                        return false;
                    type = entry.Type;
                    _params.Remove(name);
                    _clientUnsetRegistrations[name] = entry.Owner;
                    normalizedValue = null;
                }
                else
                {
                    if (!TryNormalizeValueForType(entry.Type, value, out normalizedValue))
                        return false;
                    entry.Value = normalizedValue;
                    _clientUnsetRegistrations.Remove(name);
                    type = entry.Type;
                }
            }
            InvokeChangedHandlers(name, normalizedValue, type);
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
                case "boolean[]":
                case "string[]":
                case "byte_array":
                case "float64":
                case "float64_array":
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
                case "float64_array":
                case "boolean[]":
                case "string[]":
                    return new JArray();
                case "byte_array":
                    return JValue.CreateString(string.Empty);
                case "float64":
                    return new JValue(0d);
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
                case "boolean[]":
                    if (value is JArray booleanArray)
                    {
                        var copy = new JArray();
                        foreach (var item in booleanArray)
                        {
                            if (item.Type != JTokenType.Boolean)
                                return false;
                            copy.Add(item.DeepClone());
                        }

                        normalized = copy;
                        return true;
                    }
                    return false;
                case "string[]":
                    if (value is JArray stringArray)
                    {
                        var copy = new JArray();
                        foreach (var item in stringArray)
                        {
                            if (item.Type != JTokenType.String)
                                return false;
                            copy.Add(item.DeepClone());
                        }

                        normalized = copy;
                        return true;
                    }
                    return false;
                case "byte_array":
                    if (value.Type != JTokenType.String)
                        return false;
                    try
                    {
                        var bytes = Convert.FromBase64String(value.Value<string>() ?? string.Empty);
                        normalized = JValue.CreateString(Convert.ToBase64String(bytes));
                        return true;
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                case "float64":
                    if (TryReadFloat64(value, out var float64))
                    {
                        normalized = new JValue(float64);
                        return true;
                    }
                    return false;
                case "float64_array":
                    if (value is JArray floatArray)
                    {
                        var copy = new JArray();
                        foreach (var item in floatArray)
                        {
                            if (!TryReadFloat64(item, out var itemValue))
                                return false;
                            copy.Add(new JValue(itemValue));
                        }

                        normalized = copy;
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static bool TryReadFloat64(JToken value, out double result)
        {
            result = 0d;
            if (value == null
                || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                return false;

            try
            {
                result = value.Value<double>();
                return !double.IsNaN(result) && !double.IsInfinity(result);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
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

        /// <summary>Return current values and explicit name-only markers for client-unset parameters.</summary>
        internal List<Parameter> GetWireParametersIncludingUnset(
            IReadOnlyList<string> names,
            ISet<string> unsetNames)
        {
            var result = GetWireParameters(names);
            if (unsetNames == null || unsetNames.Count == 0)
                return result;

            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in result)
                present.Add(parameter.Name);
            foreach (var name in unsetNames)
            {
                if (!string.IsNullOrEmpty(name) && present.Add(name))
                    result.Add(new Parameter { Name = name });
            }

            return result;
        }

        /// <summary>Remove all parameters.</summary>
        public void Clear()
        {
            ThrowIfMutationBlocked();
            lock (_lock)
            {
                _params.Clear();
                _clientUnsetRegistrations.Clear();
            }
        }

        /// <summary>Clear runtime-owned entries while the owning session is being retired.</summary>
        internal void ClearDuringCleanup()
        {
            lock (_lock)
            {
                _params.Clear();
                _clientUnsetRegistrations.Clear();
            }
        }

        private void ThrowIfMutationBlocked()
        {
            if (_mutationAllowed != null && !_mutationAllowed())
                throw new InvalidOperationException(
                    "Parameter store mutations are unavailable while session cleanup is pending.");
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

        private void InvokeChangedHandlers(string name, JToken value, string type)
        {
            var handlers = OnParameterChanged;
            if (handlers == null)
                return;

            foreach (Action<string, JToken, string> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(name, CloneValue(value), type);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Parameter change observer failed: {ex.Message}");
                }
            }
        }
    }
}
