// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated FoxRun manifest metadata.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Generated runtime snapshot of the canonical FoxRun manifest.</summary>
    public sealed class FoxRunSchemaManifestInfo
    {
        public int ManifestVersion { get; }
        public string PackageName { get; }
        public string GeneratorName { get; }
        public int GeneratorMajorVersion { get; }
        public string GlobalManifestHash { get; }
        public string FoxRunManifestHash { get; }
        public IReadOnlyList<FoxRunSchemaTypeInfo> Types { get; }
        public string SubscriptionManifestHash { get; }
        public IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> SubscriptionBindings { get; }
        /// <summary>
        /// Generated direction-neutral metadata for custom ROS2 DTO contracts.
        /// Unlike <see cref="SubscriptionBindings"/>, this list includes eligible
        /// native Publish contracts as well as inbound and P&amp;S contracts.
        /// It is ROS-free evidence used by Editor presentation and demand policy;
        /// endpoint readiness remains owned by the optional R2FU catalog.
        /// </summary>
        public IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> CustomNativeContracts { get; }
        public int TypeCount { get; }
        public int ContractCount { get; }
        public int FieldCount { get; }

        public FoxRunSchemaManifestInfo(
            int manifestVersion,
            string packageName,
            string generatorName,
            int generatorMajorVersion,
            string globalManifestHash,
            string foxRunManifestHash,
            IReadOnlyList<FoxRunSchemaTypeInfo> types,
            string subscriptionManifestHash = "",
            IReadOnlyList<FoxRunSchemaSubscriptionBindingInfo> subscriptionBindings = null,
            IReadOnlyList<FoxRunSchemaCustomNativeContractInfo> customNativeContracts = null)
        {
            ManifestVersion = manifestVersion;
            PackageName = packageName ?? string.Empty;
            GeneratorName = generatorName ?? string.Empty;
            GeneratorMajorVersion = generatorMajorVersion;
            GlobalManifestHash = globalManifestHash ?? string.Empty;
            FoxRunManifestHash = foxRunManifestHash ?? string.Empty;
            Types = new List<FoxRunSchemaTypeInfo>(types ?? Array.Empty<FoxRunSchemaTypeInfo>()).AsReadOnly();
            SubscriptionManifestHash = subscriptionManifestHash ?? string.Empty;
            SubscriptionBindings = new List<FoxRunSchemaSubscriptionBindingInfo>(
                subscriptionBindings ?? Array.Empty<FoxRunSchemaSubscriptionBindingInfo>()).AsReadOnly();
            CustomNativeContracts = new List<FoxRunSchemaCustomNativeContractInfo>(
                customNativeContracts ?? Array.Empty<FoxRunSchemaCustomNativeContractInfo>()).AsReadOnly();
            TypeCount = Types.Count;

            var contractCount = 0;
            var fieldCount = 0;
            foreach (var type in Types)
            {
                if (type == null)
                    continue;

                contractCount += type.Contracts.Count;
                foreach (var contract in type.Contracts)
                {
                    if (contract != null)
                        fieldCount += contract.Fields.Count;
                }
            }

            ContractCount = contractCount;
            FieldCount = fieldCount;
        }
    }

    /// <summary>Generated provider/capability metadata kept separate from WebSocket encodings.</summary>
    public sealed class FoxRunSchemaSubscriptionBindingInfo
    {
        public FoxRunSchemaSubscriptionBindingInfo(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            FoxRunEndpoint declaredSource,
            FoxRunQosProfile qosProfile,
            bool supportsWebSocket,
            bool supportsRos2Native,
            string nativeType,
            string canonicalRosType,
            string copyShapeIdentity,
            string ros2ContractKind = "Unsupported",
            string customDtoIdentity = "",
            string customPayloadIdentity = "",
            string customEnvelopeIdentity = "",
            FoxRunEndpoint declaredTargets = 0,
            FoxRunQosReliability qosReliability = 0,
            FoxRunQosDurability qosDurability = 0,
            FoxRunQosHistory qosHistory = 0,
            int qosDepth = 0,
            bool isStream = false)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            DeclaredSource = declaredSource;
            DeclaredTargets = declaredTargets;
            QosProfile = qosProfile;
            QosReliability = qosReliability;
            QosDurability = qosDurability;
            QosHistory = qosHistory;
            QosDepth = qosDepth;
            SupportsWebSocket = supportsWebSocket;
            SupportsRos2Native = supportsRos2Native;
            IsStream = isStream;
            NativeType = nativeType ?? string.Empty;
            CanonicalRosType = canonicalRosType ?? string.Empty;
            CopyShapeIdentity = copyShapeIdentity ?? string.Empty;
            Ros2ContractKind = ros2ContractKind ?? string.Empty;
            CustomDtoIdentity = customDtoIdentity ?? string.Empty;
            CustomPayloadIdentity = customPayloadIdentity ?? string.Empty;
            CustomEnvelopeIdentity = customEnvelopeIdentity ?? string.Empty;
        }

        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public FoxRunEndpoint DeclaredSource { get; }
        public FoxRunEndpoint DeclaredTargets { get; }
        public FoxRunQosProfile QosProfile { get; }
        public FoxRunQosReliability QosReliability { get; }
        public FoxRunQosDurability QosDurability { get; }
        public FoxRunQosHistory QosHistory { get; }
        public int QosDepth { get; }
        public bool SupportsWebSocket { get; }
        public bool SupportsRos2Native { get; }
        public bool IsStream { get; }
        public string NativeType { get; }
        public string CanonicalRosType { get; }
        public string CopyShapeIdentity { get; }
        /// <summary>
        /// Descriptor-side contract-kind name. This remains a string because
        /// the authoritative DTO-shape enum lives in the Editor generation
        /// model and the runtime SDK must remain ROS-free.
        /// </summary>
        public string Ros2ContractKind { get; }
        public string CustomDtoIdentity { get; }
        public string CustomPayloadIdentity { get; }
        public string CustomEnvelopeIdentity { get; }
    }

    /// <summary>
    /// Direction-neutral generated evidence for a supported custom ROS2 DTO.
    /// It deliberately remains separate from subscription bindings: a custom
    /// publisher exists independently of Subscribe Data being enabled.
    /// </summary>
    public sealed class FoxRunSchemaCustomNativeContractInfo
    {
        public FoxRunSchemaCustomNativeContractInfo(
            string declaringType,
            string memberName,
            string topic,
            string flow,
            FoxRunEndpoint declaredSource,
            FoxRunQosProfile qosProfile,
            bool supportsRos2Native,
            string customDtoIdentity,
            string customPayloadIdentity,
            string customEnvelopeIdentity,
            FoxRunEndpoint declaredTargets = 0,
            FoxRunQosReliability qosReliability = 0,
            FoxRunQosDurability qosDurability = 0,
            FoxRunQosHistory qosHistory = 0,
            int qosDepth = 0)
        {
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Topic = topic ?? string.Empty;
            Flow = flow ?? string.Empty;
            DeclaredSource = declaredSource;
            DeclaredTargets = declaredTargets;
            QosProfile = qosProfile;
            QosReliability = qosReliability;
            QosDurability = qosDurability;
            QosHistory = qosHistory;
            QosDepth = qosDepth;
            SupportsRos2Native = supportsRos2Native;
            CustomDtoIdentity = customDtoIdentity ?? string.Empty;
            CustomPayloadIdentity = customPayloadIdentity ?? string.Empty;
            CustomEnvelopeIdentity = customEnvelopeIdentity ?? string.Empty;
        }

        public string DeclaringType { get; }
        public string MemberName { get; }
        public string Topic { get; }
        public string Flow { get; }
        public FoxRunEndpoint DeclaredSource { get; }
        public FoxRunEndpoint DeclaredTargets { get; }
        public FoxRunQosProfile QosProfile { get; }
        public FoxRunQosReliability QosReliability { get; }
        public FoxRunQosDurability QosDurability { get; }
        public FoxRunQosHistory QosHistory { get; }
        public int QosDepth { get; }
        public bool SupportsRos2Native { get; }
        public string CustomDtoIdentity { get; }
        public string CustomPayloadIdentity { get; }
        public string CustomEnvelopeIdentity { get; }
    }
}
