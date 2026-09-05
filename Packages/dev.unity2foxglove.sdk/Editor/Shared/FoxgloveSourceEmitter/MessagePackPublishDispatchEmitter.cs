// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits direct, deterministic typed MessagePack writers for FoxRun.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class MessagePackPublishDispatchEmitter
    {
        private sealed class ObjectShape
        {
            public ObjectShape(FoxRunTypeShape shape)
            {
                Shape = shape;
                Identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            }

            public FoxRunTypeShape Shape { get; }
            public string Identity { get; }
        }

        internal static bool UsesMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => string.Equals(
                TopicMetadataEmitter.EffectiveEncoding(fields),
                FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                StringComparison.Ordinal);

        internal static bool MayUseMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => CanEncodeMessagePack(fields)
               && (UsesMessagePack(fields)
                   || TopicMetadataEmitter.IsInherited(fields));

        internal static bool CanEncodeMessagePack(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields != null
               && fields.Count > 0
               && fields.All(field =>
                   FoxRunMessagePackTypeShapeRules.IsPublishSupported(
                       field.TypeShape,
                       field.CanonicalType));

        internal static void EmitFieldsAndBuilders(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var objectShapes = new List<ObjectShape>();
            foreach (var topic in topics)
            {
                if (!CanEncodeMessagePack(topicMap[topic]))
                    continue;
                foreach (var field in topicMap[topic])
                    CollectObjectShapes(field.TypeShape, objectShapes);
            }

            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (!CanEncodeMessagePack(fields))
                    continue;

                sb.AppendLine($"{pad}    private byte[] __foxRunLastMessagePack_{topicIndex};");
                EmitTopicBuilder(sb, fields, topicIndex, pad, objectShapes);
            }

            for (var shapeIndex = 0; shapeIndex < objectShapes.Count; shapeIndex++)
                EmitObjectWriter(sb, objectShapes[shapeIndex].Shape, shapeIndex, pad, objectShapes);
        }

        private static void EmitTopicBuilder(
            StringBuilder sb,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields,
            int topicIndex,
            string pad,
            IReadOnlyList<ObjectShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private byte[] __BuildFoxRunMessagePack_{topicIndex}()");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine(
                $"{pad}        using (var __writer = new global::Unity.FoxgloveSDK.Schemas.MsgPack.FoxgloveMsgPackWriter())");
            sb.AppendLine($"{pad}        {{");

            var ordered = fields
                .Select((field, index) => new { Field = field, Index = index })
                .OrderBy(candidate => candidate.Field.JsonFieldName, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Field.MemberName, StringComparer.Ordinal)
                .ToList();
            sb.AppendLine($"{pad}            __writer.WriteMapHeader({ordered.Count});");
            var counter = new Counter();
            foreach (var candidate in ordered)
            {
                sb.AppendLine(
                    $"{pad}            __writer.WriteString(\"{StringLiteralEmitter.CSharpStringLiteral(candidate.Field.JsonFieldName)}\");");
                EmitValue(
                    sb,
                    candidate.Field.TypeShape
                    ?? FoxRunTypeShape.Canonical(candidate.Field.CanonicalType),
                    "__foxRunCapture_" + topicIndex + "_" + candidate.Index,
                    "__writer",
                    pad + "            ",
                    objectShapes,
                    counter);
            }

            sb.AppendLine($"{pad}            return __writer.ToArray();");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitObjectWriter(
            StringBuilder sb,
            FoxRunTypeShape shape,
            int shapeIndex,
            string pad,
            IReadOnlyList<ObjectShape> objectShapes)
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
            var counter = new Counter();
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

        private static void EmitValue(
            StringBuilder sb,
            FoxRunTypeShape shape,
            string access,
            string writer,
            string pad,
            IReadOnlyList<ObjectShape> objectShapes,
            Counter counter)
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
            IReadOnlyList<ObjectShape> objectShapes,
            Counter counter)
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
                        $"{pad}__WriteFoxRunMessagePackObject_{FindObjectShape(shape, objectShapes)}({writer}, {access});");
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
            Counter counter)
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
            IReadOnlyList<ObjectShape> objectShapes,
            Counter counter)
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

        private static void CollectObjectShapes(
            FoxRunTypeShape shape,
            ICollection<ObjectShape> objectShapes)
        {
            if (shape == null)
                return;
            if (shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
                if (!objectShapes.Any(candidate =>
                        string.Equals(candidate.Identity, identity, StringComparison.Ordinal)))
                {
                    objectShapes.Add(new ObjectShape(shape));
                }
                foreach (var field in shape.Fields)
                    CollectObjectShapes(field.TypeShape, objectShapes);
                return;
            }
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
                CollectObjectShapes(shape.ElementShape, objectShapes);
        }

        private static int FindObjectShape(
            FoxRunTypeShape shape,
            IReadOnlyList<ObjectShape> objectShapes)
        {
            var identity = FoxRunMessagePackTypeShapeIdentity.Build(shape);
            for (var index = 0; index < objectShapes.Count; index++)
            {
                if (string.Equals(objectShapes[index].Identity, identity, StringComparison.Ordinal))
                    return index;
            }
            throw new InvalidOperationException("MessagePack object writer shape was not collected.");
        }

        private static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            return string.IsNullOrWhiteSpace(escaped)
                   || escaped.StartsWith("global::", StringComparison.Ordinal)
                ? escaped
                : "global::" + escaped;
        }

        private sealed class Counter
        {
            private int _value;
            public int Next() => _value++;
        }
    }
}
