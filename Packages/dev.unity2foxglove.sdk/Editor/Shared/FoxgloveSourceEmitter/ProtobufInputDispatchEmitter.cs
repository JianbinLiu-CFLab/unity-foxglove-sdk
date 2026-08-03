// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits direct generated FoxRun Protobuf inbound reader calls.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class ProtobufInputDispatchEmitter
    {
        internal static string ReaderCall(int fieldNumber, string typeName, FoxRunTypeShape shape, int topicIndex)
        {
            if (IsCollectionTypeName(typeName))
                return "__TryReadFoxRunProtobufCollection_" + topicIndex + "(payload, out " + GlobalTypeName(typeName) + " __value, out error)";

            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape != null && shape.Kind == FoxRunTypeShapeKind.Object)
                return "__TryReadFoxRunProtobufObject_" + topicIndex + "_0(payload, out " + GlobalTypeName(typeName) + " __value, out error)";
            if (shape != null && shape.Kind == FoxRunTypeShapeKind.Enum)
                return "__TryReadFoxRunProtobufEnum_" + topicIndex + "(payload, out " + GlobalTypeName(typeName) + " __value, out error)";
            return "FoxRunInboundProtobuf.TryRead(payload, " + fieldNumber + ", out " + GlobalTypeName(typeName) + " __value, out error)";
        }

        internal static void EmitReaders(
            StringBuilder sb,
            string declaringType,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            string pad)
        {
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                if (!UsesProtobuf(member.Encoding)
                    || member.TypeShape == null)
                    continue;
                var shapes = new List<FoxRunTypeShape>();
                CollectObjects(member.TypeShape, shapes);
                for (var shapeIndex = 0; shapeIndex < shapes.Count; shapeIndex++)
                    EmitObjectReader(
                        sb,
                        shapes[shapeIndex],
                        member.ProtobufMetadata?.TypeMetadata,
                        index,
                        shapeIndex,
                        pad,
                        shapes);
                if (IsCollectionTypeName(member.TypeName))
                    EmitCollectionReader(sb, declaringType, member, index, pad, shapes);
                else if (member.TypeShape.Kind == FoxRunTypeShapeKind.Enum)
                    EmitEnumReader(sb, declaringType, member, index, pad);
            }
        }

        private static void EmitEnumReader(
            StringBuilder sb,
            string declaringType,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            string pad)
        {
            var number = FoxRunProtobufFieldNumber.Resolve(
                FoxRunProtobufContractBuilder.BuildFieldIdentity(
                    declaringType,
                    member.Topic,
                    member.SchemaName,
                    member.MemberName),
                member.ProtobufFieldNumber);
            var type = GlobalTypeName(member.TypeName);
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static bool __TryReadFoxRunProtobufEnum_{index}(byte[] payload, out {type} value, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (!FoxRunInboundProtobuf.TryRead(payload, {number}, out int __raw, out error)) {{ value = default; return false; }}");
            sb.AppendLine($"{pad}        value = ({type})__raw; error = string.Empty; return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitObjectReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata rootMetadata,
            int rootIndex,
            int shapeIndex,
            string pad,
            IReadOnlyList<FoxRunTypeShape> shapes)
        {
            var typeMetadata = FindTypeMetadata(rootMetadata, shape.TypeName);
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static bool __TryReadFoxRunProtobufObject_{rootIndex}_{shapeIndex}(byte[] payload, out global::{shape.TypeName} value, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var __fields = new global::System.Collections.Generic.List<FoxRunProtobufField>();");
            sb.AppendLine($"{pad}        if (!FoxRunInboundProtobuf.TryReadFields(payload, __fields, out error)) {{ value = default; return false; }}");
            sb.AppendLine($"{pad}        var __value = new global::{shape.TypeName}();");
            foreach (var field in shape.Fields.Where(candidate => candidate.Repeated).OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
                sb.AppendLine($"{pad}        var __{field.MemberName}Values = new global::System.Collections.Generic.List<{RepeatedStorageType(CollectionElementOrSelf(field.TypeShape))}>();");
            sb.AppendLine($"{pad}        foreach (var __field in __fields)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            switch (__field.Number)");
            sb.AppendLine($"{pad}            {{");
            foreach (var field in shape.Fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var number = FoxRunProtobufFieldNumber.Resolve(
                    shape.TypeName + "|" + field.MemberName,
                    typeMetadata?.Find(field.MemberName, field.JsonName)?.FieldNumber ?? 0);
                sb.AppendLine($"{pad}                case {number}:");
                sb.AppendLine($"{pad}                {{");
                EmitFieldDecode(sb, field, rootIndex, shapes, pad + "                    ");
                sb.AppendLine($"{pad}                    break;");
                sb.AppendLine($"{pad}                }}");
            }
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            foreach (var field in shape.Fields.Where(candidate => candidate.Repeated).OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var values = "__" + field.MemberName + "Values";
                var valueShape = CollectionElementOrSelf(field.TypeShape);
                if (valueShape.Kind == FoxRunTypeShapeKind.Enum)
                {
                    var typedValues = values + "Typed";
                    var enumType = CSharpType(valueShape);
                    sb.AppendLine($"{pad}        var {typedValues} = new global::System.Collections.Generic.List<{enumType}>({values}.Count);");
                    sb.AppendLine($"{pad}        foreach (var __raw in {values}) {typedValues}.Add(({enumType})__raw);");
                    values = typedValues;
                }
                var assignment = field.RepeatedCollectionKind == FoxRunCollectionKind.Array
                    ? values + ".ToArray()"
                    : values;
                sb.AppendLine($"{pad}        __value.{field.MemberName} = {assignment};");
            }
            sb.AppendLine($"{pad}        value = __value; error = string.Empty; return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitCollectionReader(
            StringBuilder sb,
            string declaringType,
            FoxgloveSourceEmitter.TopicMember member,
            int rootIndex,
            string pad,
            IReadOnlyList<FoxRunTypeShape> shapes)
        {
            var fieldNumber = FoxRunProtobufFieldNumber.Resolve(
                FoxRunProtobufContractBuilder.BuildFieldIdentity(
                    declaringType,
                    member.Topic,
                    member.SchemaName,
                    member.MemberName),
                member.ProtobufFieldNumber);
            var type = GlobalTypeName(member.TypeName);
            var valueShape = CollectionElementOrSelf(member.TypeShape);
            var storageType = RepeatedStorageType(valueShape);
            var value = "__values";

            sb.AppendLine();
            sb.AppendLine($"{pad}    private static bool __TryReadFoxRunProtobufCollection_{rootIndex}(byte[] payload, out {type} value, out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var __fields = new global::System.Collections.Generic.List<FoxRunProtobufField>();");
            sb.AppendLine($"{pad}        if (!FoxRunInboundProtobuf.TryReadFields(payload, __fields, out error)) {{ value = default; return false; }}");
            sb.AppendLine($"{pad}        var __values = new global::System.Collections.Generic.List<{storageType}>();");
            sb.AppendLine($"{pad}        foreach (var __field in __fields)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (__field.Number != {fieldNumber}) continue;");
            EmitValueDecode(sb, valueShape, true, "__values", rootIndex, shapes, pad + "            ");
            sb.AppendLine($"{pad}        }}");
            if (valueShape.Kind == FoxRunTypeShapeKind.Enum)
            {
                var enumType = CSharpType(valueShape);
                sb.AppendLine($"{pad}        var __typedValues = new global::System.Collections.Generic.List<{enumType}>(__values.Count);");
                sb.AppendLine($"{pad}        foreach (var __raw in __values) __typedValues.Add(({enumType})__raw);");
                value = IsArrayTypeName(member.TypeName) ? "__typedValues.ToArray()" : "__typedValues";
            }
            else
            {
                value = IsArrayTypeName(member.TypeName) ? "__values.ToArray()" : "__values";
            }
            sb.AppendLine($"{pad}        value = {value}; error = string.Empty; return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitFieldDecode(StringBuilder sb, FoxRunTypeField field, int rootIndex, IReadOnlyList<FoxRunTypeShape> shapes, string pad)
        {
            var target = field.Repeated ? "__" + field.MemberName + "Values" : "__value." + field.MemberName;
            EmitValueDecode(
                sb,
                field.Repeated ? CollectionElementOrSelf(field.TypeShape) : field.TypeShape,
                field.Repeated,
                target,
                rootIndex,
                shapes,
                pad);
        }

        private static void EmitValueDecode(
            StringBuilder sb,
            FoxRunTypeShape shape,
            bool repeated,
            string target,
            int rootIndex,
            IReadOnlyList<FoxRunTypeShape> shapes,
            string pad)
        {
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                if (repeated)
                {
                    sb.AppendLine($"{pad}if (!FoxRunInboundProtobuf.TryReadRepeatedInt32(__field, {target}, out error)) {{ value = default; return false; }}");
                }
                else
                {
                    var enumType = CSharpType(shape);
                    sb.AppendLine($"{pad}if (!FoxRunInboundProtobuf.TryDecodeInt32(__field, out int __raw, out error)) {{ value = default; return false; }}");
                    sb.AppendLine($"{pad}{target} = ({enumType})__raw;");
                }
                return;
            }
            if (repeated && TryGetRepeatedReader(shape, out var repeatedReader))
            {
                sb.AppendLine($"{pad}if (!FoxRunInboundProtobuf.{repeatedReader}(__field, {target}, out error)) {{ value = default; return false; }}");
                return;
            }
            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var childIndex = IndexOfObject(shapes, shape.TypeName);
                var objectType = CSharpType(shape);
                sb.AppendLine($"{pad}if (!FoxRunInboundProtobuf.TryDecodeMessage(__field, out var __payload, out error) || !__TryReadFoxRunProtobufObject_{rootIndex}_{childIndex}(__payload, out {objectType} __decoded, out error)) {{ value = default; return false; }}");
                sb.AppendLine($"{pad}{target}{(repeated ? ".Add(__decoded);" : " = __decoded;")}");
                return;
            }
            var type = CSharpType(shape);
            var decoder = DecoderMethod(shape);
            sb.AppendLine($"{pad}if (!FoxRunInboundProtobuf.{decoder}(__field, out {type} __decoded, out error)) {{ value = default; return false; }}");
            sb.AppendLine($"{pad}{target}{(repeated ? ".Add(__decoded);" : " = __decoded;")}");
        }

        private static string DecoderMethod(FoxRunTypeShape shape)
        {
            if (shape.Kind == FoxRunTypeShapeKind.Enum) return "TryDecodeInt32";
            switch (shape.CanonicalType)
            {
                case "bool": return "TryDecodeBool";
                case "int8": return "TryDecodeInt8";
                case "int16": return "TryDecodeInt16";
                case "int32": return "TryDecodeInt32";
                case "uint8": return "TryDecodeUInt8";
                case "uint16": return "TryDecodeUInt16";
                case "uint32": return "TryDecodeUInt32";
                case "int64": return "TryDecodeInt64";
                case "uint64": return "TryDecodeUInt64";
                case "float32": return "TryDecodeFloat";
                case "float64": return "TryDecodeDouble";
                case "string": return "TryDecodeString";
                case "unity.vector2.float32": return "TryDecodeVector2";
                case "unity.vector3.float32": return "TryDecodeVector3";
                case "unity.quaternion.float32": return "TryDecodeQuaternion";
                case "unity.color.float32": return "TryDecodeColor";
                default: throw new InvalidOperationException("Unsupported FoxRun Protobuf inbound field type: " + shape.CanonicalType);
            }
        }

        private static bool TryGetRepeatedReader(FoxRunTypeShape shape, out string reader)
        {
            switch (shape.CanonicalType)
            {
                case "bool": reader = "TryReadRepeatedBool"; return true;
                case "int8": reader = "TryReadRepeatedInt8"; return true;
                case "int16": reader = "TryReadRepeatedInt16"; return true;
                case "int32": reader = "TryReadRepeatedInt32"; return true;
                case "uint8": reader = "TryReadRepeatedUInt8"; return true;
                case "uint16": reader = "TryReadRepeatedUInt16"; return true;
                case "uint32": reader = "TryReadRepeatedUInt32"; return true;
                case "int64": reader = "TryReadRepeatedInt64"; return true;
                case "uint64": reader = "TryReadRepeatedUInt64"; return true;
                case "float32": reader = "TryReadRepeatedFloat"; return true;
                case "float64": reader = "TryReadRepeatedDouble"; return true;
                default: reader = null; return false;
            }
        }

        private static void CollectObjects(FoxRunTypeShape shape, ICollection<FoxRunTypeShape> shapes)
        {
            if (shape == null)
                return;
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                CollectObjects(shape.ElementShape, shapes);
                return;
            }
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape.Kind != FoxRunTypeShapeKind.Object || IndexOfObject(shapes, shape.TypeName) >= 0)
                return;
            shapes.Add(shape);
            foreach (var field in shape.Fields) CollectObjects(field.TypeShape, shapes);
        }

        private static FoxRunTypeShape CollectionElementOrSelf(FoxRunTypeShape shape)
            => shape != null && shape.Kind == FoxRunTypeShapeKind.Collection
                ? shape.ElementShape
                : shape;

        private static int IndexOfObject(IEnumerable<FoxRunTypeShape> shapes, string typeName)
        {
            var index = 0;
            foreach (var shape in shapes)
            {
                if (string.Equals(shape.TypeName, typeName, StringComparison.Ordinal)) return index;
                index++;
            }
            return -1;
        }

        private static FoxRunProtobufTypeMetadata FindTypeMetadata(
            FoxRunProtobufTypeMetadata metadata,
            string typeName)
        {
            if (metadata == null)
                return null;
            if (string.Equals(metadata.TypeName, typeName, StringComparison.Ordinal))
                return metadata;
            foreach (var field in metadata.Fields)
            {
                var found = FindTypeMetadata(field.TypeMetadata, typeName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static string CSharpType(FoxRunTypeShape shape)
        {
            if (shape.Kind == FoxRunTypeShapeKind.Object)
                return GlobalTypeName(shape.TypeName);
            if (shape.Kind == FoxRunTypeShapeKind.Enum) return GlobalTypeName(shape.TypeName);
            switch (shape.CanonicalType)
            {
                case "bool": return "bool";
                case "int8": return "sbyte";
                case "int16": return "short";
                case "int32": return "int";
                case "uint8": return "byte";
                case "uint16": return "ushort";
                case "uint32": return "uint";
                case "int64": return "long";
                case "uint64": return "ulong";
                case "float32": return "float";
                case "float64": return "double";
                case "string": return "string";
                case "unity.vector2.float32": return "global::UnityEngine.Vector2";
                case "unity.vector3.float32": return "global::UnityEngine.Vector3";
                case "unity.quaternion.float32": return "global::UnityEngine.Quaternion";
                case "unity.color.float32": return "global::UnityEngine.Color";
                default: throw new InvalidOperationException("Unsupported FoxRun Protobuf DTO field type: " + shape.CanonicalType);
            }
        }

        private static string RepeatedStorageType(FoxRunTypeShape shape)
            => shape.Kind == FoxRunTypeShapeKind.Enum ? "int" : CSharpType(shape);

        private static bool IsCollectionTypeName(string typeName)
        {
            var type = typeName ?? string.Empty;
            return IsArrayTypeName(type)
                   || type.IndexOf("List<", StringComparison.Ordinal) >= 0
                   || type.IndexOf("IList<", StringComparison.Ordinal) >= 0
                   || type.IndexOf("IReadOnlyList<", StringComparison.Ordinal) >= 0;
        }

        private static bool IsArrayTypeName(string typeName)
            => (typeName ?? string.Empty).TrimEnd().EndsWith("[]", StringComparison.Ordinal);

        private static string GlobalTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", StringComparison.Ordinal))
                return typeName;
            if (typeName.EndsWith("[]", StringComparison.Ordinal))
                return GlobalTypeName(typeName.Substring(0, typeName.Length - 2)) + "[]";
            switch (typeName)
            {
                case "bool": case "byte": case "sbyte": case "short": case "ushort":
                case "int": case "uint": case "long": case "ulong": case "float":
                case "double": case "decimal": case "string": case "char": case "object":
                    return typeName;
                default:
                    return "global::" + typeName;
            }
        }

        private static bool UsesProtobuf(string encoding)
            => string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal)
               || string.Equals(encoding, FoxRunGenerationDescriptorConstants.InheritEncoding, StringComparison.Ordinal);
    }
}
