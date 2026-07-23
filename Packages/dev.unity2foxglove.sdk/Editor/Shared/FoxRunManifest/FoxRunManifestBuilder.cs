// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Builds deterministic FoxRun canonical manifests from resolved members.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunManifestBuilder
    {
        private const string PackageName = "Unity2Foxglove";
        private const string GeneratorName = "FoxRun";
        private const string JsonEncoding = "json";
        private const string ProtobufEncoding = "protobuf";

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
            var subscriptionBindings = manifestVersion >= 2
                ? BuildSubscriptionBindings(source)
                : Array.Empty<FoxRunManifestSubscriptionBinding>();
            var subscriptionHash = manifestVersion >= 2
                ? FoxRunManifestHasher.Sha256Hex(
                    FoxRunManifestJsonWriter.WriteSubscriptionSectionHashInput(subscriptionBindings))
                : string.Empty;
            var subscriptions = new FoxRunManifestSubscriptionSection(
                subscriptionHash,
                subscriptionBindings);
            var customNativeContracts = BuildCustomNativeContracts(source);
            var sections = new FoxRunManifestSections(section, subscriptions);
            var generator = new FoxRunManifestGenerator(GeneratorName, generatorMajorVersion);
            var globalHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteGlobalHashInput(
                    manifestVersion,
                    PackageName,
                    generator,
                    manifestHash,
                    manifestVersion >= 2 ? subscriptionHash : null));
            return new FoxRunCanonicalManifest(
                manifestVersion,
                PackageName,
                generator,
                sections,
                globalHash,
                customNativeContracts);
        }

        private static IReadOnlyList<FoxRunManifestType> BuildTypes(IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .Where(member => member.GeneratesWebSocketCodec
                                 // A custom DTO P&S contract has native input
                                 // but still deliberately exposes its selected
                                 // JSON/Protobuf contract as WebSocket output.
                                 // Subscribe native contracts remain absent
                                 // so this never creates a fallback input path.
                                 && (!string.Equals(
                                         member.Source,
                                         FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                                         StringComparison.Ordinal)
                                     || member.Flow == (int)FoxRunFlow.PublishAndSubscribe))
                .GroupBy(DeclaringType)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new FoxRunManifestType(group.Key, BuildContracts(group.Key, group.ToList())))
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<FoxRunManifestSubscriptionBinding> BuildSubscriptionBindings(
            IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .Where(member => member.Flow == (int)FoxRunFlow.Subscribe
                                 || member.Flow == (int)FoxRunFlow.PublishAndSubscribe)
                .Select(member => new FoxRunManifestSubscriptionBinding(
                    DeclaringType(member),
                    member.MemberName,
                    member.Topic,
                    FoxRunGenerationMember.FlowToName(member.Flow),
                    member.Source,
                    member.Ros2Qos,
                    member.GeneratesWebSocketCodec,
                    member.GeneratesRos2NativeRegistration,
                    ResolveNativeType(member),
                    ResolvePackagedCanonicalRosType(member),
                    ResolvePackagedCopyShapeIdentity(member),
                    member.Ros2ContractKind,
                    member.Ros2CustomDtoShape?.CanonicalIdentity ?? string.Empty,
                    member.Ros2CustomDtoShape?.PayloadIdentity ?? string.Empty,
                    ResolveCustomEnvelopeIdentity(member),
                    member.Targets))
                .OrderBy(binding => binding.DeclaringType, StringComparer.Ordinal)
                .ThenBy(binding => binding.Topic, StringComparer.Ordinal)
                .ThenBy(binding => binding.MemberName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyList<FoxRunManifestCustomNativeContract> BuildCustomNativeContracts(
            IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .Where(member => member.GeneratesRos2NativeRegistration
                                 && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto)
                .Select(member => new FoxRunManifestCustomNativeContract(
                    DeclaringType(member),
                    member.MemberName,
                    member.Topic,
                    FoxRunGenerationMember.FlowToName(member.Flow),
                    member.Source,
                    member.Ros2Qos,
                    true,
                    member.Ros2CustomDtoShape?.CanonicalIdentity ?? string.Empty,
                    member.Ros2CustomDtoShape?.PayloadIdentity ?? string.Empty,
                    ResolveCustomEnvelopeIdentity(member),
                    member.Targets))
                .OrderBy(contract => contract.DeclaringType, StringComparer.Ordinal)
                .ThenBy(contract => contract.Topic, StringComparer.Ordinal)
                .ThenBy(contract => contract.MemberName, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static string ResolveNativeType(FoxRunManifestMember member)
        {
            if (!member.GeneratesRos2NativeRegistration)
                return string.Empty;

            return member.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message
                ? member.Ros2MessageShape?.FullyQualifiedTypeName ?? member.TypeName
                : member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
                    ? member.Ros2CustomDtoShape?.FullyQualifiedTypeName ?? member.TypeName
                    : member.TypeName;
        }

        private static string ResolvePackagedCanonicalRosType(FoxRunManifestMember member)
            => member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message
                ? member.Ros2MessageShape?.CanonicalRosType ?? string.Empty
                : string.Empty;

        private static string ResolvePackagedCopyShapeIdentity(FoxRunManifestMember member)
            => member.GeneratesRos2NativeRegistration
               && member.Ros2ContractKind == FoxRunRos2ContractKind.PackagedRos2Message
                ? member.Ros2MessageShape?.CopyShapeIdentity ?? string.Empty
                : string.Empty;

        private static string ResolveCustomEnvelopeIdentity(FoxRunManifestMember member)
        {
            if (!member.GeneratesRos2NativeRegistration
                || member.Ros2ContractKind != FoxRunRos2ContractKind.CustomDto
                || string.IsNullOrWhiteSpace(member.Ros2CustomDtoShape?.PayloadIdentity))
            {
                return string.Empty;
            }

            return FoxRunRos2InterfaceIdentity.BuildEnvelopeMessageName(member.Ros2CustomDtoShape.PayloadIdentity);
        }

        private static IReadOnlyList<FoxRunManifestContract> BuildContracts(
            string declaringType,
            IReadOnlyList<FoxRunManifestMember> members)
        {
            return members
                .SelectMany(member => ResolveEncodings(member).Select(encoding => new MemberEncodingVariant(member, encoding)))
                .GroupBy(variant => new ContractKey(
                    variant.Member.Topic,
                    ResolveContractSchemaName(declaringType, variant.Member, variant.Encoding),
                    variant.Encoding))
                .OrderBy(group => group.Key.Topic, StringComparer.Ordinal)
                .ThenBy(group => group.Key.SchemaName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Encoding, StringComparer.Ordinal)
                .Select(group => BuildContract(declaringType, group.Key, group.Select(variant => variant.Member).ToList()))
                .ToList()
                .AsReadOnly();
        }

        private static string ResolveContractSchemaName(
            string declaringType,
            FoxRunManifestMember member,
            string encoding)
        {
            if (!string.Equals(encoding, ProtobufEncoding, StringComparison.Ordinal))
                return member.SchemaName ?? string.Empty;

            return FoxRunProtobufContractBuilder.ResolveMessageFullName(
                member.SchemaName,
                declaringType,
                member.Topic);
        }

        private static FoxRunManifestContract BuildContract(
            string declaringType,
            ContractKey key,
            IReadOnlyList<FoxRunManifestMember> members)
        {
            var fields = members
                .Select(member => BuildField(member, key.Encoding == ProtobufEncoding))
                .OrderBy(field => field.JsonName, StringComparer.Ordinal)
                .ThenBy(field => field.MemberName, StringComparer.Ordinal)
                .ThenBy(field => field.Type, StringComparer.Ordinal)
                .ToList();
            ValidateJsonFieldNames(declaringType, key, fields);
            var policy = BuildPolicy(members);
            var flow = BuildFlow(members);
            var contractHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteContractHashInput(
                    declaringType,
                    key.SchemaName,
                    key.Encoding,
                    fields,
                    flow));
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
                flow);
        }

        private static FoxRunManifestField BuildField(FoxRunManifestMember member, bool includeProtobufFieldNumber)
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
                member.IsAggregateMember,
                includeProtobufFieldNumber ? member.ProtobufFieldNumber : 0,
                includeProtobufFieldNumber ? member.ProtobufTypeShape : null);
        }

        private static IEnumerable<string> ResolveEncodings(FoxRunManifestMember member)
        {
            switch (member.Encoding)
            {
                case 0:
                    yield return JsonEncoding;
                    yield return ProtobufEncoding;
                    yield break;
                case 1:
                    yield return ProtobufEncoding;
                    yield break;
                case 2:
                    yield return JsonEncoding;
                    yield break;
                default:
                    throw new InvalidOperationException(
                        "FoxRun manifest wire encoding is outside the supported range 0..2 for "
                        + DeclaringType(member) + "." + member.MemberName + ".");
            }
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
                PolicyName(TopicPolicy(members)),
                members.Count == 0 ? 0f : members.Max(member => NormalizeHz(member.Hz)),
                members.Count == 0 ? 0f : members.Max(member => NormalizeNonNegative(member.Tolerance)));
        }

        private static string BuildFlow(IReadOnlyList<FoxRunManifestMember> members)
        {
            var modes = members.Select(member => member.Flow).Distinct().ToList();
            if (modes.Count > 1)
                throw new InvalidOperationException("FoxRun topic has mixed data-flow modes.");

            return FoxRunGenerationMember.FlowToName(modes.Count == 0 ? 1 : modes[0]);
        }

        private static float NormalizeHz(float hz)
        {
            return !float.IsNaN(hz) && !float.IsInfinity(hz) && hz > 0f
                ? hz
                : 10f;
        }

        private static float NormalizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                return 0f;
            return value;
        }

        private static int TopicPolicy(IReadOnlyList<FoxRunManifestMember> members)
        {
            var invalid = members.FirstOrDefault(member =>
                member.Policy != 1 && member.Policy != 2 && member.Policy != 4);
            if (invalid != null)
                throw new InvalidOperationException(
                    "FoxRun manifest Policy must be FixedRate, Change, or Trigger for " +
                    DeclaringType(invalid) + "." + invalid.MemberName + ".");

            if (members.Any(member => member.Policy == 4))
                return 4;
            if (members.Any(member => member.Policy == 2))
                return 2;
            return members.Count == 0 ? 1 : members.Max(member => member.Policy);
        }

        private static string PolicyName(int policy)
        {
            switch (policy)
            {
                case 1: return "FixedRate";
                case 2: return "Change";
                case 4: return "Trigger";
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

        private readonly struct MemberEncodingVariant
        {
            public readonly FoxRunManifestMember Member;
            public readonly string Encoding;

            public MemberEncodingVariant(FoxRunManifestMember member, string encoding)
            {
                Member = member ?? throw new ArgumentNullException(nameof(member));
                Encoding = encoding ?? string.Empty;
            }
        }
    }
}
