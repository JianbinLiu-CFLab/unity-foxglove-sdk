// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Provides deterministic, legal Protobuf field numbers for FoxRun contracts.

using System;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunProtobufFieldNumber
    {
        public const int MaximumFieldNumber = 536870911;
        public const int ReservedStart = 19000;
        public const int ReservedEnd = 19999;

        /// <summary>
        /// Resolves an explicit field number or derives one from a stable canonical identity.
        /// A zero override is the automatic-number sentinel.
        /// </summary>
        public static int Resolve(string canonicalIdentity, int explicitFieldNumber)
        {
            if (explicitFieldNumber != 0)
            {
                ValidateExplicit(explicitFieldNumber);
                return explicitFieldNumber;
            }

            if (string.IsNullOrWhiteSpace(canonicalIdentity))
                throw new ArgumentException("A canonical identity is required for automatic Protobuf field numbers.", nameof(canonicalIdentity));

            var hash = StableHash(canonicalIdentity);
            var candidate = (int)(hash % MaximumFieldNumber) + 1;
            while (IsReserved(candidate))
            {
                candidate++;
                if (candidate > MaximumFieldNumber)
                    candidate = 1;
            }

            return candidate;
        }

        public static bool IsReserved(int fieldNumber)
        {
            return fieldNumber >= ReservedStart && fieldNumber <= ReservedEnd;
        }

        private static void ValidateExplicit(int fieldNumber)
        {
            if (fieldNumber < 1 || fieldNumber > MaximumFieldNumber || IsReserved(fieldNumber))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fieldNumber),
                    fieldNumber,
                    "ProtobufFieldNumber must be in 1..536870911 and outside the reserved range 19000..19999.");
            }
        }

        private static uint StableHash(string value)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash;
        }
    }
}
