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
}
