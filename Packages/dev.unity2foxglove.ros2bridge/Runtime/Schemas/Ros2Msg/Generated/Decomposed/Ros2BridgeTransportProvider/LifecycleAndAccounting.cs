// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Ros2Msg/Generated
// Purpose: Manager-local FoxRun transport Provider for the ROS 2 sidecar.

using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Camera;
using Unity.FoxgloveSDK.Schemas.PointCloud;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace Unity2Foxglove.Ros2Bridge
{

#if UNITY_5_3_OR_NEWER
    public sealed partial class Ros2BridgeTransportProvider
    {
        private void ReleaseSession(ulong generation)
        {
            if (_activeGeneration != generation)
                return;
            ClearGeneratedSources();
            _activeSession = null;
            _activeGeneration = 0;
        }

        private void SynchronizeGeneratedSources(Session session)
        {
            var seen = new HashSet<
                IFoxRunBridgeGeneratedSubscribeSource>();
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var index = 0; index < behaviours.Length; index++)
            {
                if (!(behaviours[index]
                      is IFoxRunBridgeGeneratedSubscribeSource source))
                {
                    continue;
                }
                seen.Add(source);
                if (_generatedSources.ContainsKey(source))
                    continue;
                GeneratedSourceRegistration registration;
                string reason;
                var registered = false;
                try
                {
                    registered = GeneratedSourceRegistration.TryCreate(
                        session,
                        source,
                        out registration,
                        out reason);
                }
                catch (Exception exception)
                {
                    registration = null;
                    reason = Bound(exception.Message);
                }
                if (registered)
                {
                    _generatedSources.Add(source, registration);
                    _generatedSourceFailures.Remove(source);
                }
                else if (IsHardGeneratedSourceFailure(reason))
                {
                    var bounded = Bound(reason);
                    if (!_generatedSourceFailures.TryGetValue(
                            source,
                            out var previous)
                        || !string.Equals(
                            previous,
                            bounded,
                            StringComparison.Ordinal))
                    {
                        _generatedSourceFailures[source] = bounded;
                        Debug.LogWarning(
                            "[FoxRun] ROS2 Bridge generated subscription rejected: "
                            + bounded);
                    }
                }
            }

            var stale = new List<
                IFoxRunBridgeGeneratedSubscribeSource>();
            foreach (var pair in _generatedSources)
            {
                if (!seen.Contains(pair.Key)
                    || pair.Key is MonoBehaviour behaviour
                    && (behaviour == null
                        || !behaviour.isActiveAndEnabled))
                {
                    stale.Add(pair.Key);
                }
            }
            for (var index = 0; index < stale.Count; index++)
            {
                var source = stale[index];
                if (_generatedSources.TryGetValue(
                        source,
                        out var registration))
                {
                    _generatedSources.Remove(source);
                    _generatedSourceFailures.Remove(source);
                    var cleanupError = Ros2BridgeCleanup.RunAll(
                        1,
                        _ => registration.Dispose());
                    if (cleanupError != null)
                        LogGeneratedSourceCleanupFailure(cleanupError);
                }
            }
            if (_generatedSourceFailures.Count != 0)
            {
                var staleFailures = new List<
                    IFoxRunBridgeGeneratedSubscribeSource>();
                foreach (var source in _generatedSourceFailures.Keys)
                {
                    if (!seen.Contains(source)
                        || source is MonoBehaviour behaviour
                        && (behaviour == null
                            || !behaviour.isActiveAndEnabled))
                    {
                        staleFailures.Add(source);
                    }
                }
                for (var index = 0;
                     index < staleFailures.Count;
                     index++)
                {
                    _generatedSourceFailures.Remove(
                        staleFailures[index]);
                }
            }
        }

        private void ClearGeneratedSources()
        {
            var registrations = new List<GeneratedSourceRegistration>(
                _generatedSources.Values);
            _generatedSources.Clear();
            _generatedSourceFailures.Clear();
            var cleanupError = Ros2BridgeCleanup.RunAll(
                registrations.Count,
                index => registrations[index].Dispose(),
                reverse: true);
            if (cleanupError != null)
                LogGeneratedSourceCleanupFailure(cleanupError);
        }

        private static void LogGeneratedSourceCleanupFailure(
            Exception exception)
            => Debug.LogWarning(
                "[FoxRun] ROS2 Bridge generated subscription cleanup failed: "
                + Bound(exception?.Message));

        private static bool IsHardGeneratedSourceFailure(string reason)
            => !string.IsNullOrWhiteSpace(reason)
               && reason.IndexOf(
                   "not ready",
                   StringComparison.OrdinalIgnoreCase) < 0
               && reason.IndexOf(
                   "unavailable",
                   StringComparison.OrdinalIgnoreCase) < 0;

    }
#endif
}
