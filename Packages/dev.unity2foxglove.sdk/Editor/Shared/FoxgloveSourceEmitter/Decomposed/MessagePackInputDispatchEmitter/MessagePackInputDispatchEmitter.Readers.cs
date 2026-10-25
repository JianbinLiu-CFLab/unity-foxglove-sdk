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
        private static void EmitTopicDecoder(
            StringBuilder sb,
            TransactionTopic topic,
            int transactionIndex,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var resultType = topic.IsStream
                ? GlobalTypeName(topic.Members[0].TypeName)
                : "__FoxRunMessagePackTransaction_" + transactionIndex;
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static bool __TryDecodeFoxRunMessagePackTopic_{transactionIndex}(");
            sb.AppendLine($"{pad}        byte[] payload,");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReadLimits limits,");
            sb.AppendLine($"{pad}        out {resultType} value,");
            sb.AppendLine($"{pad}        out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        value = default;");
            sb.AppendLine($"{pad}        error = string.Empty;");
            sb.AppendLine(
                $"{pad}        var __reader = new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReader(payload, limits);");

            sb.AppendLine(
                $"{pad}        if (!__reader.TryReadMapHeader(out var __count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = __reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                sb.AppendLine(
                    $"{pad}        var __seen_{memberIndex} = false;");
                sb.AppendLine(
                    $"{pad}        {GlobalTypeName(topic.Members[memberIndex].TypeName)} __value_{memberIndex} = default;");
            }
            sb.AppendLine(
                $"{pad}        var __keys = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
            sb.AppendLine($"{pad}        for (var __index = 0; __index < __count; __index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (!__reader.TryReadString(out var __key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                error = __reader.Error;");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            if (!__keys.Add(__key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine(
                $"{pad}                error = \"MessagePack object contains a duplicate key.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            switch (__key)");
            sb.AppendLine($"{pad}            {{");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                var member = topic.Members[memberIndex];
                var shape = member.TypeShape
                            ?? FoxRunTypeShape.Canonical(member.CanonicalType);
                var shapeIndex = FindShape(shape, shapes);
                sb.AppendLine(
                    $"{pad}                case \"{StringLiteralEmitter.CSharpStringLiteral(member.JsonFieldName)}\":");
                sb.AppendLine($"{pad}                    __seen_{memberIndex} = true;");
                sb.AppendLine(
                    $"{pad}                    if (!__TryReadFoxRunMessagePackValue_{shapeIndex}(__reader, out var __decoded_{memberIndex}, out error)) return false;");
                sb.AppendLine(
                    $"{pad}                    __value_{memberIndex} = __decoded_{memberIndex};");
                sb.AppendLine($"{pad}                    break;");
            }
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (!__reader.TrySkipValue())");
            sb.AppendLine($"{pad}                    {{");
            sb.AppendLine($"{pad}                        error = __reader.Error;");
            sb.AppendLine($"{pad}                        return false;");
            sb.AppendLine($"{pad}                    }}");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
            {
                sb.AppendLine($"{pad}        if (!__seen_{memberIndex})");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine(
                    $"{pad}            error = \"MessagePack object is missing a required known field.\";");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
            }

            sb.AppendLine($"{pad}        if (!__reader.TryComplete())");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = __reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            if (topic.IsStream)
            {
                sb.AppendLine($"{pad}        value = __value_0;");
            }
            else
            {
                sb.Append($"{pad}        value = new {resultType}(");
                for (var memberIndex = 0; memberIndex < topic.Members.Count; memberIndex++)
                {
                    if (memberIndex != 0)
                        sb.Append(", ");
                    sb.Append("__value_" + memberIndex);
                }
                sb.AppendLine(");");
            }
            sb.AppendLine($"{pad}        error = string.Empty;");
            sb.AppendLine($"{pad}        return true;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitShapeReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            int shapeIndex,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            var typeName = ClrType(shape);
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static bool __TryReadFoxRunMessagePackValue_{shapeIndex}(");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackReader reader,");
            sb.AppendLine($"{pad}        out {typeName} value,");
            sb.AppendLine($"{pad}        out string error)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        value = default;");
            sb.AppendLine($"{pad}        error = string.Empty;");
            if (CanBeNil(shape))
            {
                sb.AppendLine($"{pad}        if (!reader.TryReadNil(out var isNil))");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            error = reader.Error;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                if (shape.Kind == FoxRunTypeShapeKind.Object
                    && shape.IsValueType
                    && !shape.Nullable)
                {
                    sb.AppendLine($"{pad}        if (isNil)");
                    sb.AppendLine($"{pad}        {{");
                    sb.AppendLine(
                        $"{pad}            error = \"MessagePack nil cannot be assigned to a non-nullable value type.\";");
                    sb.AppendLine($"{pad}            return false;");
                    sb.AppendLine($"{pad}        }}");
                }
                else
                {
                    sb.AppendLine($"{pad}        if (isNil) return true;");
                }
            }

            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    EmitCanonicalReader(sb, shape, pad);
                    break;
                case FoxRunTypeShapeKind.Enum:
                    var enumNumbers = shape.EnumValues
                        .Select(value => value.Number)
                        .Distinct()
                        .OrderBy(value => value)
                        .ToArray();
                    if (enumNumbers.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "Typed MessagePack enum shapes require at least one declared value.");
                    }
                    sb.AppendLine($"{pad}        if (!reader.TryReadInt32(out var decoded))");
                    sb.AppendLine($"{pad}        {{");
                    sb.AppendLine($"{pad}            error = reader.Error;");
                    sb.AppendLine($"{pad}            return false;");
                    sb.AppendLine($"{pad}        }}");
                    sb.AppendLine($"{pad}        switch (decoded)");
                    sb.AppendLine($"{pad}        {{");
                    foreach (var number in enumNumbers)
                    {
                        sb.AppendLine(
                            $"{pad}            case {number.ToString(CultureInfo.InvariantCulture)}:");
                    }
                    sb.AppendLine($"{pad}                break;");
                    sb.AppendLine($"{pad}            default:");
                    sb.AppendLine(
                        $"{pad}                error = \"MessagePack value is not a declared enum value.\";");
                    sb.AppendLine($"{pad}                return false;");
                    sb.AppendLine($"{pad}        }}");
                    sb.AppendLine(
                        $"{pad}        value = ({GlobalTypeName(shape.TypeName)})decoded;");
                    sb.AppendLine($"{pad}        return true;");
                    break;
                case FoxRunTypeShapeKind.Collection:
                    EmitCollectionReader(sb, shape, pad, shapes);
                    break;
                case FoxRunTypeShapeKind.Object:
                    EmitObjectReader(sb, shape, pad, shapes);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported MessagePack input shape.");
            }
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitCanonicalReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad)
        {
            string method;
            switch (shape.CanonicalType)
            {
                case "bool": method = "TryReadBoolean"; break;
                case "int8": method = "TryReadSByte"; break;
                case "uint8": method = "TryReadByte"; break;
                case "int16": method = "TryReadInt16"; break;
                case "uint16": method = "TryReadUInt16"; break;
                case "int32": method = "TryReadInt32"; break;
                case "uint32": method = "TryReadUInt32"; break;
                case "int64": method = "TryReadInt64"; break;
                case "uint64": method = "TryReadUInt64"; break;
                case "float32": method = "TryReadSingle"; break;
                case "float64": method = "TryReadDouble"; break;
                case "string": method = "TryReadString"; break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported canonical MessagePack input type '"
                        + shape.CanonicalType
                        + "'.");
            }
            sb.AppendLine($"{pad}        if (!reader.{method}(out var decoded))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        value = decoded;");
            sb.AppendLine($"{pad}        return true;");
        }

        private static void EmitCollectionReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            if (shape.CollectionKind == FoxRunCollectionKind.Binary)
            {
                sb.AppendLine($"{pad}        if (!reader.TryReadBinary(out value))");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            error = reader.Error;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        return true;");
                return;
            }

            var elementType = ClrType(shape.ElementShape);
            var elementReader = FindShape(shape.ElementShape, shapes);
            sb.AppendLine($"{pad}        if (!reader.TryReadArrayHeader(out var count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            if (shape.CollectionKind == FoxRunCollectionKind.Array)
                sb.AppendLine($"{pad}        var collection = new {elementType}[count];");
            else
                sb.AppendLine(
                    $"{pad}        var collection = new global::System.Collections.Generic.List<{elementType}>(count);");
            sb.AppendLine($"{pad}        for (var index = 0; index < count; index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine(
                $"{pad}            if (!__TryReadFoxRunMessagePackValue_{elementReader}(reader, out {elementType} item, out error)) return false;");
            if (shape.CollectionKind == FoxRunCollectionKind.Array)
                sb.AppendLine($"{pad}            collection[index] = item;");
            else
                sb.AppendLine($"{pad}            collection.Add(item);");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        value = collection;");
            sb.AppendLine($"{pad}        return true;");
        }

        private static void EmitObjectReader(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string pad,
            IReadOnlyList<ShapeEntry> shapes)
        {
            sb.AppendLine($"{pad}        if (!reader.TryReadMapHeader(out var count))");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            error = reader.Error;");
            sb.AppendLine($"{pad}            return false;");
            sb.AppendLine($"{pad}        }}");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                var field = shape.Fields[index];
                sb.AppendLine($"{pad}        var seen_{index} = false;");
                sb.AppendLine(
                    $"{pad}        {ClrType(field.TypeShape)} field_{index} = default;");
            }
            sb.AppendLine(
                $"{pad}        var keys = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal);");
            sb.AppendLine($"{pad}        for (var index = 0; index < count; index++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            if (!reader.TryReadString(out var key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                error = reader.Error;");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            if (!keys.Add(key))");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine(
                $"{pad}                error = \"MessagePack object contains a duplicate key.\";");
            sb.AppendLine($"{pad}                return false;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}            switch (key)");
            sb.AppendLine($"{pad}            {{");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                var field = shape.Fields[index];
                var readerIndex = FindShape(field.TypeShape, shapes);
                sb.AppendLine(
                    $"{pad}                case \"{StringLiteralEmitter.CSharpStringLiteral(field.JsonName)}\":");
                sb.AppendLine($"{pad}                    seen_{index} = true;");
                sb.AppendLine(
                    $"{pad}                    if (!__TryReadFoxRunMessagePackValue_{readerIndex}(reader, out field_{index}, out error)) return false;");
                sb.AppendLine($"{pad}                    break;");
            }
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (!reader.TrySkipValue())");
            sb.AppendLine($"{pad}                    {{");
            sb.AppendLine($"{pad}                        error = reader.Error;");
            sb.AppendLine($"{pad}                        return false;");
            sb.AppendLine($"{pad}                    }}");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                sb.AppendLine($"{pad}        if (!seen_{index})");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine(
                    $"{pad}            error = \"MessagePack object is missing a required known field.\";");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
            }
            sb.AppendLine($"{pad}        var decoded = new {GlobalTypeName(shape.TypeName)}();");
            for (var index = 0; index < shape.Fields.Count; index++)
            {
                sb.AppendLine(
                    $"{pad}        decoded.{IdentifierUtils.EscapeIdentifier(shape.Fields[index].MemberName)} = field_{index};");
            }
            sb.AppendLine($"{pad}        value = decoded;");
            sb.AppendLine($"{pad}        return true;");
        }

    }
}
