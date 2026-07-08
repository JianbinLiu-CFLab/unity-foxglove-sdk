// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Allocation-free reflection property lookup cache for replay adapters.

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Unity.FoxgloveSDK.Core
{
    internal static class ReplayPropertyCache
    {
        private static readonly ConcurrentDictionary<PropertyCacheKey, PropertyInfo> Cache = new();

#if UNITY_5_3_OR_NEWER
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        private static void ResetForSubsystemRegistration()
        {
            Cache.Clear();
        }

        internal static PropertyInfo Resolve(Type type, string propertyName, BindingFlags bindingFlags)
        {
            var key = new PropertyCacheKey(type, propertyName, bindingFlags);
            return Cache.GetOrAdd(key, static cachedKey => cachedKey.Resolve());
        }

        private readonly struct PropertyCacheKey : IEquatable<PropertyCacheKey>
        {
            private readonly Type _type;
            private readonly string _propertyName;
            private readonly BindingFlags _bindingFlags;

            public PropertyCacheKey(Type type, string propertyName, BindingFlags bindingFlags)
            {
                _type = type;
                _propertyName = propertyName;
                _bindingFlags = bindingFlags;
            }

            public bool Equals(PropertyCacheKey other)
                => _type == other._type
                   && _bindingFlags == other._bindingFlags
                   && string.Equals(_propertyName, other._propertyName, StringComparison.Ordinal);

            public PropertyInfo Resolve()
                => _type?.GetProperty(_propertyName, _bindingFlags);

            public override bool Equals(object obj)
                => obj is PropertyCacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _type != null ? _type.GetHashCode() : 0;
                    hash = (hash * 397) ^ (int)_bindingFlags;
                    hash = (hash * 397) ^ (_propertyName != null ? StringComparer.Ordinal.GetHashCode(_propertyName) : 0);
                    return hash;
                }
            }
        }
    }
}
