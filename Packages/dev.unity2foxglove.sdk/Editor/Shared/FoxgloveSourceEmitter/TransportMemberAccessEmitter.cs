// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emit stable reflection-free accessors consumed by Providers.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class TransportMemberAccessEmitter
    {
        internal static IReadOnlyList<FoxRunGenerationMember>
            EligibleMembers(FoxRunGenerationType type)
            => (type?.Members
                ?? Array.Empty<FoxRunGenerationMember>())
                .Where(member => member != null && !member.IsStream)
                .ToArray();

        internal static void Emit(
            StringBuilder sb,
            FoxRunGenerationType type,
            string pad)
        {
            var members = EligibleMembers(type);
            if (members.Count == 0)
                return;

            var publishTopics = type.Members
                .Where(member => member != null && member.Mode != 2)
                .GroupBy(member => member.Topic, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.ToArray())
                .ToArray();

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                var suffix = MethodSuffix(type, member);
                var access =
                    TypeExprEmitter.MemberAccess(member.MemberName);
                var canRead =
                    member.Mode == 1 || member.Mode == 3;
                var canWrite =
                    member.Mode == 2 || member.Mode == 3;
                if (canRead)
                {
                    var topicIndex = Array.FindIndex(
                        publishTopics,
                        fields => string.Equals(
                            fields[0].Topic,
                            member.Topic,
                            StringComparison.Ordinal));
                    var fieldIndex = Array.IndexOf(
                        publishTopics[topicIndex],
                        member);
                    sb.AppendLine(
                        $"{pad}    private {member.EmissionTypeName} __FoxRunRead_{suffix}() => __foxRunCapture_{topicIndex}_{fieldIndex};");
                }

                if (canWrite)
                {
                    sb.AppendLine(
                        $"{pad}    private void __FoxRunWrite_{suffix}({member.EmissionTypeName} value) => {access} = value;");
                }

                sb.AppendLine();
            }
        }

        internal static string StableId(
            FoxRunGenerationType type,
            FoxRunGenerationMember member)
            => FoxRunGeneratedMemberIdentity.Build(
                type.DeclaringType,
                member.MemberKind,
                member.MemberName,
                member.Topic,
                member.Mode,
                member.JsonFieldName);

        internal static string MethodSuffix(
            FoxRunGenerationType type,
            FoxRunGenerationMember member)
            => IdentifierUtils.SanitizeIdentifier(
                   member.MemberName.TrimStart('_'))
               + "_"
               + FoxRunGeneratedMemberIdentity.Fingerprint(
                   StableId(type, member));
    }
}
