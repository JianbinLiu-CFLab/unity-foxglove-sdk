// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Phase192 partial source section for MessagePackInputDispatchEmitter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class MessagePackInputDispatchEmitter
    {
        private static List<ShapeEntry> CollectShapes(
            IReadOnlyList<TransactionTopic> topics)
        {
            var result = new List<ShapeEntry>();
            foreach (var topic in topics)
            foreach (var member in topic.Members)
                CollectShape(
                    member.TypeShape
                    ?? FoxRunTypeShape.Canonical(member.CanonicalType),
                    result);
            return result;
        }

        private static void CollectShape(
            FoxRunTypeShape shape,
            ICollection<ShapeEntry> result)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            if (result.Any(entry =>
                    string.Equals(entry.Identity, identity, StringComparison.Ordinal)))
            {
                return;
            }
            result.Add(new ShapeEntry(shape, identity));
            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                foreach (var field in shape.Fields)
                    CollectShape(field.TypeShape, result);
            }
            else if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                CollectShape(shape.ElementShape, result);
            }
        }

        private static int FindShape(
            FoxRunTypeShape shape,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            for (var index = 0; index < shapes.Count; index++)
            {
                if (string.Equals(shapes[index].Identity, identity, StringComparison.Ordinal))
                    return index;
            }
            throw new InvalidOperationException(
                "MessagePack input shape was not collected.");
        }

        private static string ClrType(FoxRunTypeShape shape)
        {
            string type;
            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    type = CanonicalClrType(shape.CanonicalType);
                    break;
                case FoxRunTypeShapeKind.Enum:
                case FoxRunTypeShapeKind.Object:
                    type = GlobalTypeName(shape.TypeName);
                    break;
                case FoxRunTypeShapeKind.Collection:
                    if (shape.CollectionKind == FoxRunCollectionKind.Binary)
                        return "byte[]";
                    type = shape.CollectionKind == FoxRunCollectionKind.Array
                        ? ClrType(shape.ElementShape) + "[]"
                        : "global::System.Collections.Generic.List<"
                          + ClrType(shape.ElementShape)
                          + ">";
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported MessagePack CLR type shape.");
            }
            if (shape.Nullable && shape.IsValueType)
            {
                return "global::System.Nullable<" + type + ">";
            }
            return type;
        }

        private static string CanonicalClrType(string canonicalType)
        {
            switch (canonicalType)
            {
                case "bool": return "bool";
                case "int8": return "sbyte";
                case "uint8": return "byte";
                case "int16": return "short";
                case "uint16": return "ushort";
                case "int32": return "int";
                case "uint32": return "uint";
                case "int64": return "long";
                case "uint64": return "ulong";
                case "float32": return "float";
                case "float64": return "double";
                case "string": return "string";
                default:
                    throw new InvalidOperationException(
                        "Unsupported canonical MessagePack CLR type '"
                        + canonicalType
                        + "'.");
            }
        }

        private static bool CanBeNil(FoxRunTypeShape shape)
            => shape.Nullable
               || shape.Kind == FoxRunTypeShapeKind.Object
               || shape.Kind == FoxRunTypeShapeKind.Collection
               || shape.Kind == FoxRunTypeShapeKind.Canonical
               && string.Equals(shape.CanonicalType, "string", StringComparison.Ordinal);

        private static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            if (string.IsNullOrWhiteSpace(escaped)
                || escaped.StartsWith("global::", StringComparison.Ordinal))
            {
                return escaped;
            }
            if (escaped.EndsWith("?", StringComparison.Ordinal))
            {
                return GlobalTypeName(
                           escaped.Substring(0, escaped.Length - 1))
                       + "?";
            }
            if (escaped.EndsWith("[]", StringComparison.Ordinal))
                return GlobalTypeName(escaped.Substring(0, escaped.Length - 2)) + "[]";
            switch (escaped)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "char":
                case "object":
                case "void":
                case "dynamic":
                case "nint":
                case "nuint":
                    return escaped;
            }
            return "global::" + escaped;
        }

        private static string WireEncodingLiteral(string encoding)
            => string.Equals(
                encoding,
                FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                StringComparison.Ordinal)
                ? "FoxRunEncoding.MessagePack"
                : "(FoxRunEncoding)0";

        private static string LocalChangeExpr(
            string current,
            string type,
            string previous,
            float tolerance)
        {
            var normalized = type.StartsWith("UnityEngine.", StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
            var epsilon = TypeExprEmitter.FloatLiteral(Math.Max(0f, tolerance));
            switch (normalized)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}, {previous}, {epsilon})";
                case "double":
                case "Double":
                case "System.Double":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.DoubleChanged({current}, {previous}, {epsilon})";
                case "Vector2":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon})";
                case "Vector3":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.z, {previous}.z, {epsilon})";
                case "Quaternion":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.x, {previous}.x, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.y, {previous}.y, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.z, {previous}.z, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.w, {previous}.w, {epsilon})";
                case "Color":
                    return
                        $"global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.r, {previous}.r, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.g, {previous}.g, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.b, {previous}.b, {epsilon}) || global::Unity.FoxgloveSDK.Components.FoxRunChangeHelper.FloatChanged({current}.a, {previous}.a, {epsilon})";
                default:
                    return
                        $"!global::System.Collections.Generic.EqualityComparer<{GlobalTypeName(type)}>.Default.Equals({current}, {previous})";
            }
        }

        private static bool HasExplicit(
            FoxgloveSourceEmitter.TopicMember member,
            FoxRunNamedArgumentPresence argument)
            => (member.NamedArgumentPresence & argument) == argument;

        private static string BoolLiteral(bool value) => value ? "true" : "false";

        private static int IndexOf(
            IReadOnlyList<string> values,
            string value)
        {
            if (values == null)
                return -1;
            for (var index = 0; index < values.Count; index++)
            {
                if (string.Equals(values[index], value, StringComparison.Ordinal))
                    return index;
            }
            return -1;
        }
    }
}
