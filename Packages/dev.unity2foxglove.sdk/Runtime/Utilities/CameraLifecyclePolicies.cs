// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free lifecycle policies shared by camera publishers and sensors.

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Util
{
    /// <summary>Tracks asynchronous camera readbacks independently for each capture generation.</summary>
    internal sealed class CameraReadbackGenerationTracker
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, int> _pendingByGeneration = new Dictionary<int, int>();

        /// <summary>Records one readback issued for a capture generation.</summary>
        public void Register(int generation)
        {
            lock (_gate)
            {
                _pendingByGeneration.TryGetValue(generation, out var pending);
                _pendingByGeneration[generation] = pending + 1;
            }
        }

        /// <summary>Completes one readback and returns whether that generation is drained.</summary>
        public bool Complete(int generation)
        {
            lock (_gate)
            {
                if (!_pendingByGeneration.TryGetValue(generation, out var pending))
                    return true;

                if (pending <= 1)
                {
                    _pendingByGeneration.Remove(generation);
                    return true;
                }

                _pendingByGeneration[generation] = pending - 1;
                return false;
            }
        }

        /// <summary>Returns whether a generation still owns an unfinished readback.</summary>
        public bool HasPending(int generation)
        {
            lock (_gate)
                return _pendingByGeneration.ContainsKey(generation);
        }

        /// <summary>Forgets all tracked requests after the owning resources are fully drained.</summary>
        public void Clear()
        {
            lock (_gate)
                _pendingByGeneration.Clear();
        }
    }

    /// <summary>Decides when an orthographic camera is incompatible with CameraInfo output.</summary>
    internal static class CameraInfoProjectionPolicy
    {
        /// <summary>Suppresses output for every orthographic source because CameraInfo is pinhole-based.</summary>
        public static bool ShouldSuppressOrthographic(bool hasSourceCamera, bool isOrthographic)
            => hasSourceCamera && isOrthographic;
    }

    /// <summary>Emits a Play Mode configuration warning at most once per enable cycle.</summary>
    internal sealed class PlayModeConfigurationWarningGate
    {
        private bool _issued;

        /// <summary>Resets the warning state for a newly enabled component.</summary>
        public void Reset() => _issued = false;

        /// <summary>Returns true only for the first warning-worthy validation in a cycle.</summary>
        public bool TryIssue(bool condition)
        {
            if (!condition || _issued)
                return false;

            _issued = true;
            return true;
        }
    }
}
