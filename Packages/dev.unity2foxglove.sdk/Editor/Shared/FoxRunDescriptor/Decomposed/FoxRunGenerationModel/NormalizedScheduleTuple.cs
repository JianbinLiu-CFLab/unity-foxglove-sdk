// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor/Decomposed/FoxRunGenerationModel
// Purpose: Provider-neutral semantic model section consumed by FoxRun source emission.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
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
}
