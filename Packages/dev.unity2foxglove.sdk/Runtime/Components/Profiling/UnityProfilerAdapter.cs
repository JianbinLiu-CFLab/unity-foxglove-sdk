// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Profiling
// Purpose: Bridges optional SDK profiling hooks to Unity Profiler markers.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Core;
#if UNITY_2020_3_OR_NEWER
using Unity.Profiling;
#else
using ProfilerMarker = Unity.FoxgloveSDK.Components.FallbackProfilerMarker;
#endif

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Unity-backed profiler implementation used only when profiling is enabled on <see cref="FoxgloveManager"/>.
    /// </summary>
    /// <remarks>
    /// Sample names must be a bounded static set because Unity profiler marker
    /// names are cached for the process lifetime.
    /// </remarks>
    public sealed class UnityProfilerAdapter : IFoxgloveProfiler
    {
        public static readonly UnityProfilerAdapter Instance = new UnityProfilerAdapter();

        private readonly ConcurrentDictionary<string, ProfilerMarker> _markers = new ConcurrentDictionary<string, ProfilerMarker>();
        private readonly ConcurrentBag<ProfilerScope> _scopes = new ConcurrentBag<ProfilerScope>();

        [ThreadStatic]
        private static Stack<ProfilerMarker> _activeMarkers;

        private UnityProfilerAdapter()
        {
        }

        public IDisposable Sample(string name)
        {
            var marker = GetMarker(name);
            marker.Begin();
            if (!_scopes.TryTake(out var scope))
            {
                scope = new ProfilerScope(this);
            }

            scope.Reset(marker);
            return scope;
        }

        public void BeginSample(string name)
        {
            var marker = GetMarker(name);
            marker.Begin();

            if (_activeMarkers == null)
            {
                _activeMarkers = new Stack<ProfilerMarker>();
            }

            _activeMarkers.Push(marker);
        }

        public void EndSample()
        {
            if (_activeMarkers == null || _activeMarkers.Count == 0)
            {
                return;
            }

            var marker = _activeMarkers.Pop();
            marker.End();
        }

        private ProfilerMarker GetMarker(string name)
        {
            var markerName = string.IsNullOrWhiteSpace(name) ? "Foxglove.Unnamed" : name;
            return _markers.GetOrAdd(markerName, static value => new ProfilerMarker(value));
        }

        private sealed class ProfilerScope : IDisposable
        {
            private readonly UnityProfilerAdapter _owner;
            private ProfilerMarker _marker;
            private bool _active;

            public ProfilerScope(UnityProfilerAdapter owner)
            {
                _owner = owner;
            }

            public void Reset(ProfilerMarker marker)
            {
                _marker = marker;
                _active = true;
            }

            public void Dispose()
            {
                if (!_active)
                {
                    return;
                }

                _active = false;
                _marker.End();
                _owner._scopes.Add(this);
            }
        }
    }

#if !UNITY_2020_3_OR_NEWER
    internal readonly struct FallbackProfilerMarker
    {
        public FallbackProfilerMarker(string name)
        {
        }

        public void Begin()
        {
        }

        public void End()
        {
        }
    }
#endif
}
