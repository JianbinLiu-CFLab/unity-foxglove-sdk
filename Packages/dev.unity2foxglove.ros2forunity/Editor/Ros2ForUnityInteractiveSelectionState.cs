// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Keep interactive package-selection state deterministic across reloads.

using System;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal enum Ros2ForUnityInteractiveSelectionKind
    {
        Runtime,
        CustomTypesupport
    }

    internal enum Ros2ForUnitySelectionLifecycleStatus
    {
        Pending,
        TimedOut,
        Failed,
        Completed,
        Cancelled
    }

    internal static class Ros2ForUnitySelectionLifecycleDefaults
    {
        internal const double ResolveTimeoutSeconds = 300.0;
        internal static TimeSpan ResolveTimeout => TimeSpan.FromSeconds(ResolveTimeoutSeconds);
    }

    /// <summary>
    /// Pure state and persistence boundary for an interactive Package Manager
    /// selection. Unity callbacks remain in the coordinator; this type owns
    /// generation fencing and bounded lifecycle transitions.
    /// </summary>
    internal sealed class Ros2ForUnityInteractiveSelectionState
    {
        internal sealed class Snapshot
        {
            internal long Generation { get; set; }
            internal string ProjectDirectory { get; set; }
            internal string RuntimePackage { get; set; }
            internal string AddOnPackage { get; set; }
            internal string OriginalManifest { get; set; }
            internal Ros2ForUnityInteractiveSelectionKind Kind { get; set; }
            internal DateTime DeadlineUtc { get; set; }
        }

        private bool _closed;

        private Ros2ForUnityInteractiveSelectionState(
            long generation,
            string projectDirectory,
            string runtimePackage,
            string addOnPackage,
            string originalManifest,
            Ros2ForUnityInteractiveSelectionKind kind,
            DateTime deadlineUtc)
        {
            Generation = generation;
            ProjectDirectory = projectDirectory ?? string.Empty;
            RuntimePackage = runtimePackage ?? string.Empty;
            AddOnPackage = addOnPackage ?? string.Empty;
            OriginalManifest = originalManifest ?? string.Empty;
            Kind = kind;
            DeadlineUtc = deadlineUtc;
        }

        internal long Generation { get; }
        internal string ProjectDirectory { get; }
        internal string RuntimePackage { get; }
        internal string AddOnPackage { get; }
        internal string OriginalManifest { get; }
        internal Ros2ForUnityInteractiveSelectionKind Kind { get; }
        internal DateTime DeadlineUtc { get; }
        internal bool IsPending => !_closed;

        internal static Ros2ForUnityInteractiveSelectionState Begin(
            long generation,
            string projectDirectory,
            string runtimePackage,
            string addOnPackage,
            string originalManifest,
            Ros2ForUnityInteractiveSelectionKind kind,
            DateTime nowUtc,
            TimeSpan timeout)
        {
            if (generation <= 0
                || string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(runtimePackage)
                || string.IsNullOrEmpty(originalManifest)
                || timeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    "The interactive ROS2 For Unity selection transaction is incomplete.");
            }

            if (nowUtc.Kind != DateTimeKind.Utc)
                nowUtc = nowUtc.ToUniversalTime();

            return new Ros2ForUnityInteractiveSelectionState(
                generation,
                projectDirectory,
                runtimePackage,
                addOnPackage,
                originalManifest,
                kind,
                nowUtc.Add(timeout));
        }

        internal static bool TryRestore(
            Snapshot snapshot,
            DateTime nowUtc,
            out Ros2ForUnityInteractiveSelectionState state)
        {
            state = null;
            if (snapshot == null
                || snapshot.Generation <= 0
                || string.IsNullOrWhiteSpace(snapshot.ProjectDirectory)
                || string.IsNullOrWhiteSpace(snapshot.RuntimePackage)
                || string.IsNullOrEmpty(snapshot.OriginalManifest)
                || snapshot.DeadlineUtc.Kind != DateTimeKind.Utc)
            {
                return false;
            }

            state = new Ros2ForUnityInteractiveSelectionState(
                snapshot.Generation,
                snapshot.ProjectDirectory,
                snapshot.RuntimePackage,
                snapshot.AddOnPackage,
                snapshot.OriginalManifest,
                snapshot.Kind,
                snapshot.DeadlineUtc);
            return true;
        }

        internal Snapshot Capture()
            => new Snapshot
            {
                Generation = Generation,
                ProjectDirectory = ProjectDirectory,
                RuntimePackage = RuntimePackage,
                AddOnPackage = AddOnPackage,
                OriginalManifest = OriginalManifest,
                Kind = Kind,
                DeadlineUtc = DeadlineUtc
            };

        internal bool IsCurrent(long generation)
            => IsPending && generation == Generation;

        internal bool IsExpired(DateTime nowUtc)
            => IsPending && nowUtc.ToUniversalTime() >= DeadlineUtc;

        internal Ros2ForUnityInteractiveSelectionState CreateRetry(
            DateTime nowUtc,
            TimeSpan timeout)
        {
            return Begin(
                checked(Generation + 1),
                ProjectDirectory,
                RuntimePackage,
                AddOnPackage,
                OriginalManifest,
                Kind,
                nowUtc,
                timeout);
        }

        internal bool TryCancel(long generation)
            => TryComplete(generation);
        internal bool TryComplete(long generation)
        {
            if (!IsCurrent(generation))
                return false;

            _closed = true;
            return true;
        }

        internal bool TryFail(long generation)
            => TryComplete(generation);
    }

    /// <summary>
    /// Unity-free lifecycle core for interactive and batch package selection.
    /// The Unity adapter owns callbacks, persistence, manifest I/O, and UI.
    /// </summary>
    internal sealed class Ros2ForUnitySelectionLifecycle
    {
        private Ros2ForUnityInteractiveSelectionState _state;
        private Ros2ForUnitySelectionLifecycleStatus _status;

        internal Ros2ForUnityInteractiveSelectionState State => _state;
        internal Ros2ForUnitySelectionLifecycleStatus Status => _status;
        internal bool HasOwner
            => _state != null
                && _status != Ros2ForUnitySelectionLifecycleStatus.Completed
                && _status != Ros2ForUnitySelectionLifecycleStatus.Cancelled;

        internal void Begin(Ros2ForUnityInteractiveSelectionState state)
        {
            if (state == null || !state.IsPending)
                throw new InvalidOperationException("A live selection state is required.");
            if (HasOwner)
                throw new InvalidOperationException("A selection lifecycle already owns a transaction.");

            _state = state;
            _status = Ros2ForUnitySelectionLifecycleStatus.Pending;
        }

        internal void Restore(
            Ros2ForUnityInteractiveSelectionState state,
            DateTime nowUtc,
            Ros2ForUnitySelectionLifecycleStatus restoredStatus = Ros2ForUnitySelectionLifecycleStatus.Pending)
        {
            if (state == null || !state.IsPending)
                throw new InvalidOperationException("A live selection state is required.");
            if (restoredStatus == Ros2ForUnitySelectionLifecycleStatus.Completed
                || restoredStatus == Ros2ForUnitySelectionLifecycleStatus.Cancelled)
                throw new InvalidOperationException("A terminal selection status cannot own a transaction.");

            _state = state;
            _status = restoredStatus;
            if (_status == Ros2ForUnitySelectionLifecycleStatus.Pending
                && _state.IsExpired(nowUtc))
            {
                _status = Ros2ForUnitySelectionLifecycleStatus.TimedOut;
            }
        }

        internal bool MarkTimedOut(DateTime nowUtc)
        {
            if (!HasOwner
                || _status != Ros2ForUnitySelectionLifecycleStatus.Pending)
                return false;
            if (!_state.IsExpired(nowUtc))
                return false;

            _status = Ros2ForUnitySelectionLifecycleStatus.TimedOut;
            return true;
        }

        internal bool MarkFailed()
        {
            if (!HasOwner)
                return false;
            _status = Ros2ForUnitySelectionLifecycleStatus.Failed;
            return true;
        }

        internal bool Tick(
            DateTime nowUtc,
            Func<Ros2ForUnityInteractiveSelectionState, bool> isRegistered,
            Func<Ros2ForUnityInteractiveSelectionState, bool> isValid,
            Action<Ros2ForUnityInteractiveSelectionState> commit)
        {
            if (!HasOwner
                || _status != Ros2ForUnitySelectionLifecycleStatus.Pending)
                return false;

            if (MarkTimedOut(nowUtc))
                return false;
            if (!isRegistered(_state))
                return false;
            if (!isValid(_state))
            {
                _status = Ros2ForUnitySelectionLifecycleStatus.Failed;
                return false;
            }

            commit(_state);
            _status = Ros2ForUnitySelectionLifecycleStatus.Completed;
            _state.TryComplete(_state.Generation);
            return true;
        }

        internal bool TryComplete(long generation)
        {
            if (!HasOwner || _state == null || _state.Generation != generation)
                return false;
            if (!_state.TryComplete(generation))
                return false;

            _status = Ros2ForUnitySelectionLifecycleStatus.Completed;
            return true;
        }
        internal bool Retry(DateTime nowUtc, TimeSpan timeout, Action resolve)
        {
            if (!HasOwner
                || (_status != Ros2ForUnitySelectionLifecycleStatus.TimedOut
                    && _status != Ros2ForUnitySelectionLifecycleStatus.Failed))
                return false;

            var previous = _state;
            var retry = previous.CreateRetry(nowUtc, timeout);
            previous.TryCancel(previous.Generation);
            _state = retry;
            _status = Ros2ForUnitySelectionLifecycleStatus.Pending;
            try
            {
                resolve();
                return true;
            }
            catch
            {
                _status = Ros2ForUnitySelectionLifecycleStatus.Failed;
                throw;
            }
        }

        internal bool Cancel(Action rollback)
        {
            if (!HasOwner)
                return false;

            rollback();
            _state.TryCancel(_state.Generation);
            _status = Ros2ForUnitySelectionLifecycleStatus.Cancelled;
            return true;
        }

        internal bool Complete()
        {
            if (!HasOwner)
                return false;

            _state.TryComplete(_state.Generation);
            _status = Ros2ForUnitySelectionLifecycleStatus.Completed;
            return true;
        }
    }

    internal interface IRos2ForUnitySelectionBackend
    {
        bool IsRegistered(Ros2ForUnityInteractiveSelectionState state);
        bool IsValid(Ros2ForUnityInteractiveSelectionState state);
        void Commit(Ros2ForUnityInteractiveSelectionState state);
        void Resolve();
        void Rollback(Ros2ForUnityInteractiveSelectionState state);
    }

    /// <summary>
    /// Unity-free coordinator adapter used by the Editor callbacks and by
    /// lifecycle tests. It owns no Unity state; the backend supplies package
    /// registration, validation, resolve, commit, and rollback operations.
    /// </summary>
    internal sealed class Ros2ForUnityInteractiveSelectionCoordinatorCore
    {
        private readonly Ros2ForUnitySelectionLifecycle _lifecycle =
            new Ros2ForUnitySelectionLifecycle();

        internal Ros2ForUnityInteractiveSelectionState State
            => _lifecycle.State;
        internal Ros2ForUnitySelectionLifecycleStatus Status
            => _lifecycle.Status;
        internal bool HasOwner
            => _lifecycle.HasOwner;

        internal void Begin(Ros2ForUnityInteractiveSelectionState state)
            => _lifecycle.Begin(state);

        internal void Restore(
            Ros2ForUnityInteractiveSelectionState state,
            DateTime nowUtc,
            Ros2ForUnitySelectionLifecycleStatus status =
                Ros2ForUnitySelectionLifecycleStatus.Pending)
            => _lifecycle.Restore(state, nowUtc, status);

        internal bool MarkTimedOut(DateTime nowUtc)
            => _lifecycle.MarkTimedOut(nowUtc);

        internal bool Tick(
            DateTime nowUtc,
            IRos2ForUnitySelectionBackend backend,
            Action<Exception> failureSink)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            try
            {
                return _lifecycle.Tick(
                    nowUtc,
                    backend.IsRegistered,
                    backend.IsValid,
                    backend.Commit);
            }
            catch (Exception exception)
            {
                _lifecycle.MarkFailed();
                failureSink?.Invoke(exception);
                return false;
            }
        }

        internal bool Retry(
            DateTime nowUtc,
            TimeSpan timeout,
            IRos2ForUnitySelectionBackend backend,
            Action<Exception> failureSink)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            try
            {
                return _lifecycle.Retry(nowUtc, timeout, backend.Resolve);
            }
            catch (Exception exception)
            {
                _lifecycle.MarkFailed();
                failureSink?.Invoke(exception);
                return false;
            }
        }

        internal bool Cancel(
            IRos2ForUnitySelectionBackend backend,
            Action<Exception> failureSink)
        {
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            try
            {
                return _lifecycle.Cancel(
                    () => backend.Rollback(_lifecycle.State));
            }
            catch (Exception exception)
            {
                _lifecycle.MarkFailed();
                failureSink?.Invoke(exception);
                return false;
            }
        }

        internal bool Fail()
            => _lifecycle.MarkFailed();

        internal bool Complete()
            => _lifecycle.Complete();
    }
}
