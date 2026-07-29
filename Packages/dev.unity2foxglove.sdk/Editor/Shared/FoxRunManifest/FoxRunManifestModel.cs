// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Host-independent DTOs for the FoxRun canonical manifest.

using System;
using System.Collections.Generic;

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
        public string Source { get; }
        public string Targets { get; }
        public string QosProfile { get; }
        public string QosReliability { get; }
        public string QosDurability { get; }
        public string QosHistory { get; }
        public int QosDepth { get; }
        public bool GeneratesWebSocketCodec { get; }
        public bool GeneratesRos2NativeRegistration { get; }
        public FoxRunRos2MessageShape Ros2MessageShape { get; }
        public FoxRunRos2ContractKind Ros2ContractKind { get; }
        public FoxRunRos2CustomDtoShape Ros2CustomDtoShape { get; }
        public IReadOnlyList<FoxRunEncodingVariantAvailability> EncodingVariants { get; }
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
            string source = FoxRunGenerationDescriptorConstants.InheritSource,
            string qosProfile = FoxRunGenerationDescriptorConstants.InheritQosProfile,
            bool generatesWebSocketCodec = true,
            bool generatesRos2NativeRegistration = false,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            string targets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0,
            bool isStream = false,
            IReadOnlyList<FoxRunEncodingVariantAvailability> encodingVariants = null,
            FoxRunNormalizedScheduleTuple normalizedSchedule = null,
            FoxRunProtobufMetadata protobufMetadata = null)
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
            TypeShape = typeShape;
            ProtobufMetadata = protobufMetadata
                               ?? FoxRunProtobufMetadata.FromTypeShape(
                                   typeShape,
                                   protobufFieldNumber);
            Tolerance = tolerance;
            IsAggregateMember = isAggregateMember;
            IsStream = isStream;
            JsonFieldName = jsonFieldName ?? string.Empty;
            Source = source ?? FoxRunGenerationDescriptorConstants.InheritSource;
            Targets = targets ?? FoxRunGenerationDescriptorConstants.InheritTargets;
            QosProfile = qosProfile ?? FoxRunGenerationDescriptorConstants.InheritQosProfile;
            QosReliability = qosReliability ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
            QosDurability = qosDurability ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
            QosHistory = qosHistory ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
            QosDepth = qosDepth;
            GeneratesWebSocketCodec = generatesWebSocketCodec;
            GeneratesRos2NativeRegistration = generatesRos2NativeRegistration;
            Ros2MessageShape = ros2MessageShape;
            Ros2CustomDtoShape = ros2CustomDtoShape;
            Ros2ContractKind = ResolveRos2ContractKind(
                ros2ContractKind,
                ros2MessageShape,
                ros2CustomDtoShape);
            EncodingVariants = new List<FoxRunEncodingVariantAvailability>(
                encodingVariants ?? DefaultEncodingVariants(encoding, flow)).AsReadOnly();
            NormalizedSchedule = normalizedSchedule ?? new FoxRunNormalizedScheduleTuple(
                policy,
                hz >= 0f,
                hz,
                tolerance,
                string.Empty,
                FoxRunConditionMemberKind.None);
        }

        private static FoxRunRos2ContractKind ResolveRos2ContractKind(
            FoxRunRos2ContractKind declared,
            FoxRunRos2MessageShape packagedShape,
            FoxRunRos2CustomDtoShape customShape)
        {
            if (declared != FoxRunRos2ContractKind.Unsupported)
                return declared;

            // The pre-181 constructor accepted a packaged message shape but
            // had no contract-kind argument. Preserve that public call shape
            // instead of silently erasing its canonical ROS metadata.
            if (packagedShape != null)
                return FoxRunRos2ContractKind.PackagedRos2Message;

            return customShape != null
                ? FoxRunRos2ContractKind.CustomDto
                : FoxRunRos2ContractKind.Unsupported;
        }

        /// <summary>
        /// Projects the host-neutral generation member into canonical manifest
        /// input. Both Roslyn and reflection hosts use the same normalized
        /// provider, capability, QoS, and native-copy-shape values here.
        /// </summary>
        public static FoxRunManifestMember FromGenerationMember(FoxRunGenerationMember member)
        {
            if (member == null)
                throw new ArgumentNullException(nameof(member));

            return new FoxRunManifestMember(
                member.Namespace,
                member.ClassName,
                member.MemberName,
                member.MemberKind,
                member.RawObservedTypeName,
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
                EncodingValue(member.Encoding),
                0,
                member.TypeShape,
                member.Source,
                member.QosProfile,
                member.GeneratesWebSocketCodec,
                member.GeneratesRos2NativeRegistration,
                member.Ros2MessageShape,
                member.Ros2CustomDtoShape,
                member.Ros2ContractKind,
                member.Targets,
                member.QosReliability,
                member.QosDurability,
                member.QosHistory,
                member.QosDepth,
                member.IsStream,
                member.EncodingVariants,
                member.NormalizedSchedule,
                member.ProtobufMetadata);
        }

        private static IReadOnlyList<FoxRunEncodingVariantAvailability> DefaultEncodingVariants(
            int encoding,
            int flow)
        {
            var publish = flow == 1 || flow == 3;
            var subscribe = flow == 2 || flow == 3;
            if (encoding == 0)
            {
                return new[]
                {
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants.JsonEncoding,
                        publish,
                        subscribe),
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                        publish,
                        subscribe),
                    new FoxRunEncodingVariantAvailability(
                        FoxRunGenerationDescriptorConstants.MessagePackEncoding,
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
                case 1: return FoxRunGenerationDescriptorConstants.ProtobufEncoding;
                case 2: return FoxRunGenerationDescriptorConstants.JsonEncoding;
                case 3: return FoxRunGenerationDescriptorConstants.MessagePackEncoding;
                default: return string.Empty;
            }
        }

        private static int EncodingValue(string encoding)
        {
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                return 0;
            }
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    StringComparison.Ordinal))
            {
                return 1;
            }
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.JsonEncoding,
                    StringComparison.Ordinal))
            {
                return 2;
            }
            if (string.Equals(
                    encoding,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    StringComparison.Ordinal))
            {
                return 3;
            }
            return -1;
        }
    }

    public sealed class FoxRunCanonicalManifest
    {
        public int ManifestVersion { get; }
        public string Package { get; }
        public FoxRunManifestGenerator Generator { get; }
        public FoxRunManifestSections Sections { get; }
        public string GlobalManifestHash { get; }
        /// <summary>
        /// Generated-code metadata for supported custom native contracts.
        /// This is intentionally outside the WebSocket manifest sections:
        /// custom interface source/add-on coherence is authenticated by the
        /// Phase181 interface digest rather than by a Foxglove wire hash.
        /// </summary>
        public IReadOnlyList<FoxRunManifestCustomNativeContract> CustomNativeContracts { get; }

        public FoxRunCanonicalManifest(
            int manifestVersion,
            string packageName,
            FoxRunManifestGenerator generator,
            FoxRunManifestSections sections,
            string globalManifestHash,
            IReadOnlyList<FoxRunManifestCustomNativeContract> customNativeContracts = null)
        {
            ManifestVersion = manifestVersion;
            Package = packageName ?? string.Empty;
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
            GlobalManifestHash = globalManifestHash ?? string.Empty;
            CustomNativeContracts = new List<FoxRunManifestCustomNativeContract>(
                customNativeContracts ?? Array.Empty<FoxRunManifestCustomNativeContract>()).AsReadOnly();
        }
    }

    public sealed class FoxRunManifestGenerator
    {
        public string Name { get; }
        public int MajorVersion { get; }

        public FoxRunManifestGenerator(string name, int majorVersion)
        {
            Name = name ?? string.Empty;
            MajorVersion = majorVersion;
        }
    }

    public sealed class FoxRunManifestSections
    {
        public FoxRunManifestFoxRunSection FoxRun { get; }
        public FoxRunManifestSubscriptionSection Subscriptions { get; }

        public FoxRunManifestSections(FoxRunManifestFoxRunSection foxRun)
            : this(foxRun, new FoxRunManifestSubscriptionSection(string.Empty, Array.Empty<FoxRunManifestSubscriptionBinding>()))
        {
        }

        public FoxRunManifestSections(
            FoxRunManifestFoxRunSection foxRun,
            FoxRunManifestSubscriptionSection subscriptions)
        {
            FoxRun = foxRun ?? throw new ArgumentNullException(nameof(foxRun));
            Subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
        }
    }

    public sealed class FoxRunManifestSubscriptionSection
    {
        public string ManifestHash { get; }
        public IReadOnlyList<FoxRunManifestSubscriptionBinding> Bindings { get; }

        public FoxRunManifestSubscriptionSection(
            string manifestHash,
            IReadOnlyList<FoxRunManifestSubscriptionBinding> bindings)
        {
            ManifestHash = manifestHash ?? string.Empty;
            Bindings = new List<FoxRunManifestSubscriptionBinding>(
                bindings ?? Array.Empty<FoxRunManifestSubscriptionBinding>()).AsReadOnly();
        }
    }

    public sealed class FoxRunManifestSubscriptionBinding
    {
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public string DeclaredSource { get; }
        public string DeclaredTargets { get; }
        public string QosProfile { get; }
        public string QosReliability { get; }
        public string QosDurability { get; }
        public string QosHistory { get; }
        public int QosDepth { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
        public bool IsStream { get; }
        public string NativeType { get; }
        public string CanonicalRosType { get; }
        public string CopyShapeIdentity { get; }
        public FoxRunRos2ContractKind Ros2ContractKind { get; }
        public string CustomDtoIdentity { get; }
        public string CustomPayloadIdentity { get; }
        public string CustomEnvelopeIdentity { get; }

        public FoxRunManifestSubscriptionBinding(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            string declaredSource,
            string qosProfile,
            bool supportsWebSocket,
            bool supportsRos2Native,
            string nativeType,
            string canonicalRosType,
            string copyShapeIdentity,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            string customDtoIdentity = "",
            string customPayloadIdentity = "",
            string customEnvelopeIdentity = "",
            string declaredTargets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0,
            bool isStream = false)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            DeclaredSource = declaredSource ?? string.Empty;
            DeclaredTargets = declaredTargets ?? FoxRunGenerationDescriptorConstants.InheritTargets;
            QosProfile = qosProfile ?? string.Empty;
            QosReliability = qosReliability ?? string.Empty;
            QosDurability = qosDurability ?? string.Empty;
            QosHistory = qosHistory ?? string.Empty;
            QosDepth = qosDepth;
            SupportsWebSocket = supportsWebSocket;
            SupportsRos2Native = supportsRos2Native;
            IsStream = isStream;
            NativeType = nativeType ?? string.Empty;
            CanonicalRosType = canonicalRosType ?? string.Empty;
            CopyShapeIdentity = copyShapeIdentity ?? string.Empty;
            Ros2ContractKind = ros2ContractKind;
            CustomDtoIdentity = customDtoIdentity ?? string.Empty;
            CustomPayloadIdentity = customPayloadIdentity ?? string.Empty;
            CustomEnvelopeIdentity = customEnvelopeIdentity ?? string.Empty;
        }
    }

    /// <summary>
    /// Direction-neutral manifest metadata for a supported custom DTO that has
    /// a generated ROS2 native registration. This is intentionally not named a
    /// subscription binding because Publish custom contracts are included.
    /// </summary>
    public sealed class FoxRunManifestCustomNativeContract
    {
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public string DeclaredSource { get; }
        public string DeclaredTargets { get; }
        public string QosProfile { get; }
        public string QosReliability { get; }
        public string QosDurability { get; }
        public string QosHistory { get; }
        public int QosDepth { get; }
        public bool SupportsRos2Native { get; }
        public string CustomDtoIdentity { get; }
        public string CustomPayloadIdentity { get; }
        public string CustomEnvelopeIdentity { get; }

        public FoxRunManifestCustomNativeContract(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            string declaredSource,
            string qosProfile,
            bool supportsRos2Native,
            string customDtoIdentity,
            string customPayloadIdentity,
            string customEnvelopeIdentity,
            string declaredTargets = FoxRunGenerationDescriptorConstants.InheritTargets,
            string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
            int qosDepth = 0)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            DeclaredSource = declaredSource ?? string.Empty;
            DeclaredTargets = declaredTargets ?? FoxRunGenerationDescriptorConstants.InheritTargets;
            QosProfile = qosProfile ?? string.Empty;
            QosReliability = qosReliability ?? string.Empty;
            QosDurability = qosDurability ?? string.Empty;
            QosHistory = qosHistory ?? string.Empty;
            QosDepth = qosDepth;
            SupportsRos2Native = supportsRos2Native;
            CustomDtoIdentity = customDtoIdentity ?? string.Empty;
            CustomPayloadIdentity = customPayloadIdentity ?? string.Empty;
            CustomEnvelopeIdentity = customEnvelopeIdentity ?? string.Empty;
        }
    }

    public sealed class FoxRunManifestFoxRunSection
    {
        public string ManifestHash { get; }
        public IReadOnlyList<FoxRunManifestType> Types { get; }

        public FoxRunManifestFoxRunSection(string manifestHash, IReadOnlyList<FoxRunManifestType> types)
        {
            ManifestHash = manifestHash ?? string.Empty;
            Types = Copy(types);
        }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            return new List<T>(values ?? Array.Empty<T>()).AsReadOnly();
        }
    }

    public sealed class FoxRunManifestType
    {
        public string DeclaringType { get; }
        public IReadOnlyList<FoxRunManifestContract> Contracts { get; }

        public FoxRunManifestType(string declaringType, IReadOnlyList<FoxRunManifestContract> contracts)
        {
            DeclaringType = declaringType ?? string.Empty;
            Contracts = new List<FoxRunManifestContract>(contracts ?? Array.Empty<FoxRunManifestContract>()).AsReadOnly();
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
            string subscribeUnavailableReason = null)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            ContractHash = contractHash ?? string.Empty;
            BindingHash = bindingHash ?? string.Empty;
            PolicyHash = policyHash ?? string.Empty;
            Flow = string.IsNullOrWhiteSpace(flow) ? "Publish" : flow;
            Fields = new List<FoxRunManifestField>(fields ?? Array.Empty<FoxRunManifestField>()).AsReadOnly();
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            PublishAvailable = publishAvailable;
            SubscribeAvailable = subscribeAvailable;
            PublishUnavailableDiagnosticId = publishAvailable
                ? string.Empty
                : publishUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            PublishUnavailableReason = publishAvailable
                ? string.Empty
                : publishUnavailableReason ?? unavailableReason ?? string.Empty;
            SubscribeUnavailableDiagnosticId = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableDiagnosticId ?? unavailableDiagnosticId ?? string.Empty;
            SubscribeUnavailableReason = subscribeAvailable
                ? string.Empty
                : subscribeUnavailableReason ?? unavailableReason ?? string.Empty;
        }

        private static string SharedUnavailableValue(
            bool publishAvailable,
            string publishValue,
            bool subscribeAvailable,
            string subscribeValue)
        {
            if (publishAvailable)
                return subscribeAvailable ? string.Empty : subscribeValue;
            if (subscribeAvailable)
                return publishValue;
            if (string.IsNullOrEmpty(publishValue))
                return subscribeValue;
            if (string.IsNullOrEmpty(subscribeValue))
                return publishValue;
            return string.Equals(publishValue, subscribeValue, StringComparison.Ordinal)
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
            ProtobufMetadata = protobufMetadata
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
