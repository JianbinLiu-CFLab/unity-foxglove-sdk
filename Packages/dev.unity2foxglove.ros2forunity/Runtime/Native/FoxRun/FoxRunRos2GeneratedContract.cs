// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Immutable generated native subscription metadata and bounded-copy context.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>Immutable metadata for one generated native ROS2 subscription.</summary>
    public sealed class FoxRunRos2GeneratedContract
    {
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            string declaredProvider,
            string ros2Qos)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            DeclaredProvider = Require(declaredProvider, nameof(declaredProvider));
            Ros2Qos = Require(ros2Qos, nameof(ros2Qos));
        }

        public string Id { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public string DeclaredProvider { get; }
        public string Ros2Qos { get; }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Generated ROS2 contract value must not be empty.", name);
            return value;
        }
    }

    /// <summary>
    /// Per-callback managed-copy budget. Counts copied string UTF-16 storage and
    /// sequence element storage; it is intentionally not a DDS/CDR byte size.
    /// </summary>
    public sealed class FoxRunRos2CopyContext
    {
        public FoxRunRos2CopyContext(long maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            RemainingBytes = maximumBytes;
        }

        public long RemainingBytes { get; private set; }

        public void RequireBytes(long byteCount)
        {
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if (byteCount > RemainingBytes)
                throw new InvalidOperationException("FoxRun ROS2 managed-copy budget exceeded.");
            RemainingBytes -= byteCount;
        }
    }
}
#endif
