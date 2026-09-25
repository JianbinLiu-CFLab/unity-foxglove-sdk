// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Tracks pending replay panel and scene snapshot requests for
// FoxgloveRuntime without owning replay publication.

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Tracks pending replay snapshot requests and exposes atomic consume operations
    /// for the runtime tick loop.
    /// </summary>
    internal sealed class ReplaySnapshotStateMachine
    {
        private readonly object _panelSnapshotLock = new();
        private readonly object _sceneSnapshotLock = new();

        private bool _panelSnapshotPending;
        private ulong _panelSnapshotTimeNs;
        private ulong _panelSnapshotReadyWallNs;
        private uint? _panelSnapshotClientId;
        private bool _sceneSnapshotPending;
        private ulong _sceneSnapshotTimeNs;

        public void RequestPanelSnapshot(ulong timeNs, ulong readyWallNs, uint? clientId = null)
        {
            lock (_panelSnapshotLock)
            {
                _panelSnapshotTimeNs = timeNs;
                _panelSnapshotReadyWallNs = readyWallNs;
                _panelSnapshotClientId = clientId;
                _panelSnapshotPending = true;
            }
        }

        public bool TryConsumePanelSnapshot(
            ulong wallNowNs,
            out ulong timeNs,
            out uint? clientId)
        {
            lock (_panelSnapshotLock)
            {
                timeNs = _panelSnapshotTimeNs;
                clientId = _panelSnapshotClientId;
                if (!_panelSnapshotPending)
                    return false;
                if (wallNowNs < _panelSnapshotReadyWallNs)
                    return false;
                _panelSnapshotPending = false;
                _panelSnapshotClientId = null;
                return true;
            }
        }

        public void RequestSceneSnapshot(ulong timeNs)
        {
            lock (_sceneSnapshotLock)
            {
                _sceneSnapshotTimeNs = timeNs;
                _sceneSnapshotPending = true;
            }
        }

        public bool TryConsumeSceneSnapshot(out ulong timeNs)
        {
            lock (_sceneSnapshotLock)
            {
                timeNs = _sceneSnapshotTimeNs;
                if (!_sceneSnapshotPending)
                    return false;
                _sceneSnapshotPending = false;
                return true;
            }
        }

        public void ClearPanelSnapshot()
        {
            lock (_panelSnapshotLock)
            {
                _panelSnapshotPending = false;
                _panelSnapshotTimeNs = 0;
                _panelSnapshotReadyWallNs = 0;
                _panelSnapshotClientId = null;
            }
        }

        public void ClearSceneSnapshot()
        {
            lock (_sceneSnapshotLock)
            {
                _sceneSnapshotPending = false;
                _sceneSnapshotTimeNs = 0;
            }
        }

        public void Clear()
        {
            ClearPanelSnapshot();
            ClearSceneSnapshot();
        }
    }
}
