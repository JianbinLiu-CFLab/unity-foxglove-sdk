// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Immutable generated native subscription metadata and bounded-copy context.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using Unity.FoxgloveSDK.Components;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    /// <summary>
    /// Declares whether a generated native contract targets one of the
    /// precompiled ROS2 message assemblies or a locked Phase181 custom
    /// interface package.  This is intentionally an explicit semantic axis:
    /// callers must never infer it from a type-name convention.
    /// </summary>
    public enum FoxRunRos2GeneratedContractKind
    {
        PackagedMessage = 0,
        CustomInterface = 1
    }

    /// <summary>Immutable metadata for one generated native ROS2 subscription.</summary>
    public sealed class FoxRunRos2GeneratedContract
    {
        private readonly Func<object, string> _customEnvelopeOriginAccessor;
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            FoxRunFlow mode,
            FoxRunRos2RouteEndpoint source,
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
            bool supportsRos2Native,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = 0f,
            bool hasExplicitHz = false,
            float heartbeatIntervalSeconds = 0f)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            Mode = mode;
            Policy = policy;
            Hz = hz;
            HasExplicitHz = hasExplicitHz;
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds;
            Source = source;
            SetQosDeclaration(
                qosProfile,
                hasExplicitQosProfile,
                qosReliability,
                hasExplicitQosReliability,
                qosDurability,
                hasExplicitQosDurability,
                qosHistory,
                hasExplicitQosHistory,
                qosDepth,
                hasExplicitQosDepth);
            SupportsRos2Native = supportsRos2Native;
            HasCompleteMetadata = true;
            ContractKind = FoxRunRos2GeneratedContractKind.PackagedMessage;
            DeclaredSubscriptionEncoding = (FoxRunEncoding)0;
            CanonicalPayloadType = CanonicalRosType;
            CanonicalEnvelopeType = CanonicalRosType;
            StaticInterfacePackageId = string.Empty;
            RosPackageName = string.Empty;
            InterfaceRevision = 0;
            InterfaceDigest = string.Empty;
            BaseRuntimePackageId = string.Empty;
        }

        /// <summary>
        /// Full generated-contract constructor for Phase181 custom ROS2
        /// interfaces.  Existing Phase179 constructors deliberately retain
        /// their packaged-message defaults for source and binary compatibility.
        /// </summary>
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalEnvelopeType,
            FoxRunFlow mode,
            FoxRunRos2RouteEndpoint source,
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
            bool supportsRos2Native,
            FoxRunEncoding declaredSubscriptionEncoding,
            FoxRunRos2GeneratedContractKind contractKind,
            string staticInterfacePackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string baseRuntimePackageId,
            string canonicalPayloadType,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = 0f,
            bool hasExplicitHz = false,
            float heartbeatIntervalSeconds = 0f)
            : this(
                id,
                topic,
                declaringType,
                memberName,
                canonicalEnvelopeType,
                mode,
                source,
                qosProfile,
                hasExplicitQosProfile,
                qosReliability,
                hasExplicitQosReliability,
                qosDurability,
                hasExplicitQosDurability,
                qosHistory,
                hasExplicitQosHistory,
                qosDepth,
                hasExplicitQosDepth,
                supportsRos2Native,
                declaredSubscriptionEncoding,
                contractKind,
                staticInterfacePackageId,
                rosPackageName,
                interfaceRevision,
                interfaceDigest,
                baseRuntimePackageId,
                canonicalPayloadType,
                null,
                policy,
                hz,
                hasExplicitHz,
                heartbeatIntervalSeconds)
        {
        }

        /// <summary>
        /// Complete custom-interface constructor with a generated direct
        /// envelope-origin accessor. The accessor is used only on the Unity
        /// main thread after a callback-owned envelope has been copied, never
        /// through reflection and never on the R2FU executor.
        /// </summary>
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalEnvelopeType,
            FoxRunFlow mode,
            FoxRunRos2RouteEndpoint source,
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
            bool supportsRos2Native,
            FoxRunEncoding declaredSubscriptionEncoding,
            FoxRunRos2GeneratedContractKind contractKind,
            string staticInterfacePackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string baseRuntimePackageId,
            string canonicalPayloadType,
            Func<object, string> customEnvelopeOriginAccessor,
            FoxRunPolicy policy = FoxRunPolicy.FixedRate,
            float hz = 0f,
            bool hasExplicitHz = false,
            float heartbeatIntervalSeconds = 0f)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalEnvelopeType, nameof(canonicalEnvelopeType));
            Mode = mode;
            Policy = policy;
            Hz = hz;
            HasExplicitHz = hasExplicitHz;
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds;
            Source = source;
            SetQosDeclaration(
                qosProfile,
                hasExplicitQosProfile,
                qosReliability,
                hasExplicitQosReliability,
                qosDurability,
                hasExplicitQosDurability,
                qosHistory,
                hasExplicitQosHistory,
                qosDepth,
                hasExplicitQosDepth);
            SupportsRos2Native = supportsRos2Native;
            HasCompleteMetadata = true;
            DeclaredSubscriptionEncoding = declaredSubscriptionEncoding;
            ContractKind = contractKind;
            StaticInterfacePackageId = staticInterfacePackageId ?? string.Empty;
            RosPackageName = rosPackageName ?? string.Empty;
            InterfaceRevision = interfaceRevision;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            BaseRuntimePackageId = baseRuntimePackageId ?? string.Empty;
            CanonicalPayloadType = canonicalPayloadType ?? string.Empty;
            CanonicalEnvelopeType = CanonicalRosType;
            _customEnvelopeOriginAccessor = customEnvelopeOriginAccessor;
        }

        public string Id { get; }
        public string Topic { get; }
        public string DeclaringType { get; }
        public string MemberName { get; }
        public string CanonicalRosType { get; }
        public bool HasCompleteMetadata { get; }
        public FoxRunFlow Mode { get; }
        public FoxRunPolicy Policy { get; }
        public float Hz { get; }
        public bool HasExplicitHz { get; }
        public float HeartbeatIntervalSeconds { get; }
        public FoxRunRos2RouteEndpoint Source { get; }
        public FoxRunQosProfile QosProfile { get; private set; }
        public bool HasExplicitQosProfile { get; private set; }
        public FoxRunQosReliability QosReliability { get; private set; }
        public bool HasExplicitQosReliability { get; private set; }
        public FoxRunQosDurability QosDurability { get; private set; }
        public bool HasExplicitQosDurability { get; private set; }
        public FoxRunQosHistory QosHistory { get; private set; }
        public bool HasExplicitQosHistory { get; private set; }
        public int QosDepth { get; private set; }
        public bool HasExplicitQosDepth { get; private set; }
        public bool SupportsRos2Native { get; }
        /// <summary>Actual generated subscription encoding; it is independent from provider.</summary>
        public FoxRunEncoding DeclaredSubscriptionEncoding { get; }
        /// <summary>Explicit packaged-message versus generated-interface category.</summary>
        public FoxRunRos2GeneratedContractKind ContractKind { get; }
        /// <summary>Canonical DTO payload type identity for custom interfaces.</summary>
        public string CanonicalPayloadType { get; }
        /// <summary>Canonical ROS envelope identity; equals <see cref="CanonicalRosType"/> for compatibility.</summary>
        public string CanonicalEnvelopeType { get; }
        /// <summary>Locked static UPM package that supplies custom interfaces.</summary>
        public string StaticInterfacePackageId { get; }
        /// <summary>Locked ROS package identity for custom interfaces.</summary>
        public string RosPackageName { get; }
        /// <summary>Monotonic static-interface revision.</summary>
        public int InterfaceRevision { get; }
        /// <summary>SHA-256 of the locked static interface package.</summary>
        public string InterfaceDigest { get; }
        /// <summary>Exact runtime package identity expected by the selected custom typesupport add-on.</summary>
        public string BaseRuntimePackageId { get; }

        /// <summary>
        /// Whether all metadata required to activate a generated custom
        /// interface endpoint is present.  The activation layer still validates
        /// enum values and runtime readiness so malformed generated metadata
        /// fails closed instead of throwing on an executor path.
        /// </summary>
        public bool HasCompleteCustomMetadata
            => ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface
               && !string.IsNullOrWhiteSpace(CanonicalPayloadType)
               && !string.IsNullOrWhiteSpace(CanonicalEnvelopeType)
               && !string.IsNullOrWhiteSpace(StaticInterfacePackageId)
               && !string.IsNullOrWhiteSpace(RosPackageName)
               && !string.IsNullOrWhiteSpace(BaseRuntimePackageId)
               && InterfaceRevision > 0
               && IsSha256(InterfaceDigest);

        /// <summary>
        /// Reads the generated custom envelope origin without reflection. A
        /// malformed generated accessor fails closed: callers treat the value
        /// as unavailable and never let it throw through an apply path.
        /// </summary>
        internal bool TryGetCustomEnvelopeOrigin(object envelope, out string origin)
        {
            origin = string.Empty;
            if (ContractKind != FoxRunRos2GeneratedContractKind.CustomInterface
                || envelope == null
                || _customEnvelopeOriginAccessor == null)
                return false;
            try
            {
                origin = _customEnvelopeOriginAccessor(envelope) ?? string.Empty;
                return true;
            }
            catch (Exception)
            {
                origin = string.Empty;
                return false;
            }
        }

        private static string Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Generated ROS2 contract value must not be empty.", name);
            return value;
        }

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

        private void SetQosDeclaration(
            FoxRunQosProfile profile,
            bool hasProfile,
            FoxRunQosReliability reliability,
            bool hasReliability,
            FoxRunQosDurability durability,
            bool hasDurability,
            FoxRunQosHistory history,
            bool hasHistory,
            int depth,
            bool hasDepth)
        {
            QosProfile = profile;
            HasExplicitQosProfile = hasProfile;
            QosReliability = reliability;
            HasExplicitQosReliability = hasReliability;
            QosDurability = durability;
            HasExplicitQosDurability = hasDurability;
            QosHistory = history;
            HasExplicitQosHistory = hasHistory;
            QosDepth = depth;
            HasExplicitQosDepth = hasDepth;
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
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

    /// <summary>
    /// Per-callback managed-copy budget. Counts copied string UTF-16 storage and
    /// sequence element storage; it is intentionally not a DDS/CDR byte size.
    /// </summary>
    public sealed class FoxRunRos2CopyContext
    {
        [ThreadStatic]
        private static FoxRunRos2CopyContext s_cached;

        private long _remainingBytes;
        private bool _rented;

        public FoxRunRos2CopyContext(long maximumBytes)
        {
            Reset(maximumBytes);
        }

        public long RemainingBytes => _remainingBytes;

        public void RequireBytes(long byteCount)
        {
            if (byteCount < 0)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if (byteCount > _remainingBytes)
                throw new InvalidOperationException("FoxRun ROS2 managed-copy budget exceeded.");
            _remainingBytes -= byteCount;
        }

        internal static FoxRunRos2CopyContext Rent(long maximumBytes)
        {
            var context = s_cached;
            if (context == null)
                context = new FoxRunRos2CopyContext(maximumBytes);
            else
            {
                s_cached = null;
                context.Reset(maximumBytes);
            }
            context._rented = true;
            return context;
        }

        internal void Return()
        {
            if (!_rented)
                return;
            _rented = false;
            if (s_cached == null)
                s_cached = this;
        }

        private void Reset(long maximumBytes)
        {
            if (maximumBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            _remainingBytes = maximumBytes;
        }
    }
}
#endif
