// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits direct, static Protobuf byte writers for FoxRun topic payloads.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class ProtobufPublishDispatchEmitter
    {
        internal static void EmitBuilders(
            StringBuilder sb,
            string declaringType,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var objectShapes = new List<FoxRunProtobufTypeShape>();
            foreach (var fields in topicMap.Values)
                foreach (var field in fields)
                    CollectObjects(field.ProtobufTypeShape, objectShapes);

            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!TopicMetadataEmitter.UsesProtobuf(fields))
                    continue;

                sb.AppendLine();
                sb.AppendLine($"{pad}    private byte[] __BuildFoxRunProtobuf_{topicIndex}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var __payload = new global::System.Collections.Generic.List<byte>(64);");
                foreach (var field in fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
                {
                    var number = FoxRunProtobufFieldNumber.Resolve(
                        FoxRunProtobufContractBuilder.BuildFieldIdentity(
                            declaringType,
                            field.Topic,
                            field.SchemaName,
                            field.MemberName),
                        field.ProtobufFieldNumber);
                    EmitWriteField(sb, field, number, TypeExprEmitter.MemberAccess(field.MemberName), "__payload", pad + "        ", objectShapes);
                }
                sb.AppendLine($"{pad}        return __payload.ToArray();");
                sb.AppendLine($"{pad}    }}");
            }

            for (var i = 0; i < objectShapes.Count; i++)
                EmitObjectWriter(sb, objectShapes[i], i, pad, objectShapes);
        }

        private static void EmitWriteField(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember field,
            int number,
            string access,
            string buffer,
            string pad,
            IReadOnlyList<FoxRunProtobufTypeShape> objectShapes)
        {
            if (IsCollection(field.TypeName))
            {
                sb.AppendLine($"{pad}if ({access} != null)");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    foreach (var __item in {access})");
                EmitOptionalValue(
                    sb,
                    field.CanonicalType,
                    field.ProtobufTypeShape,
                    number,
                    "__item",
                    buffer,
                    pad + "        ",
                    objectShapes,
                    IsCollectionElementNullable(field.TypeName));
                sb.AppendLine($"{pad}}}");
                return;
            }

            EmitOptionalValue(
                sb,
                field.CanonicalType,
                field.ProtobufTypeShape,
                number,
                access,
                buffer,
                pad,
                objectShapes,
                IsNullableValueType(field.TypeName));
        }

        private static void EmitOptionalValue(
            StringBuilder sb,
            string canonicalType,
            FoxRunProtobufTypeShape shape,
            int number,
            string access,
            string buffer,
            string pad,
            IReadOnlyList<FoxRunProtobufTypeShape> objectShapes,
            bool isNullable)
        {
            if (!isNullable)
            {
                EmitValue(sb, canonicalType, shape, number, access, buffer, pad, objectShapes);
                return;
            }

            sb.AppendLine($"{pad}if ({access}.HasValue)");
            sb.AppendLine($"{pad}{{");
            EmitValue(sb, canonicalType, shape, number, access + ".Value", buffer, pad + "    ", objectShapes);
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitValue(
            StringBuilder sb, string canonicalType, FoxRunProtobufTypeShape shape, int number, string access,
            string buffer, string pad, IReadOnlyList<FoxRunProtobufTypeShape> objectShapes)
        {
            if (shape != null && shape.Kind == FoxRunProtobufTypeShapeKind.Object)
            {
                var index = IndexOfObjectShape(objectShapes, shape.TypeName);
                if (index < 0) throw new InvalidOperationException("FoxRun Protobuf object writer shape was not registered.");
                sb.AppendLine($"{pad}__WriteFoxRunProtobufObject_{index}({buffer}, {number}, {access});");
                return;
            }
            if (shape != null && shape.Kind == FoxRunProtobufTypeShapeKind.Enum)
            {
                sb.AppendLine($"{pad}FoxRunProtobufWire.WriteInt32({buffer}, {number}, (int){access});");
                return;
            }
            var method = WriterMethod(shape?.CanonicalType ?? canonicalType);
            if (method == null) throw new InvalidOperationException("FoxRun Protobuf emission does not support '" + canonicalType + "'.");
            sb.AppendLine($"{pad}FoxRunProtobufWire.{method}({buffer}, {number}, {access});");
        }

        private static void EmitObjectWriter(StringBuilder sb, FoxRunProtobufTypeShape shape, int index, string pad, IReadOnlyList<FoxRunProtobufTypeShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void __WriteFoxRunProtobufObject_{index}(global::System.Collections.Generic.List<byte> __target, int __fieldNumber, global::{shape.TypeName} __value)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if ((object)__value == null) return;");
            sb.AppendLine($"{pad}        var __nested = new global::System.Collections.Generic.List<byte>(32);");
            foreach (var field in shape.Fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var number = FoxRunProtobufFieldNumber.Resolve(shape.TypeName + "|" + field.MemberName, field.ProtobufFieldNumber);
                var access = "__value." + field.MemberName;
                if (field.Repeated)
                {
                    sb.AppendLine($"{pad}        if ({access} != null) foreach (var __item in {access})");
                    EmitOptionalValue(
                        sb,
                        field.TypeShape.CanonicalType,
                        field.TypeShape,
                        number,
                        "__item",
                        "__nested",
                        pad + "            ",
                        objectShapes,
                        field.IsNullable);
                }
                else
                    EmitOptionalValue(
                        sb,
                        field.TypeShape.CanonicalType,
                        field.TypeShape,
                        number,
                        access,
                        "__nested",
                        pad + "        ",
                        objectShapes,
                        field.IsNullable);
            }
            sb.AppendLine($"{pad}        FoxRunProtobufWire.WriteBytes(__target, __fieldNumber, __nested);");
            sb.AppendLine($"{pad}    }}");
        }

        private static void CollectObjects(FoxRunProtobufTypeShape shape, ICollection<FoxRunProtobufTypeShape> shapes)
        {
            if (shape == null || shape.Kind != FoxRunProtobufTypeShapeKind.Object || IndexOfObjectShape(shapes, shape.TypeName) >= 0) return;
            shapes.Add(shape);
            foreach (var field in shape.Fields) CollectObjects(field.TypeShape, shapes);
        }

        private static int IndexOfObjectShape(IEnumerable<FoxRunProtobufTypeShape> shapes, string typeName)
        {
            var index = 0;
            foreach (var shape in shapes)
            {
                if (string.Equals(shape.TypeName, typeName, StringComparison.Ordinal)) return index;
                index++;
            }
            return -1;
        }

        private static string WriterMethod(string canonicalType)
        {
            switch (canonicalType)
            {
                case "bool": return "WriteBool";
                case "int8":
                case "int16":
                case "int32": return "WriteInt32";
                case "uint8":
                case "uint16":
                case "uint32": return "WriteUInt32";
                case "int64": return "WriteInt64";
                case "uint64": return "WriteUInt64";
                case "float32": return "WriteFloat";
                case "float64": return "WriteDouble";
                case "string": return "WriteString";
                case "unity.vector2.float32": return "WriteVector2";
                case "unity.vector3.float32": return "WriteVector3";
                case "unity.quaternion.float32": return "WriteQuaternion";
                case "unity.color.float32": return "WriteColor";
                default: return null;
            }
        }

        private static bool IsCollection(string typeName)
        {
            var type = typeName ?? string.Empty;
            return type.EndsWith("[]", StringComparison.Ordinal)
                   || type.IndexOf("List<", StringComparison.Ordinal) >= 0
                   || type.IndexOf("IList<", StringComparison.Ordinal) >= 0
                   || type.IndexOf("IReadOnlyList<", StringComparison.Ordinal) >= 0;
        }

        private static bool IsCollectionElementNullable(string typeName)
        {
            if (!TryGetCollectionElementType(typeName, out var elementType))
                return false;
            return IsNullableValueType(elementType);
        }

        private static bool TryGetCollectionElementType(string typeName, out string elementType)
        {
            var type = (typeName ?? string.Empty).Trim();
            if (type.EndsWith("[]", StringComparison.Ordinal))
            {
                elementType = type.Substring(0, type.Length - 2).Trim();
                return elementType.Length > 0;
            }

            return TryGetSingleGenericArgument(type, "List<", out elementType)
                   || TryGetSingleGenericArgument(type, "System.Collections.Generic.List<", out elementType)
                   || TryGetSingleGenericArgument(type, "IList<", out elementType)
                   || TryGetSingleGenericArgument(type, "System.Collections.Generic.IList<", out elementType)
                   || TryGetSingleGenericArgument(type, "IReadOnlyList<", out elementType)
                   || TryGetSingleGenericArgument(type, "System.Collections.Generic.IReadOnlyList<", out elementType);
        }

        private static bool TryGetSingleGenericArgument(string type, string prefix, out string argument)
        {
            argument = string.Empty;
            if (!type.StartsWith(prefix, StringComparison.Ordinal) || !type.EndsWith(">", StringComparison.Ordinal))
                return false;

            argument = type.Substring(prefix.Length, type.Length - prefix.Length - 1).Trim();
            return argument.Length > 0;
        }

        private static bool IsNullableValueType(string typeName)
        {
            var type = (typeName ?? string.Empty).Trim();
            return type.EndsWith("?", StringComparison.Ordinal)
                   || (type.EndsWith(">", StringComparison.Ordinal)
                       && (type.StartsWith("Nullable<", StringComparison.Ordinal)
                           || type.StartsWith("System.Nullable<", StringComparison.Ordinal)));
        }
    }
}
