// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: Unity-free lifecycle policies shared by camera publishers and sensors.

using System;
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

        /// <summary>Returns whether every readback owned by a generation has completed.</summary>
        public bool IsDrained(int generation)
            => !HasPending(generation);

        /// <summary>Forgets all tracked requests after the owning resources are fully drained.</summary>
        public void Clear()
        {
            lock (_gate)
                _pendingByGeneration.Clear();
        }
    }

    /// <summary>Applies a manager sensor-clock generation transition in a fixed order.</summary>
    internal static class SensorGenerationTransition
    {
        /// <summary>
        /// Retire queued work before resetting sensor epoch state and publishing the new generation.
        /// </summary>
        public static bool Apply(
            ref int observedGeneration,
            int currentGeneration,
            Action retireQueuedWork,
            Action resetEpoch)
        {
            if (observedGeneration == currentGeneration)
                return false;

            retireQueuedWork?.Invoke();
            resetEpoch?.Invoke();
            observedGeneration = currentGeneration;
            return true;
        }
    }

    /// <summary>Chooses the actual image-source authority over a configured fallback.</summary>
    internal static class CameraInfoAuthorityPolicy
    {
        /// <summary>Returns the image source whenever one is present.</summary>
        public static T Select<T>(bool hasImageSource, T imageSource, T configuredSource)
            => hasImageSource ? imageSource : configuredSource;
    }

    /// <summary>Chooses the actual sensor transform over a publisher fallback.</summary>
    internal static class SensorTransformAuthorityPolicy
    {
        /// <summary>Returns a valid sensor transform before the publisher transform.</summary>
        public static T Select<T>(T sensorTransform, T publisherTransform, Func<T, bool> isValid)
            => isValid != null && isValid(sensorTransform) ? sensorTransform : publisherTransform;
    }

    /// <summary>Rejects callbacks that belong to a retired camera capture generation.</summary>
    internal static class CameraCaptureGenerationPolicy
    {
        /// <summary>Accepts only callbacks from a live component and current generation.</summary>
        public static bool Accepts(bool destroyed, bool active, int callbackGeneration, int currentGeneration)
            => !destroyed && active && callbackGeneration == currentGeneration;
    }

    /// <summary>Decides when an orthographic camera is incompatible with CameraInfo output.</summary>
    internal static class CameraInfoProjectionPolicy
    {
        /// <summary>Suppresses output when the published image cannot be represented by pinhole CameraInfo.</summary>
        public static bool ShouldSuppressOrthographic(
            bool hasImageSourceCamera,
            bool imageSourceIsOrthographic,
            bool autoFromCamera,
            bool fallbackSourceIsOrthographic)
        {
            if (hasImageSourceCamera)
                return imageSourceIsOrthographic;

            return autoFromCamera && fallbackSourceIsOrthographic;
        }
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
