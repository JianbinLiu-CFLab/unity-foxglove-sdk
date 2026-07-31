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

            EmitAccessSurface(
                sb,
                type,
                members,
                publishTopics,
                pad);
        }

        private static void EmitAccessSurface(
            StringBuilder sb,
            FoxRunGenerationType type,
            IReadOnlyList<FoxRunGenerationMember> members,
            IReadOnlyList<FoxRunGenerationMember[]> publishTopics,
            string pad)
        {
            for (var index = 0; index < members.Count; index++)
            {
                sb.AppendLine(
                    $"{pad}    private IFoxRunGeneratedMemberAccess __foxRunTransportMember_{index};");
            }
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    int IFoxRunGeneratedTransportSource.FoxRunTransport_MemberCount => {members.Count};");
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    IFoxRunGeneratedMemberAccess IFoxRunGeneratedTransportSource.FoxRunTransport_GetMember(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                var suffix = MethodSuffix(type, member);
                var stableId = StringLiteralEmitter.CSharpStringLiteral(
                    StableId(type, member));
                var topic = StringLiteralEmitter.CSharpStringLiteral(
                    member.Topic);
                var schema = StringLiteralEmitter.CSharpStringLiteral(
                    member.SchemaName);
                var read = member.Mode == 1 || member.Mode == 3
                    ? $"__FoxRunRead_{suffix}"
                    : "null";
                var write = member.Mode == 2 || member.Mode == 3
                    ? $"__FoxRunWrite_{suffix}"
                    : "null";
                sb.AppendLine($"{pad}            case {index}:");
                sb.AppendLine(
                    $"{pad}                return __foxRunTransportMember_{index} ??= new FoxRunGeneratedMemberAccess<{member.EmissionTypeName}>(");
                sb.AppendLine(
                    $"{pad}                    \"{stableId}\",");
                sb.AppendLine(
                    $"{pad}                    \"{topic}\",");
                sb.AppendLine(
                    $"{pad}                    \"{schema}\",");
                sb.AppendLine(
                    $"{pad}                    (FoxRunFlow){member.Mode},");
                sb.AppendLine(
                    $"{pad}                    {TopicMetadataEmitter.TransportIdsLiteral(member.PublishTransportIds)},");
                sb.AppendLine(
                    $"{pad}                    {TopicMetadataEmitter.NullableStringLiteral(member.SubscribeTransportId)},");
                sb.AppendLine(
                    $"{pad}                    {TopicMetadataEmitter.EncodingLiteral(member.Encoding)},");
                sb.AppendLine(
                    $"{pad}                    new FoxRunDeliveryPolicy(");
                sb.AppendLine(
                    $"{pad}                        {TopicMetadataEmitter.ReliabilityLiteral(member.Reliability)},");
                sb.AppendLine(
                    $"{pad}                        {TopicMetadataEmitter.DurabilityLiteral(member.Durability)},");
                sb.AppendLine(
                    $"{pad}                        {TopicMetadataEmitter.HistoryLiteral(member.History)},");
                sb.AppendLine(
                    $"{pad}                        {member.Depth}),");
                sb.AppendLine(
                    $"{pad}                    {read},");
                sb.AppendLine(
                    $"{pad}                    {write});");
            }
            sb.AppendLine(
                $"{pad}            default: throw new ArgumentOutOfRangeException(nameof(index));");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();

            sb.AppendLine(
                $"{pad}    ulong IFoxRunGeneratedTransportSource.FoxRunTransport_GetCaptureSequence(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (var topicIndex = 0;
                 topicIndex < publishTopics.Count;
                 topicIndex++)
            {
                var fields = publishTopics[topicIndex];
                var sequence = fields.Length == 1
                               && fields[0]?.TypeShape?.Kind
                               == FoxRunTypeShapeKind.Object
                               && fields[0].TypeShape.CanConstruct
                    ? $"__foxRunCaptureSequence_{topicIndex}"
                    : "0UL";
                sb.AppendLine(
                    $"{pad}            case {topicIndex}: return {sequence};");
            }
            sb.AppendLine(
                $"{pad}            default: throw new ArgumentOutOfRangeException(nameof(topicIndex));");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
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
