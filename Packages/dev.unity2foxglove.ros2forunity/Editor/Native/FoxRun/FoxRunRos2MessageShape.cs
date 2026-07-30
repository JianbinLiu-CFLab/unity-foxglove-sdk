// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-neutral native ROS2 message-copy shape and deterministic diagnostics.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public enum FoxRunRos2MessageMemberKind
    {
        Scalar = 0,
        String = 1,
        Enum = 2,
        NestedMessage = 3,
        Sequence = 4
    }

    public enum FoxRunRos2SequenceRepresentation
    {
        None = 0,
        Array = 1,
        List = 2,
        FixedArray = 3
    }

    public sealed class FoxRunRos2MessageMemberShape
    {
        public readonly string Name;
        public readonly FoxRunRos2MessageMemberKind Kind;
        public readonly string FullyQualifiedTypeName;
        public readonly string SequenceElementTypeName;
        public readonly string NestedShapeIdentity;
        /// <summary>
        /// Host-neutral recursive copy shape for nested messages or nested-message
        /// sequence elements. The identity remains the compact deterministic
        /// digest; emitters consume this graph and never reconstruct it by
        /// parsing the digest or reflecting over runtime message types.
        /// </summary>
        public readonly FoxRunRos2MessageShape NestedShape;
        public readonly bool CanRead;
        public readonly bool CanWrite;
        public readonly FoxRunRos2SequenceRepresentation SequenceRepresentation;
        // Zero means the generated metadata does not expose a static bound. For
        // getter-only FixedArray members, the copier must require the already-
        // constructed target array to have exactly the source length. Builders
        // never instantiate ROS2 message types merely to discover this value.
        public readonly int FixedSize;

        public FoxRunRos2MessageMemberShape(
            string name,
            FoxRunRos2MessageMemberKind kind,
            string fullyQualifiedTypeName,
            string sequenceElementTypeName,
            string nestedShapeIdentity,
            bool canRead = true,
            bool canWrite = true,
            FoxRunRos2SequenceRepresentation sequenceRepresentation = FoxRunRos2SequenceRepresentation.None,
            int fixedSize = 0,
            FoxRunRos2MessageShape nestedShape = null)
        {
            Name = name ?? string.Empty;
            Kind = kind;
            FullyQualifiedTypeName = fullyQualifiedTypeName ?? string.Empty;
            SequenceElementTypeName = sequenceElementTypeName ?? string.Empty;
            NestedShapeIdentity = nestedShapeIdentity ?? string.Empty;
            NestedShape = nestedShape;
            CanRead = canRead;
            CanWrite = canWrite;
            SequenceRepresentation = sequenceRepresentation;
            FixedSize = fixedSize < 0 ? 0 : fixedSize;
        }
    }

    public sealed class FoxRunRos2MessageShape
    {
        public readonly string FullyQualifiedTypeName;
        public readonly string CanonicalRosType;
        public readonly bool HasPublicParameterlessConstructor;
        public readonly bool ImplementsRos2Message;
        public readonly string CopyShapeIdentity;
        public readonly IReadOnlyList<FoxRunRos2MessageMemberShape> Members;
        public readonly IReadOnlyList<string> Diagnostics;

        public FoxRunRos2MessageShape(
            string fullyQualifiedTypeName,
            string canonicalRosType,
            bool hasPublicParameterlessConstructor,
            bool implementsRos2Message,
            string copyShapeIdentity,
            IReadOnlyList<FoxRunRos2MessageMemberShape> members,
            IReadOnlyList<string> diagnostics)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName ?? string.Empty;
            CanonicalRosType = canonicalRosType ?? string.Empty;
            HasPublicParameterlessConstructor = hasPublicParameterlessConstructor;
            ImplementsRos2Message = implementsRos2Message;
            CopyShapeIdentity = copyShapeIdentity ?? string.Empty;
            Members = (members ?? Array.Empty<FoxRunRos2MessageMemberShape>()).ToList().AsReadOnly();
            Diagnostics = (diagnostics ?? Array.Empty<string>()).ToList().AsReadOnly();
        }
    }

    public static class FoxRunRos2ShapeDiagnostic
    {
        private const char Separator = '|';

        public static string Encode(string id, string path, string message)
            => (id ?? string.Empty) + Separator + (path ?? string.Empty) + Separator + (message ?? string.Empty);

        public static bool TryDecode(string value, out string id, out string path, out string message)
        {
            value = value ?? string.Empty;
            var first = value.IndexOf(Separator);
            var second = first < 0 ? -1 : value.IndexOf(Separator, first + 1);
            if (first <= 0 || second < 0)
            {
                id = string.Empty;
                path = string.Empty;
                message = value;
                return false;
            }

            id = value.Substring(0, first);
            path = value.Substring(first + 1, second - first - 1);
            message = value.Substring(second + 1);
            return true;
        }
    }
}
