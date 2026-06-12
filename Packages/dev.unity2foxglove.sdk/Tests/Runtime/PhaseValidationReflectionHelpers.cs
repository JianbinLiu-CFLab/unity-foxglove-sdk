// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared reflection helpers for runtime validation phases.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class PhaseValidationReflectionHelpers
    {
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        public static Type FindType(string fullName)
        {
            lock (TypeCache)
            {
                if (TypeCache.TryGetValue(fullName, out var cached))
                    return cached;
            }

            Type resolved = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolved = assembly.GetType(fullName, throwOnError: false);
                if (resolved != null)
                    break;
            }

            lock (TypeCache)
            {
                if (!TypeCache.ContainsKey(fullName))
                    TypeCache.Add(fullName, resolved);
                return TypeCache[fullName];
            }
        }
    }
}
