// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Pure per-Transform replay pose ownership arbitration.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Core
{
    public enum ReplayPoseOwnershipDecisionKind
    {
        Apply = 0,
        Hold = 1,
        Skip = 2
    }

    public readonly struct ReplayPoseSample
    {
        public readonly bool HasPosition;
        public readonly float PositionX;
        public readonly float PositionY;
        public readonly float PositionZ;
        public readonly bool HasRotation;
        public readonly float RotationX;
        public readonly float RotationY;
        public readonly float RotationZ;
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

        public static ReplayPoseSample CreatePosition(float x, float y, float z)
            => new(true, x, y, z, false, 0, 0, 0, 1);
    }

    public readonly struct ReplayPoseOwnershipDecision
    {
        public readonly ReplayPoseOwnershipDecisionKind Kind;
        public readonly int TransformKey;
        public readonly ushort ChannelId;
        public readonly ushort OwnerChannelId;
        public readonly ReplayChannelBehavior OwnerBehavior;
        public readonly ReplayPoseSample Pose;
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

    public sealed class ReplayPoseOwnershipArbiter
    {
        private readonly Dictionary<int, OwnerState> _owners = new();
        private readonly Dictionary<int, HeldPoseState> _held = new();
        private readonly HashSet<ContentionKey> _reportedContentions = new();
        private readonly List<ReplayPoseOwnershipDecision> _resolvedHeld = new();

        public bool IsDeferralActive { get; private set; } = true;

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

                var contentionKey = new ContentionKey(transformKey, channelId);
                var shouldReport = _reportedContentions.Add(contentionKey);
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

        public IReadOnlyList<ReplayPoseOwnershipDecision> EndInitDeferral()
        {
            _resolvedHeld.Clear();
            if (!IsDeferralActive)
                return _resolvedHeld;

            IsDeferralActive = false;
            foreach (var pair in _held)
            {
                var held = pair.Value;
                _owners[pair.Key] = new OwnerState(held.ChannelId, held.Behavior);
                _resolvedHeld.Add(Apply(pair.Key, held.ChannelId, held.ChannelId, held.Behavior, held.Pose));
            }

            _held.Clear();
            return _resolvedHeld;
        }

        public void Reset()
        {
            _owners.Clear();
            _held.Clear();
            _reportedContentions.Clear();
            _resolvedHeld.Clear();
            IsDeferralActive = true;
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
