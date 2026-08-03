// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Canonical Provider-neutral FoxRun manifest model.

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Editor
{
    public sealed class FoxRunManifestMember
    {
        public string Namespace { get; }
        public string ClassName { get; }
        public string MemberName { get; }
        public string MemberKind { get; }
        public string TypeName { get; }
        public bool IsValueType { get; }
        public bool IsArray { get; }
        public string ElementTypeName { get; }
        public string Topic { get; }
        public float Hz { get; }
        public string SchemaName { get; }
        public int Policy { get; }
        public int Flow { get; }
        public int Encoding { get; }
        public FoxRunProtobufMetadata ProtobufMetadata { get; }
        public FoxRunTypeShape TypeShape { get; }
        public float Tolerance { get; }
        public bool IsAggregateMember { get; }
        public bool IsStream { get; }
        public string JsonFieldName { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public string Reliability { get; }
        public string Durability { get; }
        public string History { get; }
        public int Depth { get; }
        public bool GeneratesWebSocketCodec { get; }
        public object ProviderData { get; }
        public IReadOnlyList<FoxRunEncodingVariantAvailability>
            EncodingVariants { get; }
        public FoxRunNormalizedScheduleTuple NormalizedSchedule { get; }

        public FoxRunManifestMember(
            string ns,
            string className,
            string memberName,
            string memberKind,
            string typeName,
            bool isValueType,
            bool isArray,
            string elementTypeName,
            string topic,
            float hz,
            string schemaName,
            int policy,
            float tolerance,
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int flow = 1,
            int encoding = 2,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            bool generatesWebSocketCodec = true,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability>
                encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null,
            string reliability = "inherit",
            string durability = "inherit",
            string history = "inherit",
            int depth = 0,
            object providerData = null)
        {
            Namespace = ns ?? string.Empty;
            ClassName = className ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            IsValueType = isValueType;
            IsArray = isArray;
            ElementTypeName = elementTypeName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Hz = hz;
            SchemaName = schemaName ?? string.Empty;
            Policy = policy;
            Flow = flow;
            Encoding = encoding;
            Tolerance = tolerance;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = jsonFieldName ?? string.Empty;
            TypeShape = typeShape;
            ProtobufMetadata =
                protobufMetadata
                ?? (protobufFieldNumber == 0
                    ? null
                    : FoxRunProtobufMetadata.FromTypeShape(
                        typeShape,
                        protobufFieldNumber));
            GeneratesWebSocketCodec = generatesWebSocketCodec;
            IsStream = isStream;
            EncodingVariants = new List<
                    FoxRunEncodingVariantAvailability>(
                    encodingVariants
                    ?? DefaultEncodingVariants(
                        encoding,
                        flow))
                .AsReadOnly();
            NormalizedSchedule =
                normalizedSchedule
                ?? new FoxRunNormalizedScheduleTuple(
                    policy,
                    hz >= 0f,
                    hz,
                    tolerance,
                    string.Empty,
                    FoxRunConditionMemberKind.None);
            PublishTransportIds = CanonicalTransportIds(
                publishTransportIds);
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability ?? "inherit";
            Durability = durability ?? "inherit";
            History = history ?? "inherit";
            Depth = depth;
            ProviderData = providerData;
        }

        public static FoxRunManifestMember FromGenerationMember(
            FoxRunGenerationMember member)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));
            return new FoxRunManifestMember(
                member.Namespace,
                member.ClassName,
                member.MemberName,
                member.MemberKind,
                member.EmissionTypeName,
                member.IsValueType,
                member.IsArray,
                member.ElementTypeName,
                member.Topic,
                member.Hz,
                member.SchemaName,
                member.Policy,
                member.Tolerance,
                member.IsAggregateMember,
                member.JsonFieldName,
                member.Mode,
                EncodingToValue(member.Encoding),
                member.ProtobufMetadata?.FieldNumber ?? 0,
                member.TypeShape,
                member.GeneratesWebSocketCodec,
                member.IsStream,
                member.EncodingVariants,
                member.NormalizedSchedule,
                member.ProtobufMetadata,
                member.PublishTransportIds,
                member.SubscribeTransportId,
                member.Reliability,
                member.Durability,
                member.History,
                member.Depth,
                member.ProviderData);
        }

        private static int EncodingToValue(string encoding)
        {
            switch (encoding)
            {
                case "protobuf": return 1;
                case "json": return 2;
                case "msgpack": return 3;
                default: return 0;
            }
        }

        private static IReadOnlyList<
            FoxRunEncodingVariantAvailability>
            DefaultEncodingVariants(
                int encoding,
                int flow)
        {
            var publish =
                flow == (int)FoxRunFlow.Publish
                || flow
                == (int)FoxRunFlow.PublishAndSubscribe;
            var subscribe =
                flow == (int)FoxRunFlow.Subscribe
                || flow
                == (int)FoxRunFlow.PublishAndSubscribe;
            if (encoding == 0)
            {
                return new[]
                {
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants
                            .JsonEncoding,
                        publish,
                        subscribe),
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants
                            .ProtobufEncoding,
                        publish,
                        subscribe),
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants
                            .MessagePackEncoding,
                        publish,
                        subscribe)
                };
            }

            return new[]
            {
                new FoxRunEncodingVariantAvailability(
                    EncodingText(encoding),
                    publish,
                    subscribe)
            };
        }

        private static string EncodingText(int encoding)
        {
            switch (encoding)
            {
                case 1:
                    return FoxRunGenerationDescriptorConstants
                        .ProtobufEncoding;
                case 2:
                    return FoxRunGenerationDescriptorConstants
                        .JsonEncoding;
                case 3:
                    return FoxRunGenerationDescriptorConstants
                        .MessagePackEncoding;
                default:
                    return string.Empty;
            }
        }

        private static IReadOnlyList<string> CanonicalTransportIds(
            IReadOnlyList<string> values)
        {
            if (values == null)
                return null;
            var copy = values
                .Select(value => value ?? string.Empty)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return Array.AsReadOnly(copy);
        }
    }

    public sealed class FoxRunCanonicalManifest
    {
        public int ManifestVersion { get; }
        public string Package { get; }
        public FoxRunManifestGenerator Generator { get; }
        public FoxRunManifestSections Sections { get; }
        public string GlobalManifestHash { get; }

        public FoxRunCanonicalManifest(
            int manifestVersion,
            string packageName,
            FoxRunManifestGenerator generator,
            FoxRunManifestSections sections,
            string globalManifestHash)
        {
            ManifestVersion = manifestVersion;
            Package = packageName ?? string.Empty;
            Generator =
                generator
                ?? throw new ArgumentNullException(nameof(generator));
            Sections =
                sections
                ?? throw new ArgumentNullException(nameof(sections));
            GlobalManifestHash = globalManifestHash ?? string.Empty;
        }
    }

    public sealed class FoxRunManifestGenerator
    {
        public string Name { get; }
        public int MajorVersion { get; }

        public FoxRunManifestGenerator(
            string name,
            int majorVersion)
        {
            Name = name ?? string.Empty;
            MajorVersion = majorVersion;
        }
    }

    public sealed class FoxRunManifestSections
    {
        public FoxRunManifestFoxRunSection FoxRun { get; }
        public FoxRunManifestSubscriptionSection Subscriptions { get; }

        public FoxRunManifestSections(
            FoxRunManifestFoxRunSection foxRun)
            : this(
                foxRun,
                new FoxRunManifestSubscriptionSection(
                    string.Empty,
                    Array.Empty<
                        FoxRunManifestSubscriptionBinding>()))
        {
        }

        public FoxRunManifestSections(
            FoxRunManifestFoxRunSection foxRun,
            FoxRunManifestSubscriptionSection subscriptions)
        {
            FoxRun =
                foxRun
                ?? throw new ArgumentNullException(nameof(foxRun));
            Subscriptions =
                subscriptions
                ?? throw new ArgumentNullException(
                    nameof(subscriptions));
        }
    }

    public sealed class FoxRunManifestSubscriptionSection
    {
        public string ManifestHash { get; }
        public IReadOnlyList<FoxRunManifestSubscriptionBinding>
            Bindings { get; }

        public FoxRunManifestSubscriptionSection(
            string manifestHash,
            IReadOnlyList<FoxRunManifestSubscriptionBinding>
                bindings)
        {
            ManifestHash = manifestHash ?? string.Empty;
            Bindings = new List<
                    FoxRunManifestSubscriptionBinding>(
                    bindings
                    ?? Array.Empty<
                        FoxRunManifestSubscriptionBinding>())
                .AsReadOnly();
        }
    }

    public sealed class FoxRunManifestSubscriptionBinding
    {
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public string Reliability { get; }
        public string Durability { get; }
        public string History { get; }
        public int Depth { get; }
        public bool SupportsWebSocket { get; }
        public bool IsStream { get; }

        public FoxRunManifestSubscriptionBinding(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            IReadOnlyList<string> publishTransportIds,
            string subscribeTransportId,
            string reliability,
            string durability,
            string history,
            int depth,
            bool supportsWebSocket,
            bool isStream)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            Reliability = reliability ?? "inherit";
            Durability = durability ?? "inherit";
            History = history ?? "inherit";
            Depth = depth;
            SupportsWebSocket = supportsWebSocket;
            IsStream = isStream;
        }
    }

    public sealed class FoxRunManifestFoxRunSection
    {
        public string ManifestHash { get; }
        public IReadOnlyList<FoxRunManifestType> Types { get; }

        public FoxRunManifestFoxRunSection(
            string manifestHash,
            IReadOnlyList<FoxRunManifestType> types)
        {
            ManifestHash = manifestHash ?? string.Empty;
            Types = new List<FoxRunManifestType>(
                    types
                    ?? Array.Empty<FoxRunManifestType>())
                .AsReadOnly();
        }
    }

    public sealed class FoxRunManifestType
    {
        public string DeclaringType { get; }
        public IReadOnlyList<FoxRunManifestContract> Contracts { get; }

        public FoxRunManifestType(
            string declaringType,
            IReadOnlyList<FoxRunManifestContract> contracts)
        {
            DeclaringType = declaringType ?? string.Empty;
            Contracts = new List<FoxRunManifestContract>(
                    contracts
                    ?? Array.Empty<FoxRunManifestContract>())
                .AsReadOnly();
        }
    }

    public sealed class FoxRunManifestContract
    {
        public string DeclaringType { get; }
        public string Topic { get; }
        public string SchemaName { get; }
        public string WireSchemaName => SchemaName;
        public string LogicalSchemaName { get; }
        public string Encoding { get; }
        public bool IncludesTransportSelection { get; }
        public IReadOnlyList<string> PublishTransportIds { get; }
        public string SubscribeTransportId { get; }
        public string ContractHash { get; }
        public string BindingHash { get; }
        public string PolicyHash { get; }
        public string Flow { get; }
        public IReadOnlyList<FoxRunManifestField> Fields { get; }
        public FoxRunManifestPolicy Policy { get; }
        public bool PublishAvailable { get; }
        public bool SubscribeAvailable { get; }
        public string PublishUnavailableDiagnosticId { get; }
        public string PublishUnavailableReason { get; }
        public string SubscribeUnavailableDiagnosticId { get; }
        public string SubscribeUnavailableReason { get; }
        public string UnavailableDiagnosticId
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableDiagnosticId,
                SubscribeAvailable,
                SubscribeUnavailableDiagnosticId);
        public string UnavailableReason
            => SharedUnavailableValue(
                PublishAvailable,
                PublishUnavailableReason,
                SubscribeAvailable,
                SubscribeUnavailableReason);

        public FoxRunManifestContract(
            string declaringType,
            string topic,
            string schemaName,
            string encoding,
            string contractHash,
            string bindingHash,
            string policyHash,
            IReadOnlyList<FoxRunManifestField> fields,
            FoxRunManifestPolicy policy,
            string flow = "Publish",
            string logicalSchemaName = "",
            bool publishAvailable = true,
            bool subscribeAvailable = true,
            string unavailableDiagnosticId = "",
            string unavailableReason = "",
            string publishUnavailableDiagnosticId = null,
            string publishUnavailableReason = null,
            string subscribeUnavailableDiagnosticId = null,
            string subscribeUnavailableReason = null,
            bool includesTransportSelection = false,
            IReadOnlyList<string> publishTransportIds = null,
            string subscribeTransportId = null)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            IncludesTransportSelection = includesTransportSelection;
            PublishTransportIds = publishTransportIds == null
                ? null
                : Array.AsReadOnly(
                    publishTransportIds
                        .OrderBy(
                            value => value,
                            StringComparer.Ordinal)
                        .ToArray());
            SubscribeTransportId = subscribeTransportId;
            ContractHash = contractHash ?? string.Empty;
            BindingHash = bindingHash ?? string.Empty;
            PolicyHash = policyHash ?? string.Empty;
            Flow = string.IsNullOrWhiteSpace(flow)
                ? "Publish"
                : flow;
            Fields = new List<FoxRunManifestField>(
                    fields
                    ?? Array.Empty<FoxRunManifestField>())
                .AsReadOnly();
            Policy =
                policy
                ?? throw new ArgumentNullException(nameof(policy));
            PublishAvailable = publishAvailable;
            SubscribeAvailable = subscribeAvailable;
            PublishUnavailableDiagnosticId = publishAvailable
                ? string.Empty
                : publishUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId
                  ?? unavailableDiagnosticId
                  ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason
                  ?? unavailableReason
                  ?? string.Empty;
        }

        private static string SharedUnavailableValue(
            bool publishAvailable,
            string publishValue,
            bool subscribeAvailable,
            string subscribeValue)
        {
            if (publishAvailable)
                return subscribeAvailable
                    ? string.Empty
                    : subscribeValue;
            if (subscribeAvailable)
                return publishValue;
            if (string.IsNullOrEmpty(publishValue))
                return subscribeValue;
            if (string.IsNullOrEmpty(subscribeValue))
                return publishValue;
            return string.Equals(
                publishValue,
                subscribeValue,
                StringComparison.Ordinal)
                ? publishValue
                : string.Empty;
        }
    }

    public sealed class FoxRunManifestField
    {
        public string JsonName { get; }
        public string MemberName { get; }
        public string MemberKind { get; }
        public string Type { get; }
        public bool Nullable { get; }
        public bool Array { get; }
        public bool Aggregate { get; }
        public FoxRunProtobufMetadata ProtobufMetadata { get; }
        public FoxRunTypeShape TypeShape { get; }
        public FoxRunNormalizedScheduleTuple NormalizedSchedule { get; }

        public FoxRunManifestField(
            string jsonName,
            string memberName,
            string memberKind,
            string type,
            bool nullable,
            bool array,
            bool aggregate = false,
            int protobufFieldNumber = 0,
            FoxRunTypeShape typeShape = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            Type = type ?? string.Empty;
            Nullable = nullable;
            Array = array;
            Aggregate = aggregate;
            TypeShape = typeShape;
            ProtobufMetadata =
                protobufMetadata
                ?? (protobufFieldNumber == 0
                    ? null
                    : FoxRunProtobufMetadata.FromTypeShape(
                        typeShape,
                        protobufFieldNumber));
            NormalizedSchedule = normalizedSchedule;
        }
    }

    public sealed class FoxRunManifestPolicy
    {
        public string Mode { get; }
        public float Hz { get; }
        public float Tolerance { get; }

        public FoxRunManifestPolicy(
            string mode,
            float hz,
            float tolerance)
        {
            Mode = mode ?? string.Empty;
            Hz = hz;
            Tolerance = tolerance;
        }
    }
}
