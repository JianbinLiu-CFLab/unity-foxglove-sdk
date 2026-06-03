// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Tracks the camera output mode locked for a Play Mode session.
    /// </summary>
    internal sealed class CameraOutputModeRuntimeLock
    {
        private CameraOutputMode _lockedMode;
        private bool _isLocked;
        private bool _warnedRuntimeOutputModeSwitch;

        public void Lock(CameraOutputMode mode)
        {
            _lockedMode = mode;
            _isLocked = true;
            _warnedRuntimeOutputModeSwitch = false;
        }

        public void Unlock()
        {
            _isLocked = false;
            _warnedRuntimeOutputModeSwitch = false;
        }

        public CameraOutputMode Resolve(
            CameraOutputMode configuredMode,
            bool isPlaying,
            out string warning)
        {
            warning = null;
            if (!isPlaying || !_isLocked)
                return configuredMode;

            if (configuredMode != _lockedMode && !_warnedRuntimeOutputModeSwitch)
            {
                _warnedRuntimeOutputModeSwitch = true;
                var active = CameraVideoOutputProfile.ForMode(_lockedMode).DisplayName;
                var requested = CameraVideoOutputProfile.ForMode(configuredMode).DisplayName;
                warning =
                    "[Foxglove] Camera output mode changes during Play Mode are ignored to avoid stale channel advertisements. " +
                    $"Restart Play Mode to switch from {active} to {requested}.";
            }

            return _lockedMode;
        }
    }
}
