// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Main-thread local-origin ownership for custom native P&S endpoints.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Maps one generated source/contract endpoint to its currently active
    /// publisher origin. The subscription side reads this on Unity's main
    /// thread after the callback has made an owned envelope copy, so no native
    /// callback ever touches this registry.
    /// </summary>
    internal static class FoxRunRos2CustomOriginRegistry
    {
        private static readonly Dictionary<string, string> s_activeOrigins =
            new Dictionary<string, string>(StringComparer.Ordinal);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_activeOrigins.Clear();
        }

        internal static string BeginPublisher(string endpointIdentity)
        {
            if (String.IsNullOrWhiteSpace(endpointIdentity))
                throw new ArgumentException("Custom ROS2 endpoint identity is required.", nameof(endpointIdentity));

            var origin = "unity2foxglove-" + Guid.NewGuid().ToString("N");
            s_activeOrigins[endpointIdentity] = origin;
            return origin;
        }

        internal static void EndPublisher(string endpointIdentity, string origin)
        {
            if (String.IsNullOrWhiteSpace(endpointIdentity)
                || String.IsNullOrWhiteSpace(origin))
                return;
            if (s_activeOrigins.TryGetValue(endpointIdentity, out var active)
                && String.Equals(active, origin, StringComparison.Ordinal))
                s_activeOrigins.Remove(endpointIdentity);
        }

        internal static bool IsCurrentOrigin(string endpointIdentity, string origin)
            => !String.IsNullOrWhiteSpace(endpointIdentity)
               && !String.IsNullOrWhiteSpace(origin)
               && s_activeOrigins.TryGetValue(endpointIdentity, out var active)
               && String.Equals(active, origin, StringComparison.Ordinal);

        internal static void ResetForTests()
        {
            s_activeOrigins.Clear();
        }
    }
}
#endif
