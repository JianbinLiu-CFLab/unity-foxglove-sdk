// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/TypedMessagePack
// Purpose: Encoding-neutral typed MessagePack source emission shared by FoxRun and Component generators.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class TypedMessagePackWriterEmitter
    {        internal sealed class TypedMessagePackObjectShape
        {
            public TypedMessagePackObjectShape(FoxRunTypeShape shape)
            {
                Shape = shape;
                Identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            }

            public FoxRunTypeShape Shape { get; }
            public string Identity { get; }
        }

        public static void EmitValue(StringBuilder sb, FoxRunTypeShape shape, string access, string writer, string pad)
        {
            var objectShapes = new List<TypedMessagePackObjectShape>();
            var counter = new TypedMessagePackCounter();
            EmitValue(sb, shape, access, writer, pad, objectShapes, counter);
        }
        internal static void EmitObjectWriter(
            StringBuilder sb,
            FoxRunTypeShape shape,
            int shapeIndex,
            string pad,
            IReadOnlyList<TypedMessagePackObjectShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static void __WriteFoxRunMessagePackObject_{shapeIndex}(");
            sb.AppendLine(
                $"{pad}        global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackWriter __writer,");
            var valueType = GlobalTypeName(shape.TypeName);
            var nullableValueType = shape.IsValueType && shape.Nullable;
            var parameterType = nullableValueType
                ? "global::System.Nullable<" + valueType + ">"
                : valueType;
            sb.AppendLine($"{pad}        {parameterType} __value)");
            sb.AppendLine($"{pad}    {{");
            if (nullableValueType)
            {
                sb.AppendLine($"{pad}        if (!__value.HasValue)");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            __writer.WriteNil();");
                sb.AppendLine($"{pad}            return;");
                sb.AppendLine($"{pad}        }}");
            }
            else if (!shape.IsValueType)
            {
                sb.AppendLine(
                    $"{pad}        if ((object)__value == null)");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            __writer.WriteNil();");
                sb.AppendLine($"{pad}            return;");
                sb.AppendLine($"{pad}        }}");
            }

            // FoxRunTypeShape owns canonical field order. In particular, Unity
            // component shapes deliberately preserve x/y/z/w and r/g/b/a
            // rather than lexical order.
            var ordered = shape.Fields;
            var objectAccess = nullableValueType
                ? "__value.Value"
                : "__value";
            sb.AppendLine($"{pad}        __writer.WriteMapHeader({ordered.Count});");
            var counter = new TypedMessagePackCounter();
            foreach (var field in ordered)
            {
                sb.AppendLine(
                    $"{pad}        __writer.WriteString(\"{StringLiteralEmitter.CSharpStringLiteral(field.JsonName)}\");");
                EmitValue(
                    sb,
                    field.TypeShape,
                    objectAccess + "." + IdentifierUtils.EscapeIdentifier(field.MemberName),
                    "__writer",
                    pad + "        ",
                    objectShapes,
                    counter);
            }
            sb.AppendLine($"{pad}    }}");
        }

        internal static void EmitValue(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string access,
            string writer,
            string pad,
            IReadOnlyList<TypedMessagePackObjectShape> objectShapes,
            TypedMessagePackCounter counter)
        {
            if (shape == null)
                throw new InvalidOperationException("Typed MessagePack publication requires a complete type shape.");

            if (shape.Nullable && shape.Kind != FoxRunTypeShapeKind.Object)
            {
                var nullableValueType =
                    shape.Kind == FoxRunTypeShapeKind.Enum
                    || shape.Kind == FoxRunTypeShapeKind.Canonical
                    && !string.Equals(
                        shape.CanonicalType,
                        "string",
                        StringComparison.Ordinal);
                sb.AppendLine(
                    nullableValueType
                        ? $"{pad}if (!{access}.HasValue)"
                        : $"{pad}if ((object){access} == null)");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    {writer}.WriteNil();");
                sb.AppendLine($"{pad}}}");
                sb.AppendLine($"{pad}else");
                sb.AppendLine($"{pad}{{");
                var value = nullableValueType
                    ? access + ".Value"
                    : access;
                EmitNonNullValue(
                    sb,
                    shape,
                    value,
                    writer,
                    pad + "    ",
                    objectShapes,
                    counter);
                sb.AppendLine($"{pad}}}");
                return;
            }

            EmitNonNullValue(sb, shape, access, writer, pad, objectShapes, counter);
        }

        private static void EmitNonNullValue(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string access,
            string writer,
            string pad,
            IReadOnlyList<TypedMessagePackObjectShape> objectShapes,
            TypedMessagePackCounter counter)
        {
            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    EmitCanonical(sb, shape.CanonicalType, access, writer, pad);
                    return;
                case FoxRunTypeShapeKind.Enum:
                    EmitEnum(
                        sb,
                        shape,
                        access,
                        writer,
                        pad,
                        counter);
                    return;
                case FoxRunTypeShapeKind.Object:
                    sb.AppendLine(
                        $"{pad}__WriteFoxRunMessagePackObject_{FindTypedMessagePackObjectShape(shape, objectShapes)}({writer}, {access});");
                    return;
                case FoxRunTypeShapeKind.Collection:
                    EmitCollection(sb, shape, access, writer, pad, objectShapes, counter);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported typed MessagePack shape.");
            }
        }

        private static void EmitEnum(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string access,
            string writer,
            string pad,
            TypedMessagePackCounter counter)
        {
            var suffix = counter.Next();
            var numbers = shape.EnumValues
                .Select(value => value.Number)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (numbers.Length == 0)
            {
                throw new InvalidOperationException(
                    "Typed MessagePack enum shapes require at least one declared value.");
            }
            sb.AppendLine(
                $"{pad}var __enum_{suffix} = checked((int){access});");
            sb.AppendLine($"{pad}switch (__enum_{suffix})");
            sb.AppendLine($"{pad}{{");
            foreach (var number in numbers)
            {
                sb.AppendLine(
                    $"{pad}    case {number.ToString(CultureInfo.InvariantCulture)}:");
            }
            sb.AppendLine($"{pad}        break;");
            sb.AppendLine($"{pad}    default:");
            sb.AppendLine(
                $"{pad}        throw new global::System.InvalidOperationException(\"FoxRun MessagePack value is not a declared enum value.\");");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine(
                $"{pad}{writer}.WriteInt32(__enum_{suffix});");
        }

        private static void EmitCollection(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string access,
            string writer,
            string pad,
            IReadOnlyList<TypedMessagePackObjectShape> objectShapes,
            TypedMessagePackCounter counter)
        {
            if (shape.CollectionKind == FoxRunCollectionKind.Binary)
            {
                sb.AppendLine($"{pad}{writer}.WriteBinary({access});");
                return;
            }

            var suffix = counter.Next();
            sb.AppendLine($"{pad}if ({access} == null)");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    {writer}.WriteNil();");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine($"{pad}else");
            sb.AppendLine($"{pad}{{");
            var countMember = shape.CollectionKind == FoxRunCollectionKind.Array
                ? "Length"
                : "Count";
            sb.AppendLine($"{pad}    var __count_{suffix} = {access}.{countMember};");
            sb.AppendLine($"{pad}    {writer}.WriteArrayHeader(__count_{suffix});");
            sb.AppendLine(
                $"{pad}    for (var __index_{suffix} = 0; __index_{suffix} < __count_{suffix}; __index_{suffix}++)");
            sb.AppendLine($"{pad}    {{");
            EmitValue(
                sb,
                shape.ElementShape,
                access + "[__index_" + suffix + "]",
                writer,
                pad + "        ",
                objectShapes,
                counter);
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitCanonical(
            StringBuilder sb,
            string canonicalType,
            string access,
            string writer,
            string pad)
        {
            switch (canonicalType)
            {
                case "bool":
                    sb.AppendLine($"{pad}{writer}.WriteBool({access});");
                    return;
                case "int8":
                case "int16":
                case "int32":
                    sb.AppendLine($"{pad}{writer}.WriteInt32((int){access});");
                    return;
                case "uint8":
                case "uint16":
                case "uint32":
                    sb.AppendLine($"{pad}{writer}.WriteUInt32((uint){access});");
                    return;
                case "int64":
                    sb.AppendLine($"{pad}{writer}.WriteInt64({access});");
                    return;
                case "uint64":
                    sb.AppendLine($"{pad}{writer}.WriteUInt64({access});");
                    return;
                case "float32":
                    sb.AppendLine($"{pad}{writer}.WriteFloat({access});");
                    return;
                case "float64":
                    sb.AppendLine($"{pad}{writer}.WriteDouble({access});");
                    return;
                case "string":
                    sb.AppendLine($"{pad}{writer}.WriteString({access});");
                    return;
                default:
                    throw new InvalidOperationException(
                        "Unsupported canonical MessagePack type '" + canonicalType + "'.");
            }
        }

        internal static void CollectTypedMessagePackObjectShapes(
            FoxRunTypeShape shape,
            ICollection<TypedMessagePackObjectShape> objectShapes)
        {
            if (shape == null)
                return;
            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
                if (!objectShapes.Any(candidate =>
                        string.Equals(candidate.Identity, identity, StringComparison.Ordinal)))
                {
                    objectShapes.Add(new TypedMessagePackObjectShape(shape));
                }
                foreach (var field in shape.Fields)
                    CollectTypedMessagePackObjectShapes(field.TypeShape, objectShapes);
                return;
            }
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
                CollectTypedMessagePackObjectShapes(shape.ElementShape, objectShapes);
        }

        private static int FindTypedMessagePackObjectShape(
            FoxRunTypeShape shape,
            IReadOnlyList<TypedMessagePackObjectShape> objectShapes)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            for (var index = 0; index < objectShapes.Count; index++)
            {
                if (string.Equals(objectShapes[index].Identity, identity, StringComparison.Ordinal))
                    return index;
            }
            throw new InvalidOperationException("MessagePack object writer shape was not collected.");
        }

        internal static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            return string.IsNullOrWhiteSpace(escaped)
                   || escaped.StartsWith("global::", StringComparison.Ordinal)
                ? escaped
                : "global::" + escaped;
        }

        internal sealed class TypedMessagePackCounter
        {
            private int _value;
            public int Next() => _value++;
        }

    }
}
