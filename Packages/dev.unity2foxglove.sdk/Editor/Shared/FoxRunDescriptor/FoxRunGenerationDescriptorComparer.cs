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
            CompareSemantic(key, "isArray", left.IsArray ? "true" : "false", right.IsArray ? "true" : "false", semantic);
            CompareSemantic(key, "elementTypeName", left.ElementTypeName, right.ElementTypeName, semantic);
            CompareSemantic(key, "encoding", left.Encoding, right.Encoding, semantic);
            CompareSemantic(key, "rateHz", left.RateHz, right.RateHz, semantic);
            CompareSemantic(key, "publishMode", left.PublishModeName, right.PublishModeName, semantic);
            CompareSemantic(key, "mode", left.ModeName, right.ModeName, semantic);
            CompareSemantic(key, "changeEpsilon", left.ChangeEpsilon, right.ChangeEpsilon, semantic);
            CompareSemantic(key, "forceIntervalSeconds", left.ForceIntervalSeconds, right.ForceIntervalSeconds, semantic);
            CompareSemantic(key, "when", left.When, right.When, semantic);
            CompareSemantic(key, "unless", left.Unless, right.Unless, semantic);
            CompareSemantic(key, "isAggregateMember", left.IsAggregateMember ? "true" : "false", right.IsAggregateMember ? "true" : "false", semantic);
            CompareSemantic(key, "jsonFieldName", left.JsonFieldName, right.JsonFieldName, semantic);
            CompareProvenance(key, "hostKind", left.HostKind, right.HostKind, provenance);
            CompareProvenance(key, "rawTypeName", left.RawTypeName, right.RawTypeName, provenance);
            CompareProvenance(key, "rawMemberOrder", left.RawMemberOrder.ToString(), right.RawMemberOrder.ToString(), provenance);
            CompareProvenance(key, "conditionalSymbols", left.ConditionalSymbols, right.ConditionalSymbols, provenance);
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
