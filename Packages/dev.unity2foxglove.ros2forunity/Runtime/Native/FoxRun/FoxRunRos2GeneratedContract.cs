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
        /// <summary>
        /// Compatibility constructor for Phase179-B generated callers. Legacy
        /// string metadata is retained for source compatibility but is not
        /// sufficient to activate a native subscription.
        /// </summary>
        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            string declaredProvider,
            string ros2Qos)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            DeclaredProvider = Require(declaredProvider, nameof(declaredProvider));
            Ros2Qos = Require(ros2Qos, nameof(ros2Qos));
            Mode = FoxRunMode.PublishOnly;
            SubscriptionProvider = FoxRunSubscriptionProvider.Inherit;
            QosPreset = FoxRunRos2QosPreset.Inherit;
            SupportsRos2Native = false;
            HasCompleteMetadata = false;
            ContractKind = FoxRunRos2GeneratedContractKind.PackagedMessage;
            DeclaredSubscriptionEncoding = FoxRunWireEncoding.Inherit;
            CanonicalPayloadType = CanonicalRosType;
            CanonicalEnvelopeType = CanonicalRosType;
            StaticInterfacePackageId = string.Empty;
            RosPackageName = string.Empty;
            InterfaceRevision = 0;
            InterfaceDigest = string.Empty;
            BaseRuntimePackageId = string.Empty;
        }

        public FoxRunRos2GeneratedContract(
            string id,
            string topic,
            string declaringType,
            string memberName,
            string canonicalRosType,
            FoxRunMode mode,
            FoxRunSubscriptionProvider subscriptionProvider,
            FoxRunRos2QosPreset qosPreset,
            bool supportsRos2Native)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalRosType, nameof(canonicalRosType));
            Mode = mode;
            SubscriptionProvider = subscriptionProvider;
            QosPreset = qosPreset;
            SupportsRos2Native = supportsRos2Native;
            HasCompleteMetadata = true;
            DeclaredProvider = ProviderText(subscriptionProvider);
            Ros2Qos = QosText(qosPreset);
            ContractKind = FoxRunRos2GeneratedContractKind.PackagedMessage;
            DeclaredSubscriptionEncoding = FoxRunWireEncoding.Inherit;
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
            FoxRunMode mode,
            FoxRunSubscriptionProvider subscriptionProvider,
            FoxRunRos2QosPreset qosPreset,
            bool supportsRos2Native,
            FoxRunWireEncoding declaredSubscriptionEncoding,
            FoxRunRos2GeneratedContractKind contractKind,
            string staticInterfacePackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string baseRuntimePackageId,
            string canonicalPayloadType)
            : this(
                id,
                topic,
                declaringType,
                memberName,
                canonicalEnvelopeType,
                mode,
                subscriptionProvider,
                qosPreset,
                supportsRos2Native,
                declaredSubscriptionEncoding,
                contractKind,
                staticInterfacePackageId,
                rosPackageName,
                interfaceRevision,
                interfaceDigest,
                baseRuntimePackageId,
                canonicalPayloadType,
                null)
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
            FoxRunMode mode,
            FoxRunSubscriptionProvider subscriptionProvider,
            FoxRunRos2QosPreset qosPreset,
            bool supportsRos2Native,
            FoxRunWireEncoding declaredSubscriptionEncoding,
            FoxRunRos2GeneratedContractKind contractKind,
            string staticInterfacePackageId,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            string baseRuntimePackageId,
            string canonicalPayloadType,
            Func<object, string> customEnvelopeOriginAccessor)
        {
            Id = Require(id, nameof(id));
            Topic = Require(topic, nameof(topic));
            DeclaringType = Require(declaringType, nameof(declaringType));
            MemberName = Require(memberName, nameof(memberName));
            CanonicalRosType = Require(canonicalEnvelopeType, nameof(canonicalEnvelopeType));
            Mode = mode;
            SubscriptionProvider = subscriptionProvider;
            QosPreset = qosPreset;
            SupportsRos2Native = supportsRos2Native;
            HasCompleteMetadata = true;
            DeclaredProvider = ProviderText(subscriptionProvider);
            Ros2Qos = QosText(qosPreset);
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
        public string DeclaredProvider { get; }
        public string Ros2Qos { get; }
        public bool HasCompleteMetadata { get; }
        public FoxRunMode Mode { get; }
        public FoxRunSubscriptionProvider SubscriptionProvider { get; }
        public FoxRunRos2QosPreset QosPreset { get; }
        public bool SupportsRos2Native { get; }
        /// <summary>Actual generated subscription encoding; it is independent from provider.</summary>
        public FoxRunWireEncoding DeclaredSubscriptionEncoding { get; }
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

        private static string ProviderText(FoxRunSubscriptionProvider provider)
        {
            switch (provider)
            {
                case FoxRunSubscriptionProvider.Inherit: return "inherit";
                case FoxRunSubscriptionProvider.FoxgloveWebSocket: return "foxglove-websocket";
                case FoxRunSubscriptionProvider.Ros2Native: return "ros2-native";
                default: return ((int)provider).ToString();
            }
        }

        private static string QosText(FoxRunRos2QosPreset qos)
        {
            switch (qos)
            {
                case FoxRunRos2QosPreset.Inherit: return "inherit";
                case FoxRunRos2QosPreset.Default: return "default";
                case FoxRunRos2QosPreset.Reliable: return "reliable";
                case FoxRunRos2QosPreset.SensorData: return "sensor-data";
                case FoxRunRos2QosPreset.TransientLocal: return "transient-local";
                default: return ((int)qos).ToString();
            }
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
