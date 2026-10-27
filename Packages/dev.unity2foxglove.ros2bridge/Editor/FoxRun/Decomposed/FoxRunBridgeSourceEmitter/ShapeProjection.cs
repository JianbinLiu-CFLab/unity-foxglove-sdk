// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits ROS-free XCDR1 writers for Phase181 custom FoxRun DTO envelopes.

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

        private static string RosType(
            FoxRunTypeShape shape,
            BridgeDtoShape nested)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object)
                return nested?.PayloadIdentity ?? string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Enum)
                return string.IsNullOrEmpty(shape.CanonicalType)
                    ? "int32"
                    : shape.CanonicalType;
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            switch (shape.CanonicalType)
            {
                case "bool":
                case "uint8":
                case "int8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "float32":
                case "float64":
                case "string":
                    return shape.CanonicalType;
                default:
                    return string.Empty;
            }
        }

        private static string ClrTypeName(FoxRunTypeShape shape)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object
                || shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                return NullableTypeName(
                    shape.TypeName,
                    shape.Nullable
                    && shape.Kind == FoxRunTypeShapeKind.Enum);
            }
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            string typeName;
            switch (shape.CanonicalType)
            {
                case "bool": typeName = "System.Boolean"; break;
                case "uint8": typeName = "System.Byte"; break;
                case "int8": typeName = "System.SByte"; break;
                case "int16": typeName = "System.Int16"; break;
                case "uint16": typeName = "System.UInt16"; break;
                case "int32": typeName = "System.Int32"; break;
                case "uint32": typeName = "System.UInt32"; break;
                case "int64": typeName = "System.Int64"; break;
                case "uint64": typeName = "System.UInt64"; break;
                case "float32": typeName = "System.Single"; break;
                case "float64": typeName = "System.Double"; break;
                case "string": return "System.String";
                default: return string.Empty;
            }
            return NullableTypeName(typeName, shape.Nullable);
        }

        private static string NullableTypeName(
            string typeName,
            bool nullable)
            => nullable
                ? "System.Nullable<" + typeName + ">"
                : typeName ?? string.Empty;

        private static string BuildCanonicalIdentity(
            string typeName,
            IEnumerable<BridgeDtoMemberShape> members)
        {
            var builder = new StringBuilder();
            AppendLengthFramed(builder, typeName);
            foreach (var member in members)
            {
                AppendLengthFramed(builder, member.Name);
                AppendLengthFramed(builder, member.RosFieldName);
                AppendLengthFramed(builder, member.Kind.ToString());
                AppendLengthFramed(builder, member.FullyQualifiedTypeName);
                AppendLengthFramed(builder, member.RosType);
                AppendLengthFramed(builder, member.SequenceElementTypeName);
                AppendLengthFramed(builder, member.NestedShapeIdentity);
                AppendLengthFramed(
                    builder,
                    member.HasPresence ? "1" : "0");
                AppendLengthFramed(
                    builder,
                    member.SequenceRepresentation.ToString());
            }
            return builder.ToString();
        }

        private static string BuildPayloadIdentity(
            string typeName,
            string canonicalIdentity)
        {
            var simpleName = typeName ?? string.Empty;
            var dot = simpleName.LastIndexOf('.');
            if (dot >= 0)
                simpleName = simpleName.Substring(dot + 1);
            var pascal = ToPascalIdentifier(simpleName);
            if (string.IsNullOrEmpty(pascal))
                pascal = "FoxRunPayload";
            return pascal + Fnv1a64Hex(
                (typeName ?? string.Empty).ToUpperInvariant()
                + "|"
                + (canonicalIdentity ?? string.Empty));
        }

    }
}
