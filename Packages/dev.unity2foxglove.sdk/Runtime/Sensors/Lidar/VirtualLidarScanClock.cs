// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Sensors/Lidar

using System;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Maintains the stable wall-clock epoch used to timestamp a virtual LiDAR scan.
    /// </summary>
    internal sealed class VirtualLidarScanClock
    {
        private bool _initialized;
        private ulong _epochUnixNs;
        private double _epochPhysSeconds;

        /// <summary>Whether the epoch has been initialized for the current component lifetime.</summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// Initialize the epoch once and return whether the clock changed state.
        /// </summary>
        public bool EnsureInitialized(double physNow, Func<double, ulong> resolveUnixNs)
        {
            if (_initialized)
                return false;

            _initialized = true;
            _epochPhysSeconds = physNow;
            _epochUnixNs = resolveUnixNs == null
                ? FoxgloveTimeUtil.NowUnixTimeNs()
                : resolveUnixNs(physNow);
            return true;
        }

        /// <summary>Clear the epoch so it can be re-established after manager resolution.</summary>
        public void Reset()
        {
            _initialized = false;
            _epochUnixNs = 0UL;
            _epochPhysSeconds = 0d;
        }

        /// <summary>
        /// Convert a physics-time scan start into the clock's Unix nanosecond epoch.
        /// </summary>
        public ulong GetScanStartUnixNs(double scanStartPhysSeconds)
        {
            if (!_initialized)
                return FoxgloveTimeUtil.NowUnixTimeNs();

            var deltaSeconds = scanStartPhysSeconds - _epochPhysSeconds;
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
                return _epochUnixNs;

            var deltaNanoseconds = Math.Round(deltaSeconds * 1e9);
            if (double.IsNaN(deltaNanoseconds) || double.IsInfinity(deltaNanoseconds) || deltaNanoseconds <= 0d)
                return _epochUnixNs;

            var maxDelta = ulong.MaxValue - _epochUnixNs;
            if (deltaNanoseconds >= maxDelta)
                return ulong.MaxValue;

            return _epochUnixNs + (ulong)deltaNanoseconds;
        }
    }
}
