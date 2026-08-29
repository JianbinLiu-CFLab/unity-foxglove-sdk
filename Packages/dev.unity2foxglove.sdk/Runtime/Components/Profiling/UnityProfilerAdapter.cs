// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Profiling
// Purpose: Bridges optional SDK profiling hooks to Unity Profiler markers.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// names are cached for the process lifetime. Scopes returned by
    /// <see cref="Sample"/> must be disposed on the same thread that created
    /// them because Unity profiler marker begin/end pairs are thread-affine.
    /// </remarks>
    public sealed class UnityProfilerAdapter : IFoxgloveProfiler
    {
        public static readonly UnityProfilerAdapter Instance = new UnityProfilerAdapter();

        internal const int MaximumMarkerCount = 64;
        private const string OverflowMarkerName = "Foxglove.DynamicOverflow";
        private readonly ConcurrentDictionary<string, ProfilerMarker> _markers = new ConcurrentDictionary<string, ProfilerMarker>();
        private readonly ConcurrentBag<ProfilerScope> _scopes = new ConcurrentBag<ProfilerScope>();
        private readonly object _markerGate = new object();
        private ProfilerMarker _overflowMarker;
        private bool _overflowMarkerInitialized;

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
            if (_markers.TryGetValue(markerName, out var marker))
                return marker;

            lock (_markerGate)
            {
                if (_markers.TryGetValue(markerName, out marker))
                    return marker;

                // Reserve one slot for a stable overflow marker. Unity keeps
                // marker names for the process lifetime, so unbounded caller
                // supplied names must never grow this cache without limit.
                if (_markers.Count >= MaximumMarkerCount - 1)
                    return GetOverflowMarkerLocked();

                marker = new ProfilerMarker(markerName);
                _markers.TryAdd(markerName, marker);
                return marker;
            }
        }

        private ProfilerMarker GetOverflowMarkerLocked()
        {
            if (!_overflowMarkerInitialized)
            {
                _overflowMarker = new ProfilerMarker(OverflowMarkerName);
                _overflowMarkerInitialized = true;
            }

            if (_markers.Count < MaximumMarkerCount)
                _markers.TryAdd(OverflowMarkerName, _overflowMarker);

            return _overflowMarker;
        }

        private sealed class ProfilerScope : IDisposable
        {
            private readonly UnityProfilerAdapter _owner;
            private ProfilerMarker _marker;
            private int _threadId;
            private bool _active;

            public ProfilerScope(UnityProfilerAdapter owner)
            {
                _owner = owner;
            }

            public void Reset(ProfilerMarker marker)
            {
                _marker = marker;
                _threadId = Environment.CurrentManagedThreadId;
                _active = true;
            }

            public void Dispose()
            {
                if (!_active)
                {
                    return;
                }

                _active = false;
                Debug.Assert(
                    _threadId == Environment.CurrentManagedThreadId,
                    "Unity profiler scopes must be disposed on the same thread that created them.");
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
