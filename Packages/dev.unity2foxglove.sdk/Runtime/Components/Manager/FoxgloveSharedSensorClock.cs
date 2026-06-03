// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Maintains the manager-level sensor clock epoch shared by LiDAR, IMU, and camera publishers.
    /// </summary>
    internal sealed class FoxgloveSharedSensorClock
    {
        private const ulong NanosPerSecond = 1_000_000_000UL;

        private bool _initialized;
        private ulong _epochUnixNs;
        private double _epochPhysSeconds;

        /// <summary>
        /// Converts a Unity physics timestamp to a monotonic Unix nanosecond timestamp.
        /// </summary>
        public ulong GetUnixTime(double physicsTimeSeconds, ulong nowNs)
        {
            if (!_initialized)
            {
                _initialized = true;
                _epochPhysSeconds = physicsTimeSeconds;
                _epochUnixNs = nowNs;
            }

            var deltaSeconds = physicsTimeSeconds - _epochPhysSeconds;
            if (deltaSeconds <= 0d)
                return _epochUnixNs;

            return checked(_epochUnixNs + (ulong)System.Math.Round(deltaSeconds * NanosPerSecond));
        }

        /// <summary>Clears the epoch so the next sample re-anchors the clock.</summary>
        public void Reset()
        {
            _initialized = false;
            _epochUnixNs = 0UL;
            _epochPhysSeconds = 0d;
        }
    }
}
