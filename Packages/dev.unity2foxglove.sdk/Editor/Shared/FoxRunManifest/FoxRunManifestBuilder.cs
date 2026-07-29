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
        private const string JsonEncoding = FoxRunGenerationDescriptorConstants.JsonEncoding;
        private const string ProtobufEncoding = FoxRunGenerationDescriptorConstants.ProtobufEncoding;
        private const string MessagePackEncoding = FoxRunGenerationDescriptorConstants.MessagePackEncoding;

        public static FoxRunCanonicalManifest Build(
            IReadOnlyList<FoxRunManifestMember> members,
            int manifestVersion = 1,
            int generatorMajorVersion = 1)
        {
            var source = members ?? Array.Empty<FoxRunManifestMember>();
            var types = BuildTypes(source, manifestVersion);
            var sectionHashInput = FoxRunManifestJsonWriter.WriteFoxRunSectionHashInput(types);
            var manifestHash = FoxRunManifestHasher.Sha256Hex(sectionHashInput);
            var section = new FoxRunManifestFoxRunSection(manifestHash, types);
            var discoveredSubscriptionBindings = BuildSubscriptionBindings(source);
            if (manifestVersion < 3 && discoveredSubscriptionBindings.Count > 0)
            {
                throw new InvalidOperationException(
                    "FoxRun manifest version 3 is required when any subscription binding exists; "
                    + "legacy manifest versions 1 and 2 are publish-only.");
            }
            var subscriptionBindings = manifestVersion >= 3
                ? discoveredSubscriptionBindings
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

        private static IReadOnlyList<FoxRunManifestType> BuildTypes(
            IReadOnlyList<FoxRunManifestMember> members,
            int manifestVersion)
        {
            return members
                .Where(member => member.GeneratesWebSocketCodec
                                 // A custom DTO P&S contract has native input
                                 // but still deliberately exposes its selected
                                 // JSON/Protobuf/MessagePack contract as WebSocket output.
                                 // Subscribe native contracts remain absent
                                 // so this never creates a fallback input path.
                                 && (!string.Equals(
                                         member.Source,
                                         FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                                         StringComparison.Ordinal)
                                     || member.Flow == (int)FoxRunFlow.PublishAndSubscribe))
                .GroupBy(DeclaringType)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new FoxRunManifestType(
                    group.Key,
                    BuildContracts(group.Key, group.ToList(), manifestVersion)))
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
                    member.QosProfile,
                    member.GeneratesWebSocketCodec,
                    member.GeneratesRos2NativeRegistration,
                    ResolveNativeType(member),
                    ResolvePackagedCanonicalRosType(member),
                    ResolvePackagedCopyShapeIdentity(member),
                    member.Ros2ContractKind,
                    member.Ros2CustomDtoShape?.CanonicalIdentity ?? string.Empty,
                    member.Ros2CustomDtoShape?.PayloadIdentity ?? string.Empty,
                    ResolveCustomEnvelopeIdentity(member),
                    member.Targets,
                    member.QosReliability,
                    member.QosDurability,
                    member.QosHistory,
                    member.QosDepth,
                    member.IsStream))
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
                    member.QosProfile,
                    true,
                    member.Ros2CustomDtoShape?.CanonicalIdentity ?? string.Empty,
                    member.Ros2CustomDtoShape?.PayloadIdentity ?? string.Empty,
                    ResolveCustomEnvelopeIdentity(member),
                    member.Targets,
                    member.QosReliability,
                    member.QosDurability,
                    member.QosHistory,
                    member.QosDepth))
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
            IReadOnlyList<FoxRunManifestMember> members,
            int manifestVersion)
        {
            var contracts = new List<FoxRunManifestContract>();
            var groups = members
                .SelectMany(member => ResolveEncodings(member).Select(encoding => new MemberEncodingVariant(member, encoding)))
                .GroupBy(variant => new BaseContractKey(
                    variant.Member.Topic,
                    ResolveContractSchemaName(declaringType, variant.Member, variant.Encoding),
                    variant.Encoding))
                .OrderBy(group => group.Key.Topic, StringComparer.Ordinal)
                .ThenBy(group => group.Key.SchemaName, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Encoding, StringComparer.Ordinal)
                .ToList();

            foreach (var group in groups)
            {
                var groupedMembers = group.Select(variant => variant.Member).ToList();
                var flows = groupedMembers.Select(member => member.Flow).Distinct().ToList();
                var directionScoped = string.Equals(
                                          group.Key.Encoding,
                                          MessagePackEncoding,
                                          StringComparison.Ordinal)
                                      || flows.Count > 1;
                if (!directionScoped)
                {
                    contracts.Add(BuildContract(
                        declaringType,
                        new ContractKey(
                            group.Key.Topic,
                            group.Key.SchemaName,
                            group.Key.Encoding,
                            FoxRunGenerationMember.FlowToName(flows.Count == 0 ? 1 : flows[0]),
                            directionScoped: false),
                        groupedMembers,
                        manifestVersion));
                    continue;
                }

                var publishMembers = groupedMembers
                    .Where(member => SupportsPublish(member.Flow))
                    .ToList();
                if (publishMembers.Count > 0)
                {
                    contracts.Add(BuildContract(
                        declaringType,
                        new ContractKey(
                            group.Key.Topic,
                            group.Key.SchemaName,
                            group.Key.Encoding,
                            "Publish",
                            directionScoped: true),
                        publishMembers,
                        manifestVersion));
                }

                var subscribeMembers = groupedMembers
                    .Where(member => SupportsSubscribe(member.Flow))
                    .ToList();
                if (subscribeMembers.Count > 0)
                {
                    contracts.Add(BuildContract(
                        declaringType,
                        new ContractKey(
                            group.Key.Topic,
                            group.Key.SchemaName,
                            group.Key.Encoding,
                            "Subscribe",
                            directionScoped: true),
                        subscribeMembers,
                        manifestVersion));
                }
            }

            return contracts
                .OrderBy(contract => contract.Topic, StringComparer.Ordinal)
                .ThenBy(contract => contract.SchemaName, StringComparer.Ordinal)
                .ThenBy(contract => contract.Encoding, StringComparer.Ordinal)
                .ThenBy(contract => contract.Flow, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        private static string ResolveContractSchemaName(
            string declaringType,
            FoxRunManifestMember member,
            string encoding)
        {
            if (string.Equals(encoding, MessagePackEncoding, StringComparison.Ordinal))
                return string.Empty;

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
            IReadOnlyList<FoxRunManifestMember> members,
            int manifestVersion)
        {
            var fields = members
                .Select(member => BuildField(member, key.Encoding == ProtobufEncoding))
                .OrderBy(field => field.JsonName, StringComparer.Ordinal)
                .ThenBy(field => field.MemberName, StringComparer.Ordinal)
                .ThenBy(field => field.Type, StringComparer.Ordinal)
                .ToList();
            ValidateJsonFieldNames(declaringType, key, fields);
            var policy = BuildPolicy(members);
            var flow = key.Flow;
            var logicalSchemaName = ResolveLogicalSchemaName(
                declaringType,
                members);
            var availability = ResolveAvailability(
                members,
                key.Encoding,
                key.DirectionScoped ? key.Flow : string.Empty);
            var includesTransportSelection = manifestVersion >= 4;
            var publishTransportIds = includesTransportSelection
                ? ResolvePublishTransportIds(members)
                : null;
            var subscribeTransportId = includesTransportSelection
                ? ResolveSubscribeTransportId(members)
                : null;
            var contractHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteContractHashInput(
                    declaringType,
                    key.SchemaName,
                    key.Encoding,
                    fields,
                    flow,
                    logicalSchemaName,
                    availability.PublishAvailable,
                    availability.SubscribeAvailable,
                    availability.UnavailableDiagnosticId,
                    availability.UnavailableReason,
                    availability.PublishUnavailableDiagnosticId,
                    availability.PublishUnavailableReason,
                    availability.SubscribeUnavailableDiagnosticId,
                    availability.SubscribeUnavailableReason,
                    includesTransportSelection,
                    publishTransportIds,
                    subscribeTransportId));
            var bindingHash = FoxRunManifestHasher.Sha256Hex(
                FoxRunManifestJsonWriter.WriteBindingHashInput(
                    declaringType,
                    key.Topic,
                    key.SchemaName,
                    key.Encoding,
                    key.DirectionScoped ? key.Flow : string.Empty,
                    includesTransportSelection,
                    publishTransportIds,
                    subscribeTransportId));
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
                flow,
                logicalSchemaName,
                availability.PublishAvailable,
                availability.SubscribeAvailable,
                availability.UnavailableDiagnosticId,
                availability.UnavailableReason,
                availability.PublishUnavailableDiagnosticId,
                availability.PublishUnavailableReason,
                availability.SubscribeUnavailableDiagnosticId,
                availability.SubscribeUnavailableReason,
                includesTransportSelection,
                publishTransportIds,
                subscribeTransportId);
        }

        private static IReadOnlyList<string> ResolvePublishTransportIds(
            IReadOnlyList<FoxRunManifestMember> members)
        {
            var first = members.FirstOrDefault(member =>
                member.Flow == (int)FoxRunFlow.Publish
                || member.Flow == (int)FoxRunFlow.PublishAndSubscribe);
            return first?.PublishTransportIds;
        }

        private static string ResolveSubscribeTransportId(
            IReadOnlyList<FoxRunManifestMember> members)
        {
            var first = members.FirstOrDefault(member =>
                member.Flow == (int)FoxRunFlow.Subscribe
                || member.Flow == (int)FoxRunFlow.PublishAndSubscribe);
            return first?.SubscribeTransportId;
        }

        private static FoxRunManifestField BuildField(FoxRunManifestMember member, bool includeProtobufMetadata)
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
                0,
                member.TypeShape,
                member.NormalizedSchedule,
                includeProtobufMetadata ? member.ProtobufMetadata : null);
        }

        private static string LogicalTypeName(FoxRunTypeShape shape)
        {
            while (shape != null && shape.Kind == FoxRunTypeShapeKind.Collection)
                shape = shape.ElementShape;
            return shape?.TypeName ?? string.Empty;
        }

        private static string ResolveLogicalSchemaName(
            string declaringType,
            IReadOnlyList<FoxRunManifestMember> members)
        {
            var explicitNames = members
                .Select(member => (member.SchemaName ?? string.Empty).Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            if (explicitNames.Count == 1)
                return explicitNames[0];
            if (explicitNames.Count > 1)
                return declaringType;

            var shapeNames = members
                .Select(member => (LogicalTypeName(member.TypeShape) ?? string.Empty).Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            return shapeNames.Count == 1
                ? shapeNames[0]
                : declaringType;
        }

        private static FoxRunEncodingVariantAvailability ResolveAvailability(
            IReadOnlyList<FoxRunManifestMember> members,
            string encoding,
            string directionalFlow)
        {
            var variants = members
                .SelectMany(member => member.EncodingVariants
                    .Where(variant => string.Equals(
                        variant.Encoding,
                        encoding,
                        StringComparison.Ordinal)))
                .ToList();
            if (variants.Count == 0)
            {
                var publishDirection = string.Equals(
                    directionalFlow,
                    "Publish",
                    StringComparison.Ordinal);
                var subscribeDirection = string.Equals(
                    directionalFlow,
                    "Subscribe",
                    StringComparison.Ordinal);
                return new FoxRunEncodingVariantAvailability(
                    encoding,
                    publishAvailable: false,
                    subscribeAvailable: false,
                    publishUnavailableDiagnosticId: subscribeDirection ? string.Empty : "FOXRUN616",
                    publishUnavailableReason:
                        subscribeDirection
                            ? string.Empty
                            : "The generated encoding variant has no matching availability metadata.",
                    subscribeUnavailableDiagnosticId: publishDirection ? string.Empty : "FOXRUN616",
                    subscribeUnavailableReason:
                        publishDirection
                            ? string.Empty
                            : "The generated encoding variant has no matching availability metadata.");
            }

            var publishUnavailable = variants.FirstOrDefault(variant =>
                !variant.PublishAvailable
                && !string.IsNullOrEmpty(variant.PublishUnavailableDiagnosticId));
            var subscribeUnavailable = variants.FirstOrDefault(variant =>
                !variant.SubscribeAvailable
                && !string.IsNullOrEmpty(variant.SubscribeUnavailableDiagnosticId));
            var isPublishDirection = string.Equals(
                directionalFlow,
                "Publish",
                StringComparison.Ordinal);
            var isSubscribeDirection = string.Equals(
                directionalFlow,
                "Subscribe",
                StringComparison.Ordinal);
            return new FoxRunEncodingVariantAvailability(
                encoding,
                !isSubscribeDirection && variants.All(variant => variant.PublishAvailable),
                !isPublishDirection && variants.All(variant => variant.SubscribeAvailable),
                publishUnavailableDiagnosticId:
                    isSubscribeDirection
                        ? string.Empty
                        : publishUnavailable?.PublishUnavailableDiagnosticId ?? string.Empty,
                publishUnavailableReason:
                    isSubscribeDirection
                        ? string.Empty
                        : publishUnavailable?.PublishUnavailableReason ?? string.Empty,
                subscribeUnavailableDiagnosticId:
                    isPublishDirection
                        ? string.Empty
                        : subscribeUnavailable?.SubscribeUnavailableDiagnosticId ?? string.Empty,
                subscribeUnavailableReason:
                    isPublishDirection
                        ? string.Empty
                        : subscribeUnavailable?.SubscribeUnavailableReason ?? string.Empty);
        }

        private static bool SupportsPublish(int flow)
            => flow == (int)FoxRunFlow.Publish
               || flow == (int)FoxRunFlow.PublishAndSubscribe;

        private static bool SupportsSubscribe(int flow)
            => flow == (int)FoxRunFlow.Subscribe
               || flow == (int)FoxRunFlow.PublishAndSubscribe;

        private static IEnumerable<string> ResolveEncodings(FoxRunManifestMember member)
        {
            switch (member.Encoding)
            {
                case 0:
                    yield return JsonEncoding;
                    yield return ProtobufEncoding;
                    yield return MessagePackEncoding;
                    yield break;
                case 1:
                    yield return ProtobufEncoding;
                    yield break;
                case 2:
                    yield return JsonEncoding;
                    yield break;
                case 3:
                    yield return MessagePackEncoding;
                    yield break;
                default:
                    throw new InvalidOperationException(
                        "FoxRun manifest wire encoding is outside the supported range 0..3 for "
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

        private readonly struct BaseContractKey : IEquatable<BaseContractKey>
        {
            public readonly string Topic;
            public readonly string SchemaName;
            public readonly string Encoding;

            public BaseContractKey(string topic, string schemaName, string encoding)
            {
                Topic = topic ?? string.Empty;
                SchemaName = schemaName ?? string.Empty;
                Encoding = encoding ?? string.Empty;
            }

            public bool Equals(BaseContractKey other)
            {
                return string.Equals(Topic, other.Topic, StringComparison.Ordinal)
                       && string.Equals(SchemaName, other.SchemaName, StringComparison.Ordinal)
                       && string.Equals(Encoding, other.Encoding, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
                => obj is BaseContractKey other && Equals(other);

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

        private readonly struct ContractKey
        {
            public readonly string Topic;
            public readonly string SchemaName;
            public readonly string Encoding;
            public readonly string Flow;
            public readonly bool DirectionScoped;

            public ContractKey(
                string topic,
                string schemaName,
                string encoding,
                string flow,
                bool directionScoped)
            {
                Topic = topic ?? string.Empty;
                SchemaName = schemaName ?? string.Empty;
                Encoding = encoding ?? string.Empty;
                Flow = flow ?? string.Empty;
                DirectionScoped = directionScoped;
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
