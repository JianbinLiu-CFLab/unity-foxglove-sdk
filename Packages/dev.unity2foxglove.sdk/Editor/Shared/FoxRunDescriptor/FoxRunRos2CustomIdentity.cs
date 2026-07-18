// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Deterministic canonical and payload identities for custom ROS2 DTOs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunRos2CustomIdentity
    {
        public static string BuildCanonicalIdentity(
            string fullyQualifiedTypeName,
            IEnumerable<FoxRunRos2CustomDtoMemberShape> members)
        {
            var builder = new StringBuilder();
            AppendLengthFramed(builder, fullyQualifiedTypeName);
            foreach (var member in members ?? Array.Empty<FoxRunRos2CustomDtoMemberShape>())
            {
                AppendLengthFramed(builder, member?.Name);
                AppendLengthFramed(builder, member?.RosFieldName);
                AppendLengthFramed(builder, member?.Kind.ToString());
                AppendLengthFramed(builder, member?.FullyQualifiedTypeName);
                AppendLengthFramed(builder, member?.RosType);
                AppendLengthFramed(builder, member?.SequenceElementTypeName);
                AppendLengthFramed(builder, member?.NestedShapeIdentity);
                AppendLengthFramed(builder, member != null && member.HasPresence ? "1" : "0");
                AppendLengthFramed(builder, member?.SequenceRepresentation.ToString());
            }

            return builder.ToString();
        }

        public static string BuildPayloadIdentity(string fullyQualifiedTypeName, string canonicalIdentity)
        {
            var simpleName = LastSegment(fullyQualifiedTypeName);
            var pascal = FoxRunRos2CustomNamingPolicy.ToPascalIdentifier(simpleName);
            if (string.IsNullOrEmpty(pascal))
                pascal = "FoxRunPayload";

            // Hash the case-folded canonical type identity. This prevents a
            // Windows path/casing variation from producing a second payload
            // identity while retaining the original canonical descriptor text.
            return pascal + "_" + Fnv1a64Hex((fullyQualifiedTypeName ?? string.Empty).ToUpperInvariant()
                + "|" + (canonicalIdentity ?? string.Empty));
        }

        public static string Fnv1a64Hex(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }

            return hash.ToString("X16", CultureInfo.InvariantCulture).Substring(0, 12);
        }

        private static void AppendLengthFramed(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append('|');
        }

        private static string LastSegment(string typeName)
        {
            typeName = typeName ?? string.Empty;
            var dot = typeName.LastIndexOf('.');
            return dot < 0 ? typeName : typeName.Substring(dot + 1);
        }
    }
}
