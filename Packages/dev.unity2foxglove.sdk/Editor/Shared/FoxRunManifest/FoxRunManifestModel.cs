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
        public float RateHz { get; }
        public string SchemaName { get; }
        public int PublishMode { get; }
        public int FlowMode { get; }
        public int Encoding { get; }
        public int ProtobufFieldNumber { get; }
        public FoxRunProtobufTypeShape ProtobufTypeShape { get; }
        public float ChangeEpsilon { get; }
        public float ForceIntervalSeconds { get; }
        public bool IsAggregateMember { get; }
        public string JsonFieldName { get; }
        public string SubscriptionProvider { get; }
        public string Ros2Qos { get; }
        public bool GeneratesWebSocketCodec { get; }
        public bool GeneratesRos2NativeRegistration { get; }
        public FoxRunRos2MessageShape Ros2MessageShape { get; }
        public FoxRunRos2ContractKind Ros2ContractKind { get; }
        public FoxRunRos2CustomDtoShape Ros2CustomDtoShape { get; }

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
            float rateHz,
            string schemaName,
            int publishMode,
            float changeEpsilon,
            float forceIntervalSeconds,
            bool isAggregateMember = false,
            string jsonFieldName = "",
            int flowMode = 0,
            int encoding = 2,
            int protobufFieldNumber = 0,
            FoxRunProtobufTypeShape protobufTypeShape = null,
            string subscriptionProvider = FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider,
            string ros2Qos = FoxRunGenerationDescriptorConstants.InheritRos2Qos,
            bool generatesWebSocketCodec = true,
            bool generatesRos2NativeRegistration = false,
            FoxRunRos2MessageShape ros2MessageShape = null,
            FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported)
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
            RateHz = rateHz;
            SchemaName = schemaName ?? string.Empty;
            PublishMode = publishMode;
            FlowMode = flowMode;
            Encoding = encoding;
            ProtobufFieldNumber = protobufFieldNumber;
            ProtobufTypeShape = protobufTypeShape;
            ChangeEpsilon = changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
            IsAggregateMember = isAggregateMember;
            JsonFieldName = jsonFieldName ?? string.Empty;
            SubscriptionProvider = subscriptionProvider ?? FoxRunGenerationDescriptorConstants.InheritSubscriptionProvider;
            Ros2Qos = ros2Qos ?? FoxRunGenerationDescriptorConstants.InheritRos2Qos;
            GeneratesWebSocketCodec = generatesWebSocketCodec;
            GeneratesRos2NativeRegistration = generatesRos2NativeRegistration;
            Ros2MessageShape = ros2MessageShape;
            Ros2CustomDtoShape = ros2CustomDtoShape;
            Ros2ContractKind = ResolveRos2ContractKind(
                ros2ContractKind,
                ros2MessageShape,
                ros2CustomDtoShape);
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
                member.RateHz,
                member.SchemaName,
                member.PublishMode,
                member.ChangeEpsilon,
                member.ForceIntervalSeconds,
                member.IsAggregateMember,
                member.JsonFieldName,
                member.Mode,
                EncodingValue(member.Encoding),
                member.ProtobufFieldNumber,
                member.ProtobufTypeShape,
                member.SubscriptionProvider,
                member.Ros2Qos,
                member.GeneratesWebSocketCodec,
                member.GeneratesRos2NativeRegistration,
                member.Ros2MessageShape,
                member.Ros2CustomDtoShape,
                member.Ros2ContractKind);
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

        public FoxRunCanonicalManifest(
            int manifestVersion,
            string packageName,
            FoxRunManifestGenerator generator,
            FoxRunManifestSections sections,
            string globalManifestHash)
        {
            ManifestVersion = manifestVersion;
            Package = packageName ?? string.Empty;
            Generator = generator ?? throw new ArgumentNullException(nameof(generator));
            Sections = sections ?? throw new ArgumentNullException(nameof(sections));
            GlobalManifestHash = globalManifestHash ?? string.Empty;
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
        public string FlowMode { get; }
        public string DeclaredProvider { get; }
        public string Ros2Qos { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
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
            string flowMode,
            string declaredProvider,
            string ros2Qos,
            bool supportsWebSocket,
            bool supportsRos2Native,
            string nativeType,
            string canonicalRosType,
            string copyShapeIdentity,
            FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
            string customDtoIdentity = "",
            string customPayloadIdentity = "",
            string customEnvelopeIdentity = "")
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            FlowMode = flowMode ?? string.Empty;
            DeclaredProvider = declaredProvider ?? string.Empty;
            Ros2Qos = ros2Qos ?? string.Empty;
            SupportsWebSocket = supportsWebSocket;
            SupportsRos2Native = supportsRos2Native;
            NativeType = nativeType ?? string.Empty;
            CanonicalRosType = canonicalRosType ?? string.Empty;
            CopyShapeIdentity = copyShapeIdentity ?? string.Empty;
            Ros2ContractKind = ros2ContractKind;
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
        public string Encoding { get; }
        public string ContractHash { get; }
        public string BindingHash { get; }
        public string PolicyHash { get; }
        public string FlowMode { get; }
        public IReadOnlyList<FoxRunManifestField> Fields { get; }
        public FoxRunManifestPolicy Policy { get; }

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
            string flowMode = "PublishOnly")
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            SchemaName = schemaName ?? string.Empty;
            Encoding = encoding ?? string.Empty;
            ContractHash = contractHash ?? string.Empty;
            BindingHash = bindingHash ?? string.Empty;
            PolicyHash = policyHash ?? string.Empty;
            FlowMode = string.IsNullOrWhiteSpace(flowMode) ? "PublishOnly" : flowMode;
            Fields = new List<FoxRunManifestField>(fields ?? Array.Empty<FoxRunManifestField>()).AsReadOnly();
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
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
        public int ProtobufFieldNumber { get; }
        public FoxRunProtobufTypeShape ProtobufTypeShape { get; }

        public FoxRunManifestField(
            string jsonName,
            string memberName,
            string memberKind,
            string type,
            bool nullable,
            bool array,
            bool aggregate = false,
            int protobufFieldNumber = 0,
            FoxRunProtobufTypeShape protobufTypeShape = null)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            Type = type ?? string.Empty;
            Nullable = nullable;
            Array = array;
            Aggregate = aggregate;
            ProtobufFieldNumber = protobufFieldNumber;
            ProtobufTypeShape = protobufTypeShape;
        }
    }

    public sealed class FoxRunManifestPolicy
    {
        public string Mode { get; }
        public float RateHz { get; }
        public float ChangeEpsilon { get; }
        public float ForceIntervalSeconds { get; }

        public FoxRunManifestPolicy(
            string mode,
            float rateHz,
            float changeEpsilon,
            float forceIntervalSeconds)
        {
            Mode = mode ?? string.Empty;
            RateHz = rateHz;
            ChangeEpsilon = changeEpsilon;
            ForceIntervalSeconds = forceIntervalSeconds;
        }
    }
}
