// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Builds deterministic FoxRun canonical manifests from resolved members.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunManifestBuilder
    {
        private const string PackageName = "Unity2Foxglove";
        private const string GeneratorName = "FoxRun";
        private const string JsonEncoding = "json";

        public static FoxRunCanonicalManifest Build(
            IReadOnlyList<FoxRunManifestMember> members,
            int manifestVersion = 1,
            int generatorMajorVersion = 1)
        {
            var source = members ?? Array.Empty<FoxRunManifestMember>();
            var types = BuildTypes(source);
            var sectionHashInput = FoxRunManifestJsonWriter.WriteFoxRunSectionHashInput(types);
            var manifestHash = FoxRunManifestHasher.Sha256Hex(sectionHashInput);
            var section = new FoxRunManifestFoxRunSection(manifestHash, types);
            var sections = new FoxRunManifestSections(section);
            var generator = new FoxRunManifestGenerator(GeneratorName, generatorMajorVersion);
            var globalHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteGlobalHashInput(
                    manifestVersion,
                    PackageName,
                    generator,
                    manifestHash));
            return new FoxRunCanonicalManifest(
                manifestVersion,
                PackageName,
                generator,
                sections,
                globalHash);
        }

        private static IReadOnlyList<FoxRunManifestType> BuildTypes(IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .GroupBy(DeclaringType)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new FoxRunManifestType(group.Key, BuildContracts(group.Key, group.ToList())))
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<FoxRunManifestContract> BuildContracts(
            string declaringType,
            IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .GroupBy(member => new ContractKey(member.Topic, member.SchemaName, JsonEncoding))
                .OrderBy(group => group.Key.Topic, StringComparer.Ordinal)
                .ThenBy(group => group.Key.SchemaName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Encoding, StringComparer.Ordinal)
                .Select(group => BuildContract(declaringType, group.Key, group.ToList()))
                .ToList()
                .AsReadOnly();
        }

        private static FoxRunManifestContract BuildContract(
            string declaringType,
            ContractKey key,
            IReadOnlyList<FoxRunManifestMember> members)
        {
            var fields = members
                .Select(BuildField)
                .OrderBy(field => field.JsonName, StringComparer.Ordinal)
                .ThenBy(field => field.MemberName, StringComparer.Ordinal)
                .ThenBy(field => field.Type, StringComparer.Ordinal)
                .ToList();
            ValidateJsonFieldNames(declaringType, key, fields);
            var policy = BuildPolicy(members);
            var flowMode = BuildFlowMode(members);
            var contractHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteContractHashInput(
                    declaringType,
                    key.SchemaName,
                    key.Encoding,
                    fields,
                    flowMode));
            var bindingHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteBindingHashInput(
                    declaringType,
                    key.Topic,
                    key.SchemaName,
                    key.Encoding));
            var policyHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WritePolicyHashInput(policy));

            return new FoxRunManifestContract(
                declaringType,
                key.Topic,
                key.SchemaName,
                key.Encoding,
                contractHash,
                bindingHash,
                policyHash,
                fields.AsReadOnly(),
                policy,
                flowMode);
        }

        private static FoxRunManifestField BuildField(FoxRunManifestMember member)
        {
            var sourceType = ResolveFieldSourceType(member);
            var normalized = FoxRunCanonicalTypeNormalizer.NormalizeTypeName(sourceType);
            var nullable = member.IsArray
                           || FoxRunCanonicalTypeNormalizer.IsNullableType(member.TypeName)
                           || FoxRunCanonicalTypeNormalizer.IsStringType(member.TypeName)
                           || (!member.IsValueType && !FoxRunCanonicalTypeNormalizer.IsKnownUnityValueType(member.TypeName));
            return new FoxRunManifestField(
                JsonFieldName(member),
                member.MemberName,
                NormalizeMemberKind(member.MemberKind),
                normalized,
                nullable,
                member.IsArray,
                member.IsAggregateMember);
        }

        private static string ResolveFieldSourceType(FoxRunManifestMember member)
        {
            if (!member.IsArray)
                return member.TypeName;

            if (!string.IsNullOrEmpty(member.ElementTypeName))
                return member.ElementTypeName;

            var typeName = member.TypeName ?? string.Empty;
            return typeName.EndsWith("[]", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - 2)
                : typeName;
        }

        private static void ValidateJsonFieldNames(
            string declaringType,
            ContractKey key,
            IReadOnlyList<FoxRunManifestField> fields)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.JsonName))
                {
                    throw new InvalidOperationException(
                        "FoxRun manifest field JSON name is empty for " + declaringType + "." + field.MemberName);
                }

                if (!seen.Add(field.JsonName))
                {
                    throw new InvalidOperationException(
                        "FoxRun manifest field JSON name collision for " + declaringType +
                        " topic " + key.Topic + ": " + field.JsonName);
                }
            }
        }

        private static FoxRunManifestPolicy BuildPolicy(IReadOnlyList<FoxRunManifestMember> members)
        {
            return new FoxRunManifestPolicy(
                PublishModeName(TopicPublishMode(members)),
                members.Count == 0 ? 0f : members.Max(member => NormalizeRateHz(member.RateHz)),
                members.Count == 0 ? 0f : members.Max(member => NormalizeNonNegative(member.ChangeEpsilon)),
                members.Count == 0 ? 0f : members.Max(member => NormalizeNonNegative(member.ForceIntervalSeconds)));
        }

        private static string BuildFlowMode(IReadOnlyList<FoxRunManifestMember> members)
        {
            var modes = members.Select(member => member.FlowMode).Distinct().ToList();
            if (modes.Count > 1)
                throw new InvalidOperationException("FoxRun topic has mixed data-flow modes.");

            return FoxRunGenerationMember.ModeToName(modes.Count == 0 ? 0 : modes[0]);
        }

        private static float NormalizeRateHz(float rateHz)
        {
            if (float.IsNaN(rateHz) || float.IsInfinity(rateHz) || rateHz <= 0f)
                return 0f;
            return rateHz;
        }

        private static float NormalizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;
            return value;
        }

        private static int TopicPublishMode(IReadOnlyList<FoxRunManifestMember> members)
        {
            var invalid = members.FirstOrDefault(member => member.PublishMode < 0 || member.PublishMode > 3);
            if (invalid != null)
                throw new InvalidOperationException(
                    "FoxRun manifest publish mode is outside the supported range 0..3 for " +
                    DeclaringType(invalid) + "." + invalid.MemberName + ".");

            if (members.Any(member => member.PublishMode == 3))
                return 3;
            if (members.Any(member => member.PublishMode == 2))
                return 2;
            if (members.Any(member => member.PublishMode == 1))
                return 1;
            return members.Count == 0 ? 0 : members.Max(member => member.PublishMode);
        }

        private static string PublishModeName(int mode)
        {
            switch (mode)
            {
                case 0: return "FixedRate";
                case 1: return "OnChange";
                case 2: return "OnChangeOrInterval";
                case 3: return "OnTrigger";
                default: return "Unknown";
            }
        }

        private static string DeclaringType(FoxRunManifestMember member)
        {
            return string.IsNullOrEmpty(member.Namespace)
                ? member.ClassName
                : member.Namespace + "." + member.ClassName;
        }

        private static string JsonFieldName(FoxRunManifestMember member)
        {
            if (!string.IsNullOrWhiteSpace(member.JsonFieldName))
                return member.JsonFieldName;

            var memberName = member.MemberName;
            var name = memberName != null && memberName.StartsWith("@", StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? string.Empty;
            return name.TrimStart('_');
        }

        private static string NormalizeMemberKind(string memberKind)
        {
            return string.Equals(memberKind, "property", StringComparison.OrdinalIgnoreCase)
                ? "property"
                : "field";
        }

        private readonly struct ContractKey : IEquatable<ContractKey>
        {
            public readonly string Topic;
            public readonly string SchemaName;
            public readonly string Encoding;

            public ContractKey(string topic, string schemaName, string encoding)
            {
                Topic = topic ?? string.Empty;
                SchemaName = schemaName ?? string.Empty;
                Encoding = encoding ?? string.Empty;
            }

            public bool Equals(ContractKey other)
            {
                return string.Equals(Topic, other.Topic, StringComparison.Ordinal)
                       && string.Equals(SchemaName, other.SchemaName, StringComparison.Ordinal)
                       && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
                => obj is ContractKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(Topic);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SchemaName);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Encoding);
                    return hash;
                }
            }
        }
    }
}
