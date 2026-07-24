// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Immutable generated custom-ROS2 native publisher metadata.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Immutable metadata for one generated custom DTO native-output endpoint.
    /// This is intentionally distinct from the Phase179 packaged-message input
    /// contract: custom P&amp;S endpoints have a generated envelope and a locked
    /// static interface add-on identity.
    /// </summary>
    public sealed class FoxRunRos2CustomPublisherContract
    {
        public FoxRunRos2CustomPublisherContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalPayloadType,
            string canonicalEnvelopeType,
            string staticInterfacePackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string baseRuntimePackageId,
            FoxRunFlow mode,
            FoxRunQosProfile qosProfile,
            bool hasExplicitQosProfile,
            FoxRunQosReliability qosReliability,
            bool hasExplicitQosReliability,
            FoxRunQosDurability qosDurability,
            bool hasExplicitQosDurability,
            FoxRunQosHistory qosHistory,
            bool hasExplicitQosHistory,
            int qosDepth,
            bool hasExplicitQosDepth,
            FoxRunEndpoint declaredSource = 0,
            bool hasExplicitSource = false,
            FoxRunEndpoint declaredTargets = 0,
            bool hasExplicitTargets = false)
        {
            Id = id ?? string.Empty;
            Topic = topic ?? string.Empty;
            DeclaringType = declaringType ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            CanonicalPayloadType = canonicalPayloadType ?? string.Empty;
            CanonicalEnvelopeType = canonicalEnvelopeType ?? string.Empty;
            StaticInterfacePackageId = staticInterfacePackageId ?? string.Empty;
            RosPackageName = rosPackageName ?? string.Empty;
            InterfaceRevision = interfaceRevision;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            BaseRuntimePackageId = baseRuntimePackageId ?? string.Empty;
            Mode = mode;
            QosProfile = qosProfile;
            HasExplicitQosProfile = hasExplicitQosProfile;
            QosReliability = qosReliability;
            HasExplicitQosReliability = hasExplicitQosReliability;
            QosDurability = qosDurability;
            HasExplicitQosDurability = hasExplicitQosDurability;
            QosHistory = qosHistory;
            HasExplicitQosHistory = hasExplicitQosHistory;
            QosDepth = qosDepth;
            HasExplicitQosDepth = hasExplicitQosDepth;
            DeclaredSource = declaredSource;
            HasExplicitSource = hasExplicitSource;
            DeclaredTargets = declaredTargets;
            HasExplicitTargets = hasExplicitTargets;
        }

        public string Id { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalPayloadType { get; }
        public string CanonicalEnvelopeType { get; }
        public string StaticInterfacePackageId { get; }
        public string RosPackageName { get; }
        public int InterfaceRevision { get; }
        public string InterfaceDigest { get; }
        public string BaseRuntimePackageId { get; }
        public FoxRunFlow Mode { get; }
        public FoxRunQosProfile QosProfile { get; }
        public bool HasExplicitQosProfile { get; }
        public FoxRunQosReliability QosReliability { get; }
        public bool HasExplicitQosReliability { get; }
        public FoxRunQosDurability QosDurability { get; }
        public bool HasExplicitQosDurability { get; }
        public FoxRunQosHistory QosHistory { get; }
        public bool HasExplicitQosHistory { get; }
        public int QosDepth { get; }
        public bool HasExplicitQosDepth { get; }
        public FoxRunEndpoint DeclaredSource { get; }
        public bool HasExplicitSource { get; }
        public FoxRunEndpoint DeclaredTargets { get; }
        public bool HasExplicitTargets { get; }
        public bool HasExplicitQos
            => HasExplicitQosProfile
               || HasExplicitQosReliability
               || HasExplicitQosDurability
               || HasExplicitQosHistory
               || HasExplicitQosDepth;

        public FoxRunQosResolution ResolveQos(FoxRunResolvedQos inherited)
            => FoxRunRos2QosProfileResolver.Resolve(
                QosProfile,
                HasExplicitQosProfile,
                QosReliability,
                HasExplicitQosReliability,
                QosDurability,
                HasExplicitQosDurability,
                QosHistory,
                HasExplicitQosHistory,
                QosDepth,
                HasExplicitQosDepth,
                inherited);

        public FoxRunEndpointResolution ResolveTopology(
            FoxRunEndpoint defaultSource,
            FoxRunEndpoint defaultTargets)
            => FoxRunEndpointResolver.Resolve(
                Mode,
                DeclaredSource,
                HasExplicitSource,
                DeclaredTargets,
                HasExplicitTargets,
                declaredEncoding: 0,
                hasExplicitEncoding: false,
                defaultSource,
                defaultTargets,
                publishDefaultEncoding: FoxRunEncoding.Protobuf,
                subscribeDefaultEncoding: FoxRunEncoding.Protobuf,
                HasExplicitQos);

        /// <summary>True only for a generated custom PublishAndSubscribe contract.</summary>
        public bool IsPublishAndSubscribe => Mode == FoxRunFlow.PublishAndSubscribe;

        /// <summary>
        /// Custom native output is supported only for generated Publish and
        /// PublishAndSubscribe contracts whose add-on identity is complete.
        /// Invalid serialized enum values fail closed through this property.
        /// </summary>
        public bool SupportsNativeOutput
            => HasCompleteMetadata
               && (Mode == FoxRunFlow.Publish || Mode == FoxRunFlow.PublishAndSubscribe);

        public bool HasCompleteMetadata
            => !String.IsNullOrWhiteSpace(Id)
               && !String.IsNullOrWhiteSpace(Topic)
               && !String.IsNullOrWhiteSpace(DeclaringType)
               && !String.IsNullOrWhiteSpace(MemberName)
               && !String.IsNullOrWhiteSpace(CanonicalPayloadType)
               && !String.IsNullOrWhiteSpace(CanonicalEnvelopeType)
               && !String.IsNullOrWhiteSpace(StaticInterfacePackageId)
               && !String.IsNullOrWhiteSpace(RosPackageName)
               && !String.IsNullOrWhiteSpace(BaseRuntimePackageId)
               && InterfaceRevision > 0
               && IsSha256(InterfaceDigest);

        private static bool IsSha256(string value)
        {
            if (String.IsNullOrEmpty(value) || value.Length != 64)
                return false;

            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }
    }
}
#endif
