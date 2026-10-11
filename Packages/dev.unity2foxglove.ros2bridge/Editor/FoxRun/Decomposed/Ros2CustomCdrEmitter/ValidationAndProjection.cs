// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun/Decomposed/Ros2CustomCdrEmitter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    internal static partial class FoxRunBridgeSourceEmitter
    {
        private static bool IsSupportedCustomPublish(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 2
               && !IsOfficialFoxgloveMessage(member)
               && (member.PublishTransportIds == null
                   || member.PublishTransportIds.Contains(
                        BridgeProviderId,
                        StringComparer.Ordinal))
               && ProjectShape(member.TypeShape) != null;

        internal static bool TryValidateExplicitBridgeMember(
            FoxRunGenerationMember member,
            out string diagnosticId,
            out string reason)
        {
            diagnosticId = string.Empty;
            reason = string.Empty;
            if (member == null)
                return true;

            var explicitPublish =
                member.Mode != 2
                && member.PublishTransportIds != null
                && member.PublishTransportIds.Contains(
                    BridgeProviderId,
                    StringComparer.Ordinal);
            var explicitSubscribe =
                member.Mode != 1
                && string.Equals(
                    member.SubscribeTransportId,
                    BridgeProviderId,
                    StringComparison.Ordinal);
            if ((!explicitPublish && !explicitSubscribe)
                || IsOfficialFoxgloveMessage(member))
            {
                return true;
            }

            if (!TryValidateRosFieldNames(
                    member.TypeShape,
                    member.TypeShape?.TypeName
                    ?? member.EmissionTypeName,
                    new HashSet<FoxRunTypeShape>(),
                    out reason))
            {
                diagnosticId = "FOXBRG002";
                return false;
            }

            if (ProjectShape(member.TypeShape) != null)
                return true;

            diagnosticId = "FOXBRG001";
            reason = "The explicitly selected Bridge route for '"
                     + member.DeclaringType
                     + "."
                     + member.MemberName
                     + "' requires a supported constructible custom DTO with assignable members.";
            return false;
        }

        private static bool IsSupportedStandardPublish(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 2
               && IsOfficialFoxgloveMessage(member)
               && (member.PublishTransportIds == null
                   || member.PublishTransportIds.Contains(
                       BridgeProviderId,
                       StringComparer.Ordinal));

        private static bool IsSupportedPublish(
            FoxRunGenerationMember member)
            => IsSupportedCustomPublish(member)
               || IsSupportedStandardPublish(member);

        private static bool IsSupportedCustomSubscribe(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 1
               && !IsOfficialFoxgloveMessage(member)
               && (string.IsNullOrEmpty(member.SubscribeTransportId)
                   || string.Equals(
                       member.SubscribeTransportId,
                       BridgeProviderId,
                       StringComparison.Ordinal))
               && ProjectShape(member.TypeShape) != null;

        private static bool IsSupportedStandardSubscribe(
            FoxRunGenerationMember member)
            => member != null
               && member.Mode != 1
               && (string.IsNullOrEmpty(member.SubscribeTransportId)
                   || string.Equals(
                       member.SubscribeTransportId,
                       BridgeProviderId,
                       StringComparison.Ordinal))
               && IsOfficialFoxgloveMessage(member);

        private static bool IsOfficialFoxgloveMessage(
            FoxRunGenerationMember member)
        {
            var shape = member?.TypeShape;
            if (shape == null || shape.Kind != FoxRunTypeShapeKind.Object)
                return false;

            var typeName = (shape.TypeName ?? string.Empty).Trim();
            if (typeName.StartsWith("global::", StringComparison.Ordinal))
                typeName = typeName.Substring("global::".Length);
            return typeName.StartsWith("Foxglove.", StringComparison.Ordinal);
        }

        private static string Sha256Hex(string value)
        {
            byte[] digest;
            using (var sha =
                   global::System.Security.Cryptography.SHA256.Create())
            {
                digest = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            var builder = new StringBuilder(digest.Length * 2);
            foreach (var octet in digest)
            {
                builder.Append(
                    octet.ToString(
                        "x2",
                        CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string BuildSchemaContent(
            BridgeDtoShape root,
            string packageName)
        {
            var registry = new ShapeRegistry(-1);
            registry.Get(root);
            var builder = new StringBuilder();
            builder.Append("string foxrun_origin_id\n");
            builder.Append("uint64 foxrun_sequence\n");
            builder.Append("builtin_interfaces/Time foxrun_stamp\n");
            builder.Append(root.PayloadIdentity).Append(" payload\n");
            for (var index = 0; index < registry.Count; index++)
            {
                var shape = registry[index].Shape;
                builder.Append("================================================================================\n");
                builder.Append("MSG: ").Append(packageName).Append('/').Append(shape.PayloadIdentity).Append('\n');
                foreach (var member in shape.Members
                             .OrderBy(value => value.RosFieldName, StringComparer.Ordinal)
                             .ThenBy(value => value.Name, StringComparer.Ordinal))
                {
                    var rosType = member.Kind == BridgeDtoMemberKind.NestedDto
                        ? member.NestedShape.PayloadIdentity
                        : member.RosType;
                    builder.Append(rosType).Append(' ').Append(member.RosFieldName).Append('\n');
                    if (member.HasPresence)
                    {
                        builder.Append("bool ")
                            .Append(member.PresenceFieldName)
                            .Append('\n');
                    }
                }
            }
            builder.Append("================================================================================\n");
            builder.Append("MSG: builtin_interfaces/Time\n");
            builder.Append("int32 sec\nuint32 nanosec\n");
            return builder.ToString();
        }

        private static BridgeDtoShape ProjectShape(FoxRunTypeShape shape)
        {
            if (shape == null
                || shape.Kind != FoxRunTypeShapeKind.Object
                || !shape.CanConstruct)
            {
                return null;
            }

            var members = new List<BridgeDtoMemberShape>();
            var rosNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in shape.Fields
                         .OrderBy(value => value.MemberName, StringComparer.Ordinal))
            {
                if (field == null || !field.CanAssign)
                    return null;

                var rosName = ToRosFieldName(field.MemberName);
                if (string.IsNullOrEmpty(rosName)
                    || !IsValidRosFieldName(rosName)
                    || rosName.StartsWith("foxrun_", StringComparison.Ordinal)
                    || !rosNames.Add(rosName))
                {
                    return null;
                }

                var member = ProjectMember(field, rosName);
                if (member == null)
                    return null;
                members.Add(member);
            }

            var canonicalIdentity = BuildCanonicalIdentity(
                shape.TypeName,
                members);
            return new BridgeDtoShape(
                shape.TypeName,
                canonicalIdentity,
                BuildPayloadIdentity(
                    shape.TypeName,
                    canonicalIdentity),
                members,
                shape.IsValueType);
        }

        private static BridgeDtoMemberShape ProjectMember(
            FoxRunTypeField field,
            string rosName)
        {
            var shape = field.TypeShape;
            if (shape == null)
                return null;

            if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                var element = shape.ElementShape;
                if (element == null
                    || element.Kind == FoxRunTypeShapeKind.Collection
                    || element.Nullable)
                {
                    return null;
                }

                var nested = element.Kind == FoxRunTypeShapeKind.Object
                    ? ProjectShape(element)
                    : null;
                var elementRosType = RosType(element, nested);
                var elementTypeName = ClrTypeName(element);
                if (string.IsNullOrEmpty(elementRosType)
                    || string.IsNullOrEmpty(elementTypeName))
                {
                    return null;
                }

                var representation =
                    shape.CollectionKind == FoxRunCollectionKind.List
                        ? BridgeSequenceRepresentation.List
                        : BridgeSequenceRepresentation.Array;
                if (shape.CollectionKind != FoxRunCollectionKind.Array
                    && shape.CollectionKind != FoxRunCollectionKind.List
                    && shape.CollectionKind != FoxRunCollectionKind.Binary)
                {
                    return null;
                }

                var collectionTypeName =
                    representation == BridgeSequenceRepresentation.List
                        ? "System.Collections.Generic.List<"
                          + elementTypeName
                          + ">"
                        : elementTypeName + "[]";
                return new BridgeDtoMemberShape(
                    field.MemberName,
                    rosName,
                    BridgeDtoMemberKind.Sequence,
                    collectionTypeName,
                    elementRosType + "[]",
                    elementTypeName,
                    nested,
                    hasPresence: true,
                    sequenceElementIsEnum:
                        element.Kind == FoxRunTypeShapeKind.Enum,
                    representation);
            }

            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var nested = ProjectShape(shape);
                return nested == null
                    ? null
                    : new BridgeDtoMemberShape(
                        field.MemberName,
                        rosName,
                        BridgeDtoMemberKind.NestedDto,
                        shape.TypeName,
                        nested.PayloadIdentity,
                        string.Empty,
                        nested,
                        hasPresence: true,
                        sequenceElementIsEnum: false,
                        BridgeSequenceRepresentation.None);
            }

            if (shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                var enumRosType = string.IsNullOrEmpty(shape.CanonicalType)
                    ? "int32"
                    : shape.CanonicalType;
                return new BridgeDtoMemberShape(
                    field.MemberName,
                    rosName,
                    BridgeDtoMemberKind.Enum,
                    NullableTypeName(shape.TypeName, shape.Nullable),
                    enumRosType,
                    string.Empty,
                    null,
                    shape.Nullable || field.IsNullable,
                    sequenceElementIsEnum: false,
                    BridgeSequenceRepresentation.None);
            }

            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return null;

            var rosType = RosType(shape, null);
            var clrType = ClrTypeName(shape.WithNullable(false));
            if (string.IsNullOrEmpty(rosType)
                || string.IsNullOrEmpty(clrType))
            {
                return null;
            }

            var isString = string.Equals(
                shape.CanonicalType,
                "string",
                StringComparison.Ordinal);
            return new BridgeDtoMemberShape(
                field.MemberName,
                rosName,
                isString
                    ? BridgeDtoMemberKind.String
                    : BridgeDtoMemberKind.Scalar,
                NullableTypeName(
                    clrType,
                    !isString && (shape.Nullable || field.IsNullable)),
                rosType,
                string.Empty,
                null,
                isString || shape.Nullable || field.IsNullable,
                sequenceElementIsEnum: false,
                BridgeSequenceRepresentation.None);
        }
    }
}
