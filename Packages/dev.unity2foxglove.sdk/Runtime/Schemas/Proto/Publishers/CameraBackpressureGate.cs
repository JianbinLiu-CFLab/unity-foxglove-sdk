// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Owns camera backpressure runtime state outside the publisher.

using Unity.FoxgloveSDK.Util;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Tracks transport-drop baselines, cooldown timing, and bounded skip-warning
    /// emission for camera capture backpressure.
    /// </summary>
    internal sealed class CameraBackpressureGate
    {
        private const int MaxSkipWarnings = 10;
        private const string CooldownWarning = "[Foxglove] Camera capture skipped by backpressure cooldown.";

        private long _lastDropCount;
        private double _cooldownUntilSec;
        private int _skipWarningCount;
        private bool _baselineInitialized;

        public bool AllowCapture(
            bool enabled,
            bool statsSupported,
            long totalDroppedDataFrames,
            double currentTimeSec,
            float cooldownSeconds,
            bool logSkips,
            out string warning)
        {
            warning = null;
            if (!enabled)
            {
                _baselineInitialized = false;
                return true;
            }

            var currentDrop = statsSupported ? totalDroppedDataFrames : _lastDropCount;
            if (statsSupported && !_baselineInitialized)
            {
                _lastDropCount = currentDrop;
                _cooldownUntilSec = currentTimeSec;
                _baselineInitialized = true;
            }

            var result = CameraBackpressurePolicy.Evaluate(
                enabled: true,
                currentTimeSec: currentTimeSec,
                cooldownSec: cooldownSeconds,
                previousDropCount: _lastDropCount,
                currentDropCount: currentDrop,
                currentCooldownUntilSec: _cooldownUntilSec);

            _lastDropCount = result.NextDropCount;
            _cooldownUntilSec = result.NextCooldownUntilSec;

            if (result.AllowCapture)
                return true;

            TryRecordSkipWarning(logSkips, CooldownWarning, out warning);
            return false;
        }

        public void Reset()
        {
            _lastDropCount = 0;
            _cooldownUntilSec = 0;
            _skipWarningCount = 0;
            _baselineInitialized = false;
        }

        public void ResetSkipLogCount()
            => _skipWarningCount = 0;

        public bool TryRecordSkipWarning(bool logSkips, string message, out string warning)
        {
            warning = null;
            if (!logSkips || _skipWarningCount >= MaxSkipWarnings)
                return false;

            _skipWarningCount++;
            warning = message;
            return true;
        }
    }
}
