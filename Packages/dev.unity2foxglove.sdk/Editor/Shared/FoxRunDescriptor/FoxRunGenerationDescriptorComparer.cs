// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Semantic/provenance comparison for FoxRun generation descriptors.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunGenerationDescriptorComparer
    {
        public static FoxRunGenerationDescriptorComparison Compare(FoxRunGenerationModel left, FoxRunGenerationModel right)
        {
            var semantic = new List<string>();
            var provenance = new List<string>();
            CompareModelProvenance("descriptorVersion", left?.DescriptorVersion.ToString(), right?.DescriptorVersion.ToString(), provenance);
            CompareModelProvenance("generatorVersion", left?.GeneratorVersion, right?.GeneratorVersion, provenance);
            var leftMembers = Flatten(left, "left", semantic);
            var rightMembers = Flatten(right, "right", semantic);
            var leftKeys = leftMembers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var rightKeys = rightMembers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            CompareSortedMemberKeys(leftKeys, rightKeys, leftMembers, rightMembers, semantic, provenance);

            return new FoxRunGenerationDescriptorComparison(semantic, provenance, copyInputs: false);
        }

        private static void CompareSortedMemberKeys(
            List<string> leftKeys,
            List<string> rightKeys,
            Dictionary<string, FoxRunGenerationMember> leftMembers,
            Dictionary<string, FoxRunGenerationMember> rightMembers,
            List<string> semantic,
            List<string> provenance)
        {
            var extraRight = new List<string>();
            var leftIndex = 0;
            var rightIndex = 0;
            while (leftIndex < leftKeys.Count || rightIndex < rightKeys.Count)
            {
                if (leftIndex >= leftKeys.Count)
                {
                    extraRight.Add(rightKeys[rightIndex++]);
                    continue;
                }

                if (rightIndex >= rightKeys.Count)
                {
                    semantic.Add("Missing right member: " + leftKeys[leftIndex++]);
                    continue;
                }

                var comparison = StringComparer.Ordinal.Compare(leftKeys[leftIndex], rightKeys[rightIndex]);
                if (comparison < 0)
                {
                    semantic.Add("Missing right member: " + leftKeys[leftIndex++]);
                }
                else if (comparison > 0)
                {
                    extraRight.Add(rightKeys[rightIndex++]);
                }
                else
                {
                    leftIndex++;
                    rightIndex++;
                }
            }

            foreach (var extra in extraRight)
                semantic.Add("Extra right member: " + extra);

            leftIndex = 0;
            rightIndex = 0;
            while (leftIndex < leftKeys.Count && rightIndex < rightKeys.Count)
            {
                var comparison = StringComparer.Ordinal.Compare(leftKeys[leftIndex], rightKeys[rightIndex]);
                if (comparison < 0)
                {
                    leftIndex++;
                }
                else if (comparison > 0)
                {
                    rightIndex++;
                }
                else
                {
                    var key = leftKeys[leftIndex];
                    CompareMember(key, leftMembers[key], rightMembers[key], semantic, provenance);
                    leftIndex++;
                    rightIndex++;
                }
            }
        }

        private static Dictionary<string, FoxRunGenerationMember> Flatten(
            FoxRunGenerationModel model,
            string side,
            List<string> semantic)
        {
            var result = new Dictionary<string, FoxRunGenerationMember>(StringComparer.Ordinal);
            foreach (var type in (model == null ? Array.Empty<FoxRunGenerationType>() : model.Types))
            {
                foreach (var member in type.Members)
                {
                    var key = type.DeclaringType + "|" + member.Topic + "|" + member.MemberName + "|" + member.SchemaName + "|" + member.CanonicalType;
                    if (result.ContainsKey(key))
                    {
                        semantic.Add("Duplicate " + side + " member key: " + key);
                        continue;
                    }
                    result.Add(key, member);
                }
            }
            return result;
        }

        private static void CompareMember(
            string key,
            FoxRunGenerationMember left,
            FoxRunGenerationMember right,
            List<string> semantic,
            List<string> provenance)
        {
            CompareSemantic(key, "memberKind", left.MemberKind, right.MemberKind, semantic);
            CompareSemantic(key, "emissionTypeName", left.EmissionTypeName, right.EmissionTypeName, semantic);
            CompareSemantic(key, "canonicalType", left.CanonicalType, right.CanonicalType, semantic);
            CompareSemantic(key, "isValueType", left.IsValueType ? "true" : "false", right.IsValueType ? "true" : "false", semantic);
            CompareSemantic(key, "isArray", left.IsArray ? "true" : "false", right.IsArray ? "true" : "false", semantic);
            CompareSemantic(key, "elementTypeName", left.ElementTypeName, right.ElementTypeName, semantic);
            CompareSemantic(key, "encoding", left.Encoding, right.Encoding, semantic);
            CompareSemantic(
                key,
                "publishTransportIds",
                left.PublishTransportIds == null
                    ? "<inherit>"
                    : string.Join(",", left.PublishTransportIds),
                right.PublishTransportIds == null
                    ? "<inherit>"
                    : string.Join(",", right.PublishTransportIds),
                semantic);
            CompareSemantic(
                key,
                "subscribeTransportId",
                left.SubscribeTransportId ?? "<inherit>",
                right.SubscribeTransportId ?? "<inherit>",
                semantic);
            CompareSemantic(key, "reliability", left.Reliability, right.Reliability, semantic);
            CompareSemantic(key, "durability", left.Durability, right.Durability, semantic);
            CompareSemantic(key, "history", left.History, right.History, semantic);
            CompareSemantic(key, "depth", left.Depth, right.Depth, semantic);
            CompareSemantic(key, "generatesWebSocketCodec", left.GeneratesWebSocketCodec ? "true" : "false", right.GeneratesWebSocketCodec ? "true" : "false", semantic);
            CompareProtobufMetadata(key, left.ProtobufMetadata, right.ProtobufMetadata, semantic);
            CompareTypeShape(key, "typeShape", left.TypeShape, right.TypeShape, semantic);
            CompareEncodingVariants(key, left.EncodingVariants, right.EncodingVariants, semantic);
            CompareNormalizedSchedule(key, left.NormalizedSchedule, right.NormalizedSchedule, semantic);
            CompareSemantic(key, "hz", left.Hz, right.Hz, semantic);
            CompareSemantic(key, "policy", left.PolicyName, right.PolicyName, semantic);
            CompareSemantic(key, "mode", left.FlowName, right.FlowName, semantic);
            CompareSemantic(key, "tolerance", left.Tolerance, right.Tolerance, semantic);
            CompareSemantic(key, "onlyIf", left.OnlyIf, right.OnlyIf, semantic);
            CompareSemantic(
                key,
                "onlyIfMemberKind",
                FoxRunGenerationMember.ConditionMemberKindToName(left.ConditionMemberKind),
                FoxRunGenerationMember.ConditionMemberKindToName(right.ConditionMemberKind),
                semantic);
            CompareSemantic(
                key,
                "explicitArguments",
                FoxRunGenerationMember.ExplicitArgumentsToText(left.NamedArgumentPresence),
                FoxRunGenerationMember.ExplicitArgumentsToText(right.NamedArgumentPresence),
                semantic);
            CompareSemantic(key, "isAggregateMember", left.IsAggregateMember ? "true" : "false", right.IsAggregateMember ? "true" : "false", semantic);
            CompareSemantic(key, "isStream", left.IsStream ? "true" : "false", right.IsStream ? "true" : "false", semantic);
            CompareSemantic(key, "jsonFieldName", left.JsonFieldName, right.JsonFieldName, semantic);
            CompareProvenance(key, "hostKind", left.HostKind, right.HostKind, provenance);
            CompareProvenance(key, "rawTypeName", left.RawTypeName, right.RawTypeName, provenance);
            CompareProvenance(key, "rawMemberOrder", left.RawMemberOrder.ToString(), right.RawMemberOrder.ToString(), provenance);
            CompareProvenance(key, "conditionalSymbols", left.ConditionalSymbols, right.ConditionalSymbols, provenance);
        }

        private static void CompareEncodingVariants(
            string key,
            IReadOnlyList<FoxRunEncodingVariantAvailability> left,
            IReadOnlyList<FoxRunEncodingVariantAvailability> right,
            List<string> semantic)
        {
            var leftValues = left ?? Array.Empty<FoxRunEncodingVariantAvailability>();
            var rightValues = right ?? Array.Empty<FoxRunEncodingVariantAvailability>();
            CompareSemantic(
                key,
                "encodingVariants.count",
                leftValues.Count.ToString(),
                rightValues.Count.ToString(),
                semantic);
            var count = Math.Min(leftValues.Count, rightValues.Count);
            for (var index = 0; index < count; index++)
            {
                var leftValue = leftValues[index];
                var rightValue = rightValues[index];
                var prefix = "encodingVariants[" + index + "].";
                CompareSemantic(key, prefix + "encoding", leftValue.Encoding, rightValue.Encoding, semantic);
                CompareSemantic(
                    key,
                    prefix + "publishAvailable",
                    leftValue.PublishAvailable ? "true" : "false",
                    rightValue.PublishAvailable ? "true" : "false",
                    semantic);
                CompareSemantic(
                    key,
                    prefix + "subscribeAvailable",
                    leftValue.SubscribeAvailable ? "true" : "false",
                    rightValue.SubscribeAvailable ? "true" : "false",
                    semantic);
                CompareSemantic(
                    key,
                    prefix + "publishUnavailableDiagnosticId",
                    leftValue.PublishUnavailableDiagnosticId,
                    rightValue.PublishUnavailableDiagnosticId,
                    semantic);
                CompareSemantic(
                    key,
                    prefix + "publishUnavailableReason",
                    leftValue.PublishUnavailableReason,
                    rightValue.PublishUnavailableReason,
                    semantic);
                CompareSemantic(
                    key,
                    prefix + "subscribeUnavailableDiagnosticId",
                    leftValue.SubscribeUnavailableDiagnosticId,
                    rightValue.SubscribeUnavailableDiagnosticId,
                    semantic);
                CompareSemantic(
                    key,
                    prefix + "subscribeUnavailableReason",
                    leftValue.SubscribeUnavailableReason,
                    rightValue.SubscribeUnavailableReason,
                    semantic);
            }
        }

        private static void CompareNormalizedSchedule(
            string key,
            FoxRunNormalizedScheduleTuple left,
            FoxRunNormalizedScheduleTuple right,
            List<string> semantic)
        {
            if (ReferenceEquals(left, right))
                return;
            if (left == null || right == null)
            {
                CompareSemantic(
                    key,
                    "normalizedSchedule",
                    left == null ? "null" : "present",
                    right == null ? "null" : "present",
                    semantic);
                return;
            }

            CompareSemantic(key, "normalizedSchedule.policy", left.Policy.ToString(), right.Policy.ToString(), semantic);
            CompareSemantic(
                key,
                "normalizedSchedule.hasExplicitHz",
                left.HasExplicitHz ? "true" : "false",
                right.HasExplicitHz ? "true" : "false",
                semantic);
            CompareSemantic(key, "normalizedSchedule.hz", left.Hz, right.Hz, semantic);
            CompareSemantic(key, "normalizedSchedule.tolerance", left.Tolerance, right.Tolerance, semantic);
            CompareSemantic(key, "normalizedSchedule.onlyIf", left.OnlyIf, right.OnlyIf, semantic);
            CompareSemantic(
                key,
                "normalizedSchedule.conditionMemberKind",
                left.ConditionMemberKind.ToString(),
                right.ConditionMemberKind.ToString(),
                semantic);
        }

        private static void CompareTypeShape(
            string key,
            string path,
            FoxRunTypeShape left,
            FoxRunTypeShape right,
            List<string> semantic)
        {
            if (ReferenceEquals(left, right))
                return;
            if (left == null || right == null)
            {
                CompareSemantic(
                    key,
                    path,
                    left == null ? "null" : "present",
                    right == null ? "null" : "present",
                    semantic);
                return;
            }

            CompareSemantic(key, path + ".kind", left.Kind.ToString(), right.Kind.ToString(), semantic);
            CompareSemantic(key, path + ".typeName", left.TypeName, right.TypeName, semantic);
            CompareSemantic(key, path + ".canonicalType", left.CanonicalType, right.CanonicalType, semantic);
            CompareSemantic(
                key,
                path + ".nullable",
                left.Nullable ? "true" : "false",
                right.Nullable ? "true" : "false",
                semantic);
            CompareSemantic(
                key,
                path + ".canConstruct",
                left.CanConstruct ? "true" : "false",
                right.CanConstruct ? "true" : "false",
                semantic);
            CompareSemantic(
                key,
                path + ".isValueType",
                left.IsValueType ? "true" : "false",
                right.IsValueType ? "true" : "false",
                semantic);
            CompareSemantic(
                key,
                path + ".collectionKind",
                left.CollectionKind.ToString(),
                right.CollectionKind.ToString(),
                semantic);
            CompareTypeShape(key, path + ".elementShape", left.ElementShape, right.ElementShape, semantic);

            CompareSemantic(
                key,
                path + ".enumValues.count",
                left.EnumValues.Count.ToString(),
                right.EnumValues.Count.ToString(),
                semantic);
            var enumCount = Math.Min(left.EnumValues.Count, right.EnumValues.Count);
            for (var index = 0; index < enumCount; index++)
            {
                CompareSemantic(
                    key,
                    path + ".enumValues[" + index + "].name",
                    left.EnumValues[index].Name,
                    right.EnumValues[index].Name,
                    semantic);
                CompareSemantic(
                    key,
                    path + ".enumValues[" + index + "].number",
                    left.EnumValues[index].Number.ToString(),
                    right.EnumValues[index].Number.ToString(),
                    semantic);
            }

            CompareSemantic(
                key,
                path + ".fields.count",
                left.Fields.Count.ToString(),
                right.Fields.Count.ToString(),
                semantic);
            var fieldCount = Math.Min(left.Fields.Count, right.Fields.Count);
            for (var index = 0; index < fieldCount; index++)
            {
                var leftField = left.Fields[index];
                var rightField = right.Fields[index];
                var fieldPath = path + ".fields[" + index + "]";
                CompareSemantic(key, fieldPath + ".jsonName", leftField.JsonName, rightField.JsonName, semantic);
                CompareSemantic(key, fieldPath + ".memberName", leftField.MemberName, rightField.MemberName, semantic);
                CompareSemantic(
                    key,
                    fieldPath + ".repeated",
                    leftField.Repeated ? "true" : "false",
                    rightField.Repeated ? "true" : "false",
                    semantic);
                CompareSemantic(
                    key,
                    fieldPath + ".collectionKind",
                    leftField.RepeatedCollectionKind.ToString(),
                    rightField.RepeatedCollectionKind.ToString(),
                    semantic);
                CompareSemantic(
                    key,
                    fieldPath + ".canAssign",
                    leftField.CanAssign ? "true" : "false",
                    rightField.CanAssign ? "true" : "false",
                    semantic);
                CompareSemantic(
                    key,
                    fieldPath + ".nullable",
                    leftField.IsNullable ? "true" : "false",
                    rightField.IsNullable ? "true" : "false",
                    semantic);
                CompareTypeShape(
                    key,
                    fieldPath + ".shape",
                    leftField.TypeShape,
                    rightField.TypeShape,
                    semantic);
            }
        }

        private static void CompareProtobufMetadata(
            string key,
            FoxRunProtobufMetadata left,
            FoxRunProtobufMetadata right,
            List<string> semantic)
        {
            if (ReferenceEquals(left, right))
                return;
            if (left == null || right == null)
            {
                CompareSemantic(
                    key,
                    "protobuf",
                    left == null ? "null" : "present",
                    right == null ? "null" : "present",
                    semantic);
                return;
            }

            CompareSemantic(
                key,
                "protobuf.fieldNumber",
                left.FieldNumber.ToString(),
                right.FieldNumber.ToString(),
                semantic);
            CompareProtobufTypeMetadata(
                key,
                "protobuf.type",
                left.TypeMetadata,
                right.TypeMetadata,
                semantic);
        }

        private static void CompareProtobufTypeMetadata(
            string key,
            string path,
            FoxRunProtobufTypeMetadata left,
            FoxRunProtobufTypeMetadata right,
            List<string> semantic)
        {
            if (ReferenceEquals(left, right))
                return;
            if (left == null || right == null)
            {
                CompareSemantic(
                    key,
                    path,
                    left == null ? "null" : "present",
                    right == null ? "null" : "present",
                    semantic);
                return;
            }

            CompareSemantic(key, path + ".typeName", left.TypeName, right.TypeName, semantic);
            CompareSemantic(
                key,
                path + ".fields.count",
                left.Fields.Count.ToString(),
                right.Fields.Count.ToString(),
                semantic);
            var count = Math.Min(left.Fields.Count, right.Fields.Count);
            for (var index = 0; index < count; index++)
            {
                var leftField = left.Fields[index];
                var rightField = right.Fields[index];
                var fieldPath = path + ".fields[" + index + "]";
                CompareSemantic(key, fieldPath + ".memberName", leftField.MemberName, rightField.MemberName, semantic);
                CompareSemantic(key, fieldPath + ".jsonName", leftField.JsonName, rightField.JsonName, semantic);
                CompareSemantic(key, fieldPath + ".fieldNumber", leftField.FieldNumber.ToString(), rightField.FieldNumber.ToString(), semantic);
                CompareSemantic(key, fieldPath + ".presenceOnly", leftField.PresenceOnly ? "true" : "false", rightField.PresenceOnly ? "true" : "false", semantic);
                CompareSemantic(key, fieldPath + ".presenceUsesHasValue", leftField.PresenceUsesHasValue ? "true" : "false", rightField.PresenceUsesHasValue ? "true" : "false", semantic);
                CompareProtobufTypeMetadata(
                    key,
                    fieldPath + ".type",
                    leftField.TypeMetadata,
                    rightField.TypeMetadata,
                    semantic);
            }
        }

        private static void CompareSemantic(string key, string field, string left, string right, List<string> diffs)
        {
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal))
                diffs.Add(key + " semantic " + field + ": '" + left + "' != '" + right + "'");
        }

        private static void CompareSemantic(string key, string field, float left, float right, List<string> diffs)
        {
            if (!left.Equals(right))
                diffs.Add(key + " semantic " + field + ": '" + left + "' != '" + right + "'");
        }

        private static void CompareProvenance(string key, string field, string left, string right, List<string> diffs)
        {
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal))
                diffs.Add(key + " provenance " + field + ": '" + left + "' != '" + right + "'");
        }

        private static void CompareModelProvenance(string field, string left, string right, List<string> diffs)
        {
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal))
                diffs.Add("model provenance " + field + ": '" + left + "' != '" + right + "'");
        }
    }

    public sealed class FoxRunGenerationDescriptorComparison
    {
        public readonly IReadOnlyList<string> SemanticDifferences;
        public readonly IReadOnlyList<string> ProvenanceDifferences;

        public FoxRunGenerationDescriptorComparison(IReadOnlyList<string> semanticDifferences, IReadOnlyList<string> provenanceDifferences)
            : this(semanticDifferences, provenanceDifferences, copyInputs: true)
        {
        }

        internal FoxRunGenerationDescriptorComparison(
            IReadOnlyList<string> semanticDifferences,
            IReadOnlyList<string> provenanceDifferences,
            bool copyInputs)
        {
            SemanticDifferences = ToReadOnly(semanticDifferences, copyInputs);
            ProvenanceDifferences = ToReadOnly(provenanceDifferences, copyInputs);
        }

        private static IReadOnlyList<string> ToReadOnly(IReadOnlyList<string> values, bool copyInputs)
        {
            if (values == null)
                return Array.Empty<string>();
            if (!copyInputs && values is List<string> list)
                return list.AsReadOnly();
            return values.ToList().AsReadOnly();
        }

        public bool IsSemanticEqual => SemanticDifferences.Count == 0;

        public bool IsProvenanceEqual => ProvenanceDifferences.Count == 0;
    }
}
