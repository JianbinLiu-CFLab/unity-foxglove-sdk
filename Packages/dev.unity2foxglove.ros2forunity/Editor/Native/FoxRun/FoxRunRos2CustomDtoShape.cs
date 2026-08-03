// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-neutral schema for a FoxRun DTO projected into a custom ROS2 message.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Identifies the native-contract family without using a type-name heuristic.
    /// PackagedRos2Message is Phase179's precompiled ros2cs path; CustomDto is
    /// the Phase181 project-owned interface path.
    /// </summary>
    public enum FoxRunRos2ContractKind
    {
        Unsupported = 0,
        PackagedRos2Message = 1,
        CustomDto = 2
    }

    public enum FoxRunRos2CustomDtoMemberKind
    {
        Scalar = 0,
        Enum = 1,
        String = 2,
        NestedDto = 3,
        Sequence = 4
    }

    public enum FoxRunRos2CustomDtoSequenceRepresentation
    {
        None = 0,
        Array = 1,
        List = 2
    }

    public sealed class FoxRunRos2CustomDtoMemberShape
    {
        public readonly string Name;
        public readonly string RosFieldName;
        public readonly string PresenceFieldName;
        public readonly FoxRunRos2CustomDtoMemberKind Kind;
        public readonly string FullyQualifiedTypeName;
        public readonly string RosType;
        public readonly string SequenceElementTypeName;
        public readonly string NestedShapeIdentity;
        public readonly FoxRunRos2CustomDtoShape NestedShape;
        public readonly bool HasPresence;
        public readonly bool CanRead;
        public readonly bool CanWrite;
        public readonly FoxRunRos2CustomDtoSequenceRepresentation SequenceRepresentation;

        public FoxRunRos2CustomDtoMemberShape(
            string name,
            string rosFieldName,
            FoxRunRos2CustomDtoMemberKind kind,
            string fullyQualifiedTypeName,
            string rosType,
            string sequenceElementTypeName,
            string nestedShapeIdentity,
            bool hasPresence,
            bool canRead,
            bool canWrite,
            FoxRunRos2CustomDtoSequenceRepresentation sequenceRepresentation = FoxRunRos2CustomDtoSequenceRepresentation.None,
            FoxRunRos2CustomDtoShape nestedShape = null)
        {
            Name = name ?? string.Empty;
            RosFieldName = rosFieldName ?? string.Empty;
            PresenceFieldName = hasPresence
                ? FoxRunRos2CustomNamingPolicy.PresenceFieldName(RosFieldName)
                : string.Empty;
            Kind = kind;
            FullyQualifiedTypeName = fullyQualifiedTypeName ?? string.Empty;
            RosType = rosType ?? string.Empty;
            SequenceElementTypeName = sequenceElementTypeName ?? string.Empty;
            NestedShapeIdentity = nestedShapeIdentity ?? string.Empty;
            NestedShape = nestedShape;
            HasPresence = hasPresence;
            CanRead = canRead;
            CanWrite = canWrite;
            SequenceRepresentation = sequenceRepresentation;
        }
    }

    /// <summary>
    /// A deterministic, ROS-free DTO schema. It intentionally does not model
    /// Phase179's packaged-message or ros2cs implementation details.
    /// </summary>
    public sealed class FoxRunRos2CustomDtoShape
    {
        public readonly string FullyQualifiedTypeName;
        public readonly string CanonicalIdentity;
        public readonly string PayloadIdentity;
        public readonly bool HasPublicParameterlessConstructor;
        public readonly bool IsSupported;
        public readonly IReadOnlyList<FoxRunRos2CustomDtoMemberShape> Members;
        public readonly IReadOnlyList<string> Diagnostics;

        public FoxRunRos2CustomDtoShape(
            string fullyQualifiedTypeName,
            string canonicalIdentity,
            string payloadIdentity,
            bool hasPublicParameterlessConstructor,
            bool isSupported,
            IReadOnlyList<FoxRunRos2CustomDtoMemberShape> members,
            IReadOnlyList<string> diagnostics)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName ?? string.Empty;
            CanonicalIdentity = canonicalIdentity ?? string.Empty;
            PayloadIdentity = payloadIdentity ?? string.Empty;
            HasPublicParameterlessConstructor = hasPublicParameterlessConstructor;
            IsSupported = isSupported;
            Members = (members ?? Array.Empty<FoxRunRos2CustomDtoMemberShape>()).ToList().AsReadOnly();
            Diagnostics = (diagnostics ?? Array.Empty<string>()).ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Diagnostic IDs for the host-neutral custom DTO boundary. Roslyn maps the
    /// encoded values to analyzer descriptors; reflection preserves the same
    /// stable evidence for manifest and fallback generation.
    /// </summary>
    public static class FoxRunRos2CustomDtoDiagnostic
    {
        public const string UnsupportedShape = "FOXR2F009";
        public const string NonConstructible = "FOXR2F010";
        public const string NonWritableInboundMember = "FOXR2F011";
        public const string PnsRequiresCustomDto = "FOXR2F008";
    }

    /// <summary>
    /// Centralizes the capability test shared by Roslyn, reflection, descriptors,
    /// and manifests. It deliberately says nothing about an optional runtime
    /// package or endpoint activation.
    /// </summary>
    public static class FoxRunRos2ContractCapability
    {
        public static bool IsNativeRegistrationCapable(
            FoxRunRos2MessageShape packagedShape,
            FoxRunRos2CustomDtoShape customShape)
        {
            if (packagedShape != null
                && packagedShape.HasPublicParameterlessConstructor
                && packagedShape.ImplementsRos2Message
                && packagedShape.Diagnostics.Count == 0)
            {
                return true;
            }

            return customShape != null
                   && customShape.IsSupported
                   && customShape.HasPublicParameterlessConstructor
                   && customShape.Diagnostics.Count == 0
                   && !string.IsNullOrWhiteSpace(customShape.CanonicalIdentity)
                   && !string.IsNullOrWhiteSpace(customShape.PayloadIdentity);
        }
    }
}
