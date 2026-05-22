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
            var leftMembers = Flatten(left);
            var rightMembers = Flatten(right);
            var leftKeys = leftMembers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var rightKeys = rightMembers.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            foreach (var missing in leftKeys.Except(rightKeys, StringComparer.Ordinal))
                semantic.Add("Missing right member: " + missing);
            foreach (var extra in rightKeys.Except(leftKeys, StringComparer.Ordinal))
                semantic.Add("Extra right member: " + extra);

            foreach (var key in leftKeys.Intersect(rightKeys, StringComparer.Ordinal))
            {
                CompareMember(key, leftMembers[key], rightMembers[key], semantic, provenance);
            }

            return new FoxRunGenerationDescriptorComparison(semantic, provenance);
        }

        private static Dictionary<string, FoxRunGenerationMember> Flatten(FoxRunGenerationModel model)
        {
            var result = new Dictionary<string, FoxRunGenerationMember>(StringComparer.Ordinal);
            foreach (var type in (model == null ? Array.Empty<FoxRunGenerationType>() : model.Types))
            {
                foreach (var member in type.Members)
                {
                    var key = type.DeclaringType + "|" + member.Topic + "|" + member.MemberName + "|" + member.SchemaName;
                    result[key] = member;
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
            CompareSemantic(key, "encoding", left.Encoding, right.Encoding, semantic);
            CompareSemantic(key, "rateHz", left.RateHz, right.RateHz, semantic);
            CompareSemantic(key, "publishMode", left.PublishModeName, right.PublishModeName, semantic);
            CompareSemantic(key, "changeEpsilon", left.ChangeEpsilon, right.ChangeEpsilon, semantic);
            CompareSemantic(key, "forceIntervalSeconds", left.ForceIntervalSeconds, right.ForceIntervalSeconds, semantic);
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
            if (Math.Abs(left - right) > 0f)
                diffs.Add(key + " semantic " + field + ": '" + left + "' != '" + right + "'");
        }

        private static void CompareProvenance(string key, string field, string left, string right, List<string> diffs)
        {
            if (!string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal))
                diffs.Add(key + " provenance " + field + ": '" + left + "' != '" + right + "'");
        }
    }

    public sealed class FoxRunGenerationDescriptorComparison
    {
        public readonly IReadOnlyList<string> SemanticDifferences;
        public readonly IReadOnlyList<string> ProvenanceDifferences;

        public FoxRunGenerationDescriptorComparison(IReadOnlyList<string> semanticDifferences, IReadOnlyList<string> provenanceDifferences)
        {
            SemanticDifferences = (semanticDifferences ?? Array.Empty<string>()).ToList().AsReadOnly();
            ProvenanceDifferences = (provenanceDifferences ?? Array.Empty<string>()).ToList().AsReadOnly();
        }

        public bool IsSemanticEqual => SemanticDifferences.Count == 0;
    }
}
