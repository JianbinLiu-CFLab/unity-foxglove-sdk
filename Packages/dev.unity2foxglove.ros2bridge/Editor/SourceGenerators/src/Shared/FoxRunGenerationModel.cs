// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Provider-neutral semantic model consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    [Flags]
    public enum FoxRunNamedArgumentPresence : long
    {
        None = 0,
        Hz = 1L << 0,
        Tolerance = 1L << 1,
        OnlyIf = 1L << 2,
        SchemaName = 1L << 3,
        Policy = 1L << 4,
        Mode = 1L << 5,
        Encoding = 1L << 6,
        ProtobufFieldNumber = 1L << 9,
        Reliability = 1L << 11,
        Durability = 1L << 12,
        History = 1L << 13,
        Depth = 1L << 14,
        PublishTransportIds = 1L << 15,
        SubscribeTransportId = 1L << 16
    }

    public enum FoxRunConditionMemberKind
    {
        None = 0,
        Field = 1,
        Property = 2,
        Method = 3,
        Missing = 4,
        Invalid = 5,
        Unresolved = 6
    }

    public sealed class FoxRunNormalizedScheduleTuple :
        IEquatable<FoxRunNormalizedScheduleTuple>
    {
        public FoxRunNormalizedScheduleTuple(
            int policy,
            bool hasExplicitHz,
            float hz,
            float tolerance,
            string onlyIf,
            FoxRunConditionMemberKind conditionMemberKind)
        {
            Policy = policy;
            HasExplicitHz = hasExplicitHz;
            Hz = hz > 0f && !float.IsNaN(hz) && !float.IsInfinity(hz)
                ? hz
                : 0f;
            Tolerance =
                tolerance >= 0f
                && !float.IsNaN(tolerance)
                && !float.IsInfinity(tolerance)
                    ? tolerance
                    : 0f;
            OnlyIf = (onlyIf ?? string.Empty).Trim();
            ConditionMemberKind = conditionMemberKind;
        }

        public int Policy { get; }
        public bool HasExplicitHz { get; }
        public float Hz { get; }
        public float Tolerance { get; }
        public string OnlyIf { get; }
        public FoxRunConditionMemberKind ConditionMemberKind { get; }

        public bool Equals(FoxRunNormalizedScheduleTuple other)
            => other != null
               && Policy == other.Policy
               && HasExplicitHz == other.HasExplicitHz
               && Hz.Equals(other.Hz)
               && Tolerance.Equals(other.Tolerance)
               && string.Equals(OnlyIf, other.OnlyIf, StringComparison.Ordinal)
               && ConditionMemberKind == other.ConditionMemberKind;

        public override bool Equals(object obj)
            => Equals(obj as FoxRunNormalizedScheduleTuple);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Policy;
                hash = (hash * 397) ^ HasExplicitHz.GetHashCode();
                hash = (hash * 397) ^ Hz.GetHashCode();
                hash = (hash * 397) ^ Tolerance.GetHashCode();
                hash =
                    (hash * 397)
                    ^ StringComparer.Ordinal.GetHashCode(OnlyIf);
                return (hash * 397) ^ (int)ConditionMemberKind;
            }
        }
    }

    public sealed class FoxRunEncodingVariantAvailability
    {
        public FoxRunEncodingVariantAvailability(
            string encoding,
            bool publishAvailable,
            bool subscribeAvailable,
            string unavailableDiagnosticId = "",
            string unavailableReason = "",
            string publishUnavailableDiagnosticId = null,
            string publishUnavailableReason = null,
            string subscribeUnavailableDiagnosticId = null,
            string subscribeUnavailableReason = null)
        {
            Encoding = encoding ?? string.Empty;
            PublishAvailable = publishAvailable;
            SubscribeAvailable = subscribeAvailable;
            PublishUnavailableDiagnosticId = publishAvailable
                ? string.Empty
                : publishUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
        }

        public string Encoding { get; }
        public bool PublishAvailable { get; }
        public bool SubscribeAvailable { get; }
        public string PublishUnavailableDiagnosticId { get; }
        public string PublishUnavailableReason { get; }
        public string SubscribeUnavailableDiagnosticId { get; }
        public string SubscribeUnavailableReason { get; }

        public string UnavailableDiagnosticId
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableDiagnosticId,
                SubscribeAvailable,
                SubscribeUnavailableDiagnosticId);

        public string UnavailableReason
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableReason,
                SubscribeAvailable,
                SubscribeUnavailableReason);

        private static string SharedUnavailableValue(
            bool publishAvailable,
            string publishValue,
            bool subscribeAvailable,
            string subscribeValue)
        {
            if (publishAvailable)
                return subscribeAvailable ? string.Empty : subscribeValue;
            if (subscribeAvailable)
                return publishValue;
            if (string.IsNullOrEmpty(publishValue))
                return subscribeValue;
            if (string.IsNullOrEmpty(subscribeValue))
                return publishValue;
            return string.Equals(
                publishValue,
                subscribeValue,
                StringComparison.Ordinal)
                ? publishValue
                : string.Empty;
        }
    }

}
