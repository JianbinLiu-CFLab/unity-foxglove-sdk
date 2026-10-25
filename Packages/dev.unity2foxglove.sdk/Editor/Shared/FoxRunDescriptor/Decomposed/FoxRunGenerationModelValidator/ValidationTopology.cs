// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent validation for the Provider-neutral FoxRun model.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static partial class FoxRunGenerationModelValidator
    {
        private static void ValidateTopicGroups(
            FoxRunGenerationType type,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            foreach (var group in type.Members
                         .Where(member => !string.IsNullOrEmpty(member.Topic))
                         .GroupBy(member => member.Topic, StringComparer.Ordinal))
            {
                var members = group.ToList();
                ValidateTopicAuthority(
                    group.Key,
                    members,
                    diagnostics);
                var schemaNames = members.Select(member => member.SchemaName)
                    .Select(schemaName => schemaName ?? string.Empty)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var mixedAggregateTopic = members.Any(
                        member => member.IsAggregateMember)
                    && members.Any(
                        member => !member.IsAggregateMember);
                if (!mixedAggregateTopic && schemaNames.Count > 1)
                {
                    diagnostics.Add(
                        FoxRunGenerationDiagnostic.Error(
                            "FOXRUN002",
                            group.Key,
                            string.Empty,
                            "Topic members must either all omit SchemaName or all declare the same SchemaName."));
                }

                if (members.Select(member => member.Encoding)
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1)
                {
                    AddError(
                        diagnostics,
                        MixedEncodingId,
                        members[0],
                        "One topic cannot mix Encoding declarations.");
                }

                var fieldNumbers = members
                    .Select(
                        member => new
                        {
                            Member = member,
                            Number =
                                member.ProtobufMetadata?.FieldNumber ?? 0
                        })
                    .Where(value => value.Number != 0)
                    .GroupBy(value => value.Number)
                    .FirstOrDefault(grouping => grouping.Count() > 1);
                if (fieldNumbers != null)
                {
                    AddError(
                        diagnostics,
                        DuplicateFieldNumberId,
                        fieldNumbers.First().Member,
                        "FoxRun topic '" + group.Key
                        + "' has duplicate ProtobufFieldNumber "
                        + fieldNumbers.Key + ".");
                }

                if (mixedAggregateTopic)
                {
                    AddError(
                        diagnostics,
                        "FOXRUN019",
                        members[0],
                        "One topic cannot mix FoxRunMessage aggregate fields with field-level FoxRun members.");
                }

                ValidateDirectionalJsonNames(
                    group.Key,
                    "publish",
                    members.Where(
                        member => member.Mode == 1
                                  || member.Mode == 3),
                    diagnostics);
                ValidateDirectionalJsonNames(
                    group.Key,
                    "subscribe",
                    members.Where(
                        member => member.Mode == 2
                                  || member.Mode == 3),
                    diagnostics);

                var memberNameCollision = members
                    .GroupBy(
                        member => (member.MemberName
                                   ?? string.Empty)
                            .TrimStart('_'),
                        StringComparer.Ordinal)
                    .FirstOrDefault(grouping => grouping.Count() > 1);
                if (memberNameCollision != null)
                {
                    var first = memberNameCollision.First();
                    diagnostics.Add(
                        FoxRunGenerationDiagnostic.Warning(
                            "FOXRUN003",
                            Target(first),
                            first.MemberName,
                            "FoxRun member names collide after stripping leading underscores."));
                }

                if (members.Select(member => member.Policy)
                        .Distinct()
                        .Count()
                    > 1
                    || members.Select(member => member.Hz)
                        .Distinct()
                        .Count()
                    > 1
                    || members.Select(member => member.Tolerance)
                        .Distinct()
                        .Count()
                    > 1)
                {
                    var first = members[0];
                    diagnostics.Add(
                        FoxRunGenerationDiagnostic.Warning(
                            "FOXRUN005",
                            Target(first),
                            first.MemberName,
                            "Topic has mixed Policy, Hz, or Tolerance values."));
                }

                if (members.Select(member => member.OnlyIf)
                        .Distinct(StringComparer.Ordinal)
                        .Count()
                    > 1)
                {
                    AddError(
                        diagnostics,
                        "FOXRUN017",
                        members[0],
                        "One topic cannot mix OnlyIf values.");
                }

                var explicitlyMessagePack = members.Any(
                    member =>
                        member.HasNamedArgument(
                            FoxRunNamedArgumentPresence.Encoding)
                        && string.Equals(
                            member.Encoding,
                            "msgpack",
                            StringComparison.Ordinal));
                if (!explicitlyMessagePack)
                    continue;

                var subscribing = members
                    .Where(member => member.Mode == 2 || member.Mode == 3)
                    .ToList();
                var streamCount =
                    subscribing.Count(member => member.IsStream);
                if (streamCount > 1
                    || (streamCount > 0
                        && streamCount != subscribing.Count))
                {
                    AddError(
                        diagnostics,
                        MessagePackInboundTopologyId,
                        subscribing[0],
                        "MessagePack subscribe topics must contain ordinary members or exactly one stream.");
                }

                var publishing = members
                    .Where(member => member.Mode == 1 || member.Mode == 3)
                    .ToList();
                if (HasMixedSchedule(publishing)
                    || HasMixedSchedule(subscribing))
                {
                    AddError(
                        diagnostics,
                        MessagePackScheduleId,
                        publishing.FirstOrDefault() ?? subscribing[0],
                        "MessagePack members in one direction must share one normalized schedule.");
                }
            }
        }

        private static void ValidateTopicAuthority(
            string topic,
            IReadOnlyList<FoxRunGenerationMember> members,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            if (members == null || members.Count < 2)
                return;

            var publishing = members
                .Where(member => member.Mode == 1 || member.Mode == 3)
                .ToList();
            var subscribing = members
                .Where(member => member.Mode == 2 || member.Mode == 3)
                .ToList();

            if (HasDifferentPublishProviders(publishing))
            {
                AddError(
                    diagnostics,
                    InvalidDirectionalTransportId,
                    publishing[0],
                    "FoxRun topic '" + topic
                    + "' must use one publish Provider selection across all members.");
            }

            if (subscribing.Count > 1
                && subscribing.Skip(1).Any(
                    member => !string.Equals(
                        subscribing[0].SubscribeTransportId,
                        member.SubscribeTransportId,
                        StringComparison.Ordinal)))
            {
                AddError(
                    diagnostics,
                    InvalidDirectionalTransportId,
                    subscribing[0],
                    "FoxRun topic '" + topic
                    + "' must use one subscribe Provider selection across all members.");
            }

            var first = members[0];
            if (members.Skip(1).Any(
                    member => !string.Equals(
                                  first.Reliability,
                                  member.Reliability,
                                  StringComparison.Ordinal)
                              || !string.Equals(
                                  first.Durability,
                                  member.Durability,
                                  StringComparison.Ordinal)
                              || !string.Equals(
                                  first.History,
                                  member.History,
                                  StringComparison.Ordinal)
                              || first.Depth != member.Depth))
            {
                AddError(
                    diagnostics,
                    InvalidDeliveryPolicyId,
                    first,
                    "FoxRun topic '" + topic
                    + "' must use one delivery policy across all members.");
            }
        }

        private static bool HasDifferentPublishProviders(
            IReadOnlyList<FoxRunGenerationMember> publishing)
        {
            if (publishing == null || publishing.Count < 2)
                return false;

            var first = publishing[0].PublishTransportIds;
            return publishing.Skip(1).Any(
                member => !SequenceEqual(
                    first,
                    member.PublishTransportIds));
        }

        private static bool SequenceEqual(
            IReadOnlyList<string> left,
            IReadOnlyList<string> right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Count != right.Count)
                return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(
                        left[index],
                        right[index],
                        StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static void ValidateDirectionalJsonNames(
            string topic,
            string direction,
            IEnumerable<FoxRunGenerationMember> members,
            ICollection<FoxRunGenerationDiagnostic> diagnostics)
        {
            var duplicateJsonName = members
                .GroupBy(
                    member => member.JsonFieldName,
                    StringComparer.Ordinal)
                .FirstOrDefault(names => names.Count() > 1);
            if (duplicateJsonName == null)
                return;

            var first = duplicateJsonName.First();
            diagnostics.Add(
                FoxRunGenerationDiagnostic.Error(
                    "FOXRUN022",
                    first.DeclaringType + "." + first.MemberName,
                    first.MemberName,
                    "FoxRun topic '"
                    + topic
                    + "' has duplicate "
                    + direction
                    + " JSON field name '"
                    + duplicateJsonName.Key
                    + "'."));
        }

        private static bool HasMixedSchedule(
            IReadOnlyList<FoxRunGenerationMember> members)
        {
            if (members.Count < 2)
                return false;
            var first = members[0].NormalizedSchedule;
            return members.Skip(1).Any(
                member => !Equals(first, member.NormalizedSchedule));
        }

        private static bool IsInboundAssignable(FoxRunTypeShape shape)
        {
            if (shape == null)
                return true;
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
                return IsInboundAssignable(shape.ElementShape);
            return shape.Kind != FoxRunTypeShapeKind.Object
                   || shape.Fields.All(
                       field =>
                           field.CanAssign
                           && IsInboundAssignable(field.TypeShape));
        }

        private static bool IsInvalidConditionName(string name)
        {
            if (string.IsNullOrEmpty(name)
                || !IsIdentifierStart(name[0]))
                return true;
            for (var index = 1; index < name.Length; index++)
                if (!IsIdentifierPart(name[index]))
                    return true;
            return false;
        }

        private static bool IsIdentifierStart(char value)
            => value == '_' || char.IsLetter(value);

        private static bool IsIdentifierPart(char value)
            => value == '_' || char.IsLetterOrDigit(value);

        private static bool LooksLikeInputPort(string memberName)
        {
            var value =
                (memberName ?? string.Empty)
                .TrimStart('_')
                .ToLowerInvariant();
            return value.StartsWith("incoming", StringComparison.Ordinal)
                   || value.StartsWith("input", StringComparison.Ordinal)
                   || value.StartsWith("requested", StringComparison.Ordinal)
                   || value.StartsWith("command", StringComparison.Ordinal)
                   || value.StartsWith("remote", StringComparison.Ordinal);
        }

        private static bool IsKnownMemberKind(string memberKind)
            => string.Equals(memberKind, "field", StringComparison.Ordinal)
               || string.Equals(
                   memberKind,
                   "property",
                   StringComparison.Ordinal);

        private static bool IsUnsupportedGenericMember(
            FoxRunGenerationMember member)
        {
            if (IsSupportedNullableMember(member))
                return false;

            var looksGeneric =
                member.EmissionTypeName.IndexOf(
                    '<') >= 0
                || member.RawObservedTypeName.IndexOf(
                    '`') >= 0;
            if (!looksGeneric)
                return false;

            return !member.IsArray
                   || !FoxRunCanonicalTypeNormalizer
                       .IsKnownCanonicalType(
                           member.CanonicalType);
        }

        private static bool IsSupportedNullableMember(
            FoxRunGenerationMember member)
            => FoxRunCanonicalTypeNormalizer
                   .IsKnownCanonicalType(
                       member.CanonicalType)
               && (FoxRunCanonicalTypeNormalizer
                       .IsNullableType(
                           member.EmissionTypeName)
                   || FoxRunCanonicalTypeNormalizer
                       .IsNullableType(
                           member.RawObservedTypeName));

        private static bool IsBinaryLike(string typeName)
        {
            var name = FoxRunEmissionTypeNameFormatter
                .NormalizeCSharpTypeName(typeName);
            return name == "byte[]"
                   || name == "System.Byte[]"
                   || name == "uint8[]"
                   || name.IndexOf(
                       "System.IO.Stream",
                       StringComparison.Ordinal) >= 0
                   || name.IndexOf(
                       "Memory<byte>",
                       StringComparison.Ordinal) >= 0
                   || name.IndexOf(
                       "ReadOnlyMemory<byte>",
                       StringComparison.Ordinal) >= 0
                   || name.IndexOf(
                       "Span<byte>",
                       StringComparison.Ordinal) >= 0
                   || name.IndexOf(
                       "ReadOnlySpan<byte>",
                       StringComparison.Ordinal) >= 0;
        }

        private static bool IsUnityNativeContainerTypeName(
            string rawTypeName)
        {
            if (string.IsNullOrEmpty(rawTypeName))
                return false;
            return UnityNativeContainerPrefixes.Any(
                prefix => rawTypeName.StartsWith(
                    prefix,
                    StringComparison.Ordinal));
        }

        private static string Target(FoxRunGenerationMember member)
            => member.DeclaringType + "." + member.MemberName;

        private static void AddError(
            ICollection<FoxRunGenerationDiagnostic> diagnostics,
            string id,
            FoxRunGenerationMember member,
            string message)
            => diagnostics.Add(
                FoxRunGenerationDiagnostic.Error(
                    id,
                    Target(member),
                    member.MemberName,
                    message));
    }
}
