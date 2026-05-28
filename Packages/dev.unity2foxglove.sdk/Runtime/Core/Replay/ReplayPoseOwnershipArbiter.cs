// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Pure per-Transform replay pose ownership arbitration.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>Decision outcome for a single pose offer: Apply, Hold (deferred), or Skip (contention).</summary>
    public enum ReplayPoseOwnershipDecisionKind
    {
        /// <summary>Apply the offered pose immediately.</summary>
        Apply = 0,
        /// <summary>Hold the pose for deferred resolution after init-batch completes.</summary>
        Hold = 1,
        /// <summary>Skip — another channel already owns this transform.</summary>
        Skip = 2
    }

    /// <summary>Pose sample from a replay message, carrying optional position and rotation.</summary>
    public readonly struct ReplayPoseSample
    {
        /// <summary>Whether this sample carries a position value.</summary>
        public readonly bool HasPosition;
        /// <summary>World-space X position in meters.</summary>
        public readonly float PositionX;
        /// <summary>World-space Y position in meters.</summary>
        public readonly float PositionY;
        /// <summary>World-space Z position in meters.</summary>
        public readonly float PositionZ;
        /// <summary>Whether this sample carries a rotation value.</summary>
        public readonly bool HasRotation;
        /// <summary>Rotation quaternion X component.</summary>
        public readonly float RotationX;
        /// <summary>Rotation quaternion Y component.</summary>
        public readonly float RotationY;
        /// <summary>Rotation quaternion Z component.</summary>
        public readonly float RotationZ;
        /// <summary>Rotation quaternion W component.</summary>
        public readonly float RotationW;

        public ReplayPoseSample(
            bool hasPosition,
            float positionX,
            float positionY,
            float positionZ,
            bool hasRotation,
            float rotationX,
            float rotationY,
            float rotationZ,
            float rotationW)
        {
            HasPosition = hasPosition;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            HasRotation = hasRotation;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            RotationW = rotationW;
        }

        /// <summary>Creates a position-only pose sample with identity rotation.</summary>
        public static ReplayPoseSample CreatePosition(float x, float y, float z)
            => new(true, x, y, z, false, 0, 0, 0, 1);
    }

    /// <summary>Result of a single pose ownership arbitration decision.</summary>
    public readonly struct ReplayPoseOwnershipDecision
    {
        /// <summary>Decision kind: Apply, Hold, or Skip.</summary>
        public readonly ReplayPoseOwnershipDecisionKind Kind;
        /// <summary>Integer key identifying the transform in the scene.</summary>
        public readonly int TransformKey;
        /// <summary>Channel ID of the replay message offering this pose.</summary>
        public readonly ushort ChannelId;
        /// <summary>Channel ID of the current pose owner (same as ChannelId for non-contention).</summary>
        public readonly ushort OwnerChannelId;
        /// <summary>Behavior class of the owning channel.</summary>
        public readonly ReplayChannelBehavior OwnerBehavior;
        /// <summary>The pose sample being offered.</summary>
        public readonly ReplayPoseSample Pose;
        /// <summary>Whether this decision should trigger a contention warning log.</summary>
        public readonly bool ShouldReportContention;

        public ReplayPoseOwnershipDecision(
            ReplayPoseOwnershipDecisionKind kind,
            int transformKey,
            ushort channelId,
            ushort ownerChannelId,
            ReplayChannelBehavior ownerBehavior,
            ReplayPoseSample pose,
            bool shouldReportContention)
        {
            Kind = kind;
            TransformKey = transformKey;
            ChannelId = channelId;
            OwnerChannelId = ownerChannelId;
            OwnerBehavior = ownerBehavior;
            Pose = pose;
            ShouldReportContention = shouldReportContention;
        }
    }

    /// <summary>
    /// Behavior-based per-Transform pose owner selection for replayed scene
    /// and frame-transform sources.
    /// </summary>
    public sealed class ReplayPoseOwnershipArbiter
    {
        /// <summary>Maximum number of contention pairs reported before the set is cleared to prevent unbounded growth.</summary>
        private const int MaxReportedContentions = 4096;

        private readonly Dictionary<int, OwnerState> _owners = new();
        private readonly Dictionary<int, HeldPoseState> _held = new();
        private readonly HashSet<ContentionKey> _reportedContentions = new();
        private readonly List<ReplayPoseOwnershipDecision> _resolvedHeld = new();

        /// <summary>Whether init-batch deferral is active. Held poses are resolved when this is set to false.</summary>
        public bool IsDeferralActive { get; private set; } = true;

        /// <summary>
        /// Offers one pose sample using channel identity as a concrete source key,
        /// not topic-name priority, while behavior decides ownership class.
        /// </summary>
        public ReplayPoseOwnershipDecision OfferPose(
            int transformKey,
            ushort channelId,
            ReplayChannelBehavior behavior,
            ulong logTimeNs,
            ReplayPoseSample pose)
        {
            if (_owners.TryGetValue(transformKey, out var owner))
            {
                if (owner.ChannelId == channelId)
                    return Apply(transformKey, channelId, owner.ChannelId, owner.Behavior, pose);

                if (IsFrameTransformPose(behavior) && IsDeferredPoseBehavior(owner.Behavior))
                {
                    _owners[transformKey] = new OwnerState(channelId, behavior);
                    return Apply(transformKey, channelId, channelId, behavior, pose);
                }

                var shouldReport = TryReportContention(new ContentionKey(transformKey, channelId));
                return new ReplayPoseOwnershipDecision(
                    ReplayPoseOwnershipDecisionKind.Skip,
                    transformKey,
                    channelId,
                    owner.ChannelId,
                    owner.Behavior,
                    pose,
                    shouldReport);
            }

            if (IsFrameTransformPose(behavior))
            {
                _held.Remove(transformKey);
                _owners[transformKey] = new OwnerState(channelId, behavior);
                return Apply(transformKey, channelId, channelId, behavior, pose);
            }

            if (IsDeferralActive && IsDeferredPoseBehavior(behavior))
            {
                RecordHeldPose(transformKey, channelId, behavior, logTimeNs, pose);
                return new ReplayPoseOwnershipDecision(
                    ReplayPoseOwnershipDecisionKind.Hold,
                    transformKey,
                    channelId,
                    channelId,
                    behavior,
                    pose,
                    false);
            }

            if (IsDeferredPoseBehavior(behavior))
            {
                _owners[transformKey] = new OwnerState(channelId, behavior);
                return Apply(transformKey, channelId, channelId, behavior, pose);
            }

            return new ReplayPoseOwnershipDecision(
                ReplayPoseOwnershipDecisionKind.Skip,
                transformKey,
                channelId,
                channelId,
                behavior,
                pose,
                false);
        }

        /// <summary>Ends the init-batch deferral period, resolving all held poses to Apply decisions.</summary>
        public IReadOnlyList<ReplayPoseOwnershipDecision> EndInitDeferral()
        {
            _resolvedHeld.Clear();
            if (!IsDeferralActive)
                return Array.Empty<ReplayPoseOwnershipDecision>();

            IsDeferralActive = false;
            foreach (var pair in _held)
            {
                var held = pair.Value;
                _owners[pair.Key] = new OwnerState(held.ChannelId, held.Behavior);
                _resolvedHeld.Add(Apply(pair.Key, held.ChannelId, held.ChannelId, held.Behavior, held.Pose));
            }

            _held.Clear();
            return _resolvedHeld.ToArray();
        }

        /// <summary>Clears all ownership state and resets deferral to active.</summary>
        public void Reset()
        {
            _owners.Clear();
            _held.Clear();
            _reportedContentions.Clear();
            _resolvedHeld.Clear();
            IsDeferralActive = true;
        }

        private bool TryReportContention(ContentionKey key)
        {
            if (_reportedContentions.Count >= MaxReportedContentions)
                _reportedContentions.Clear();

            return _reportedContentions.Add(key);
        }

        private void RecordHeldPose(
            int transformKey,
            ushort channelId,
            ReplayChannelBehavior behavior,
            ulong logTimeNs,
            ReplayPoseSample pose)
        {
            if (!_held.TryGetValue(transformKey, out var existing)
                || logTimeNs < existing.FirstLogTimeNs
                || (logTimeNs == existing.FirstLogTimeNs && channelId < existing.ChannelId))
            {
                _held[transformKey] = new HeldPoseState(channelId, behavior, logTimeNs, pose);
                return;
            }

            if (existing.ChannelId == channelId)
                _held[transformKey] = existing.WithPose(pose);
        }

        private static ReplayPoseOwnershipDecision Apply(
            int transformKey,
            ushort channelId,
            ushort ownerChannelId,
            ReplayChannelBehavior ownerBehavior,
            ReplayPoseSample pose)
            => new(
                ReplayPoseOwnershipDecisionKind.Apply,
                transformKey,
                channelId,
                ownerChannelId,
                ownerBehavior,
                pose,
                false);

        private static bool IsFrameTransformPose(ReplayChannelBehavior behavior)
            => behavior == ReplayChannelBehavior.FrameTransformPose;

        private static bool IsDeferredPoseBehavior(ReplayChannelBehavior behavior)
            => behavior == ReplayChannelBehavior.ScenePrimitivePose || behavior == ReplayChannelBehavior.Unclassified;

        private readonly struct OwnerState
        {
            public readonly ushort ChannelId;
            public readonly ReplayChannelBehavior Behavior;

            public OwnerState(ushort channelId, ReplayChannelBehavior behavior)
            {
                ChannelId = channelId;
                Behavior = behavior;
            }
        }

        private readonly struct HeldPoseState
        {
            public readonly ushort ChannelId;
            public readonly ReplayChannelBehavior Behavior;
            public readonly ulong FirstLogTimeNs;
            public readonly ReplayPoseSample Pose;

            public HeldPoseState(
                ushort channelId,
                ReplayChannelBehavior behavior,
                ulong firstLogTimeNs,
                ReplayPoseSample pose)
            {
                ChannelId = channelId;
                Behavior = behavior;
                FirstLogTimeNs = firstLogTimeNs;
                Pose = pose;
            }

            public HeldPoseState WithPose(ReplayPoseSample pose)
                => new(ChannelId, Behavior, FirstLogTimeNs, pose);
        }

        private readonly struct ContentionKey : IEquatable<ContentionKey>
        {
            private readonly int _transformKey;
            private readonly ushort _channelId;

            public ContentionKey(int transformKey, ushort channelId)
            {
                _transformKey = transformKey;
                _channelId = channelId;
            }

            public bool Equals(ContentionKey other)
                => _transformKey == other._transformKey && _channelId == other._channelId;

            public override bool Equals(object obj)
                => obj is ContentionKey other && Equals(other);

            public override int GetHashCode()
                => (_transformKey * 397) ^ _channelId;
        }
    }
}
