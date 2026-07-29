// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Stable cross-analyzer identity for one authored FoxRun declaration.

using System;
using System.Globalization;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Builds the language-neutral identity shared by the core and optional
    /// Provider generators. The fingerprint deliberately uses a small,
    /// source-contained FNV-1a implementation so independent analyzers can
    /// duplicate this helper without taking an assembly dependency.
    /// </summary>
    public static class FoxRunGeneratedMemberIdentity
    {
        public static string Build(
            string declaringType,
            string memberKind,
            string memberName,
            string topic,
            int flow,
            string jsonFieldName)
            => (declaringType ?? string.Empty)
               + "\n"
               + (memberKind ?? string.Empty)
               + "\n"
               + (memberName ?? string.Empty)
               + "\n"
               + (topic ?? string.Empty)
               + "\n"
               + flow.ToString(CultureInfo.InvariantCulture)
               + "\n"
               + (jsonFieldName ?? string.Empty);

        public static string Fingerprint(string stableIdentity)
        {
            if (stableIdentity == null)
                throw new ArgumentNullException(nameof(stableIdentity));

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            for (var index = 0; index < stableIdentity.Length; index++)
            {
                var value = stableIdentity[index];
                hash ^= (byte)value;
                hash *= prime;
                hash ^= (byte)(value >> 8);
                hash *= prime;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
