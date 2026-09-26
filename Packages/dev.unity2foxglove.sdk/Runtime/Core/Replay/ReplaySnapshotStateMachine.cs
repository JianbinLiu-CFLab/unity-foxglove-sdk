// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Tracks pending replay panel and scene snapshot requests for
// FoxgloveRuntime without owning replay publication.

using System.Collections.Generic;

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

        private const int MaxPendingTargetedPanelSnapshots = 64;
        private readonly Dictionary<uint, PendingPanelSnapshot> _targetedPanelSnapshots = new();
        private bool _globalPanelSnapshotPending;
        private ulong _globalPanelSnapshotTimeNs;
        private ulong _globalPanelSnapshotReadyWallNs;
        private long _nextTargetedPanelSnapshotSequence;
        private bool _sceneSnapshotPending;
        private ulong _sceneSnapshotTimeNs;

        public void RequestPanelSnapshot(ulong timeNs, ulong readyWallNs, uint? clientId = null)
        {
            lock (_panelSnapshotLock)
            {
                if (!clientId.HasValue)
                {
                    _targetedPanelSnapshots.Clear();
                    _globalPanelSnapshotTimeNs = timeNs;
                    _globalPanelSnapshotReadyWallNs = readyWallNs;
                    _globalPanelSnapshotPending = true;
                    return;
                }

                // A global seek is authoritative for the current timeline. A
                // targeted request arriving during its debounce must not replace it.
                if (_globalPanelSnapshotPending)
                    return;

                if (!_targetedPanelSnapshots.ContainsKey(clientId.Value)
                    && _targetedPanelSnapshots.Count >= MaxPendingTargetedPanelSnapshots)
                {
                    RemoveOldestTargetedSnapshot();
                }

                _targetedPanelSnapshots[clientId.Value] = new PendingPanelSnapshot(
                    timeNs,
                    readyWallNs,
                    ++_nextTargetedPanelSnapshotSequence);
            }
        }

        public bool TryConsumePanelSnapshot(
            ulong wallNowNs,
            out ulong timeNs,
            out uint? clientId)
        {
            lock (_panelSnapshotLock)
            {
                timeNs = 0;
                clientId = null;
                if (_globalPanelSnapshotPending)
                {
                    timeNs = _globalPanelSnapshotTimeNs;
                    if (wallNowNs < _globalPanelSnapshotReadyWallNs)
                        return false;

                    _globalPanelSnapshotPending = false;
                    return true;
                }

                var selectedClientId = 0u;
                PendingPanelSnapshot selected = default;
                var hasSelected = false;
                foreach (var pair in _targetedPanelSnapshots)
                {
                    if (wallNowNs < pair.Value.ReadyWallNs)
                        continue;
                    if (!hasSelected || pair.Value.Sequence < selected.Sequence)
                    {
                        selectedClientId = pair.Key;
                        selected = pair.Value;
                        hasSelected = true;
                    }
                }

                if (!hasSelected)
                    return false;

                _targetedPanelSnapshots.Remove(selectedClientId);
                timeNs = selected.TimeNs;
                clientId = selectedClientId;
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
                _globalPanelSnapshotPending = false;
                _globalPanelSnapshotTimeNs = 0;
                _globalPanelSnapshotReadyWallNs = 0;
                _targetedPanelSnapshots.Clear();
            }
        }

        public void ClearPanelSnapshot(uint clientId)
        {
            lock (_panelSnapshotLock)
            {
                _targetedPanelSnapshots.Remove(clientId);
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

        private void RemoveOldestTargetedSnapshot()
        {
            var oldestClientId = 0u;
            var oldestSequence = long.MaxValue;
            foreach (var pair in _targetedPanelSnapshots)
            {
                if (pair.Value.Sequence >= oldestSequence)
                    continue;
                oldestClientId = pair.Key;
                oldestSequence = pair.Value.Sequence;
            }

            if (oldestSequence != long.MaxValue)
                _targetedPanelSnapshots.Remove(oldestClientId);
        }

        private readonly struct PendingPanelSnapshot
        {
            internal PendingPanelSnapshot(ulong timeNs, ulong readyWallNs, long sequence)
            {
                TimeNs = timeNs;
                ReadyWallNs = readyWallNs;
                Sequence = sequence;
            }

            internal ulong TimeNs { get; }
            internal ulong ReadyWallNs { get; }
            internal long Sequence { get; }
        }
    }
}
