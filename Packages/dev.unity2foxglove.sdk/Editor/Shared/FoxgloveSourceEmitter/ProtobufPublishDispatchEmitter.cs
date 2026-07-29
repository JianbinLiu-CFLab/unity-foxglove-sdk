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
        private sealed class ObjectWriterShape
        {
            public ObjectWriterShape(
                FoxRunTypeShape shape,
                FoxRunProtobufTypeMetadata metadata)
            {
                Shape = shape ?? throw new ArgumentNullException(nameof(shape));
                Metadata = metadata;
                Identity = ObjectWriterIdentity(shape, metadata);
            }

            public FoxRunTypeShape Shape { get; }
            public FoxRunProtobufTypeMetadata Metadata { get; }
            public string Identity { get; }
        }

        internal static void EmitBuilders(
            StringBuilder sb,
            string declaringType,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var objectShapes = new List<ObjectWriterShape>();
            foreach (var fields in topicMap.Values)
                foreach (var field in fields)
                {
                    CollectObjects(
                        field.TypeShape,
                        field.ProtobufMetadata?.TypeMetadata,
                        objectShapes);
                    if (field.Mode == 3
                        && PublishDispatchEmitter.NeedsStructuralOriginSnapshot(fields))
                    {
                        var originShape = OriginFingerprintShape(field);
                        CollectObjects(
                            originShape,
                            ReferenceEquals(originShape, field.TypeShape)
                                ? field.ProtobufMetadata?.TypeMetadata
                                : null,
                            objectShapes);
                    }
                }

            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (TopicMetadataEmitter.UsesProtobuf(fields))
                {
                    EmitTopicBuilder(
                        sb,
                        declaringType,
                        fields,
                        topicIndex,
                        "__BuildFoxRunProtobuf_" + topicIndex,
                        useCapture: true,
                        originFingerprint: false,
                        pad,
                        objectShapes);
                }

                if (fields.Any(field => field.Mode == 3)
                    && PublishDispatchEmitter.NeedsStructuralOriginSnapshot(fields))
                {
                    EmitTopicBuilder(
                        sb,
                        declaringType,
                        fields,
                        topicIndex,
                        "__BuildFoxRunOriginFingerprint_" + topicIndex,
                        useCapture: false,
                        originFingerprint: true,
                        pad,
                        objectShapes);
                }
            }

            for (var i = 0; i < objectShapes.Count; i++)
                EmitObjectWriter(
                    sb,
                    objectShapes[i].Shape,
                    objectShapes[i].Metadata,
                    i,
                    pad,
                    objectShapes);
        }

        private static void EmitTopicBuilder(
            StringBuilder sb,
            string declaringType,
            List<FoxgloveSourceEmitter.TopicMember> fields,
            int topicIndex,
            string methodName,
            bool useCapture,
            bool originFingerprint,
            string pad,
            IReadOnlyList<ObjectWriterShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private byte[] {methodName}()");
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
                var fieldIndex = fields.IndexOf(field);
                var access = useCapture
                    ? "__foxRunCapture_" + topicIndex + "_" + fieldIndex
                    : TypeExprEmitter.MemberAccess(field.MemberName);
                var shape = originFingerprint
                    ? OriginFingerprintShape(field)
                    : field.TypeShape;
                var metadata = ReferenceEquals(shape, field.TypeShape)
                    ? field.ProtobufMetadata?.TypeMetadata
                    : null;
                EmitWriteField(
                    sb,
                    field,
                    shape,
                    metadata,
                    number,
                    access,
                    "__payload",
                    pad + "        ",
                    objectShapes);
            }
            sb.AppendLine($"{pad}        return __payload.ToArray();");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitWriteField(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember field,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata,
            int number,
            string access,
            string buffer,
            string pad,
            IReadOnlyList<ObjectWriterShape> objectShapes)
        {
            if (IsCollection(field.TypeName))
            {
                shape = CollectionElementOrSelf(shape);
                sb.AppendLine($"{pad}if ({access} != null)");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    foreach (var __item in {access})");
                EmitOptionalValue(
                    sb,
                    shape?.CanonicalType ?? field.CanonicalType,
                    shape,
                    metadata,
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
                shape?.CanonicalType ?? field.CanonicalType,
                shape,
                metadata,
                number,
                access,
                buffer,
                pad,
                objectShapes,
                IsNullableValueType(field.TypeName));
        }

        private static FoxRunTypeShape OriginFingerprintShape(
            FoxgloveSourceEmitter.TopicMember field)
        {
            if (field?.TypeShape != null)
                return field.TypeShape;
            if (field?.Ros2CustomDtoShape != null)
                return BuildCustomDtoFingerprintShape(field.Ros2CustomDtoShape);
            if (field?.Ros2MessageShape != null)
                return BuildRos2FingerprintShape(field.Ros2MessageShape);
            return null;
        }

        private static FoxRunTypeShape BuildCustomDtoFingerprintShape(
            FoxRunRos2CustomDtoShape shape)
        {
            if (shape == null)
                return null;

            var fields = new List<FoxRunTypeField>();
            foreach (var member in shape.Members)
            {
                if (!member.CanRead)
                    continue;

                var memberShape = CustomDtoMemberFingerprintShape(member);
                if (memberShape == null)
                {
                    throw new InvalidOperationException(
                        "FoxRun origin snapshot does not support custom DTO member '"
                        + shape.FullyQualifiedTypeName
                        + "."
                        + member.Name
                        + "'.");
                }

                fields.Add(new FoxRunTypeField(
                    member.RosFieldName,
                    member.Name,
                    memberShape,
                    repeated: member.Kind == FoxRunRos2CustomDtoMemberKind.Sequence,
                    repeatedCollectionKind: member.SequenceRepresentation
                        == FoxRunRos2CustomDtoSequenceRepresentation.Array
                            ? FoxRunCollectionKind.Array
                            : FoxRunCollectionKind.List,
                    isNullable: IsNullableValueType(member.FullyQualifiedTypeName)));
                if (member.HasPresence)
                {
                    fields.Add(new FoxRunTypeField(
                        member.PresenceFieldName,
                        member.Name,
                        FoxRunTypeShape.Canonical("bool"),
                        canAssign: false,
                        isNullable: IsNullableValueType(member.FullyQualifiedTypeName)));
                }
            }

            return FoxRunTypeShape.Object(
                TrimGlobalPrefix(shape.FullyQualifiedTypeName),
                fields);
        }

        private static FoxRunTypeShape CustomDtoMemberFingerprintShape(
            FoxRunRos2CustomDtoMemberShape member)
        {
            switch (member.Kind)
            {
                case FoxRunRos2CustomDtoMemberKind.NestedDto:
                    return BuildCustomDtoFingerprintShape(member.NestedShape);
                case FoxRunRos2CustomDtoMemberKind.Enum:
                    return FoxRunTypeShape.Enum(
                        TrimGlobalPrefix(TrimNullable(member.FullyQualifiedTypeName)),
                        Array.Empty<FoxRunEnumValue>());
                case FoxRunRos2CustomDtoMemberKind.Sequence:
                    if (member.NestedShape != null)
                        return BuildCustomDtoFingerprintShape(member.NestedShape);
                    return CanonicalFingerprintShape(
                        StripRosArray(member.RosType),
                        member.SequenceElementTypeName);
                case FoxRunRos2CustomDtoMemberKind.String:
                    return FoxRunTypeShape.Canonical("string");
                default:
                    return CanonicalFingerprintShape(
                        member.RosType,
                        member.FullyQualifiedTypeName);
            }
        }

        private static FoxRunTypeShape BuildRos2FingerprintShape(
            FoxRunRos2MessageShape shape)
        {
            if (shape == null)
                return null;

            var fields = new List<FoxRunTypeField>();
            foreach (var member in shape.Members)
            {
                if (!member.CanRead)
                    continue;

                var memberShape = Ros2MemberFingerprintShape(member);
                if (memberShape == null)
                {
                    throw new InvalidOperationException(
                        "FoxRun origin snapshot does not support ROS 2 message member '"
                        + shape.FullyQualifiedTypeName
                        + "."
                        + member.Name
                        + "'.");
                }

                fields.Add(new FoxRunTypeField(
                    member.Name,
                    member.Name,
                    memberShape,
                    repeated: member.Kind == FoxRunRos2MessageMemberKind.Sequence,
                    repeatedCollectionKind: member.SequenceRepresentation
                        == FoxRunRos2SequenceRepresentation.Array
                        || member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.FixedArray
                            ? FoxRunCollectionKind.Array
                            : FoxRunCollectionKind.List));
            }

            return FoxRunTypeShape.Object(
                TrimGlobalPrefix(shape.FullyQualifiedTypeName),
                fields);
        }

        private static FoxRunTypeShape Ros2MemberFingerprintShape(
            FoxRunRos2MessageMemberShape member)
        {
            switch (member.Kind)
            {
                case FoxRunRos2MessageMemberKind.NestedMessage:
                    return BuildRos2FingerprintShape(member.NestedShape);
                case FoxRunRos2MessageMemberKind.Enum:
                    return FoxRunTypeShape.Enum(
                        TrimGlobalPrefix(member.FullyQualifiedTypeName),
                        Array.Empty<FoxRunEnumValue>());
                case FoxRunRos2MessageMemberKind.Sequence:
                    if (member.NestedShape != null)
                        return BuildRos2FingerprintShape(member.NestedShape);
                    return CanonicalFingerprintShape(
                        string.Empty,
                        member.SequenceElementTypeName);
                case FoxRunRos2MessageMemberKind.String:
                    return FoxRunTypeShape.Canonical("string");
                default:
                    return CanonicalFingerprintShape(
                        string.Empty,
                        member.FullyQualifiedTypeName);
            }
        }

        private static FoxRunTypeShape CanonicalFingerprintShape(
            string rosType,
            string managedType)
        {
            var canonical = NormalizeFingerprintCanonicalType(rosType, managedType);
            return canonical == null
                ? null
                : FoxRunTypeShape.Canonical(canonical);
        }

        private static string NormalizeFingerprintCanonicalType(
            string rosType,
            string managedType)
        {
            var ros = StripRosArray((rosType ?? string.Empty).Trim());
            switch (ros)
            {
                case "bool":
                case "boolean": return "bool";
                case "byte":
                case "char":
                case "uint8": return "uint8";
                case "int8": return "int8";
                case "int16": return "int16";
                case "uint16": return "uint16";
                case "int32": return "int32";
                case "uint32": return "uint32";
                case "int64": return "int64";
                case "uint64": return "uint64";
                case "float":
                case "float32": return "float32";
                case "double":
                case "float64": return "float64";
                case "string":
                case "wstring": return "string";
            }

            var managed = TrimNullable(TrimGlobalPrefix(managedType));
            switch (managed)
            {
                case "bool":
                case "System.Boolean": return "bool";
                case "sbyte":
                case "System.SByte": return "int8";
                case "byte":
                case "System.Byte": return "uint8";
                case "short":
                case "System.Int16": return "int16";
                case "ushort":
                case "System.UInt16": return "uint16";
                case "int":
                case "System.Int32": return "int32";
                case "uint":
                case "System.UInt32": return "uint32";
                case "long":
                case "System.Int64": return "int64";
                case "ulong":
                case "System.UInt64": return "uint64";
                case "float":
                case "System.Single": return "float32";
                case "double":
                case "System.Double": return "float64";
                case "string":
                case "System.String": return "string";
                default: return null;
            }
        }

        private static string StripRosArray(string value)
        {
            var type = value ?? string.Empty;
            var bracket = type.IndexOf('[');
            return bracket < 0 ? type : type.Substring(0, bracket);
        }

        private static string TrimGlobalPrefix(string value)
            => (value ?? string.Empty).StartsWith("global::", StringComparison.Ordinal)
                ? value.Substring("global::".Length)
                : value ?? string.Empty;

        private static string TrimNullable(string value)
        {
            var type = (value ?? string.Empty).Trim();
            if (type.EndsWith("?", StringComparison.Ordinal))
                return type.Substring(0, type.Length - 1);
            const string systemPrefix = "System.Nullable<";
            const string prefix = "Nullable<";
            if (type.StartsWith(systemPrefix, StringComparison.Ordinal)
                && type.EndsWith(">", StringComparison.Ordinal))
            {
                return type.Substring(systemPrefix.Length, type.Length - systemPrefix.Length - 1);
            }
            if (type.StartsWith(prefix, StringComparison.Ordinal)
                && type.EndsWith(">", StringComparison.Ordinal))
            {
                return type.Substring(prefix.Length, type.Length - prefix.Length - 1);
            }
            return type;
        }

        private static void EmitOptionalValue(
            StringBuilder sb,
            string canonicalType,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata,
            int number,
            string access,
            string buffer,
            string pad,
            IReadOnlyList<ObjectWriterShape> objectShapes,
            bool isNullable)
        {
            if (!isNullable)
            {
                EmitValue(
                    sb,
                    canonicalType,
                    shape,
                    metadata,
                    number,
                    access,
                    buffer,
                    pad,
                    objectShapes);
                return;
            }

            sb.AppendLine($"{pad}if ({access}.HasValue)");
            sb.AppendLine($"{pad}{{");
            EmitValue(
                sb,
                canonicalType,
                shape,
                metadata,
                number,
                access + ".Value",
                buffer,
                pad + "    ",
                objectShapes);
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitValue(
            StringBuilder sb,
            string canonicalType,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata,
            int number,
            string access,
            string buffer,
            string pad,
            IReadOnlyList<ObjectWriterShape> objectShapes)
        {
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape != null && shape.Kind == FoxRunTypeShapeKind.Object)
            {
                var index = IndexOfObjectShape(objectShapes, shape, metadata);
                if (index < 0) throw new InvalidOperationException("FoxRun Protobuf object writer shape was not registered.");
                sb.AppendLine($"{pad}__WriteFoxRunProtobufObject_{index}({buffer}, {number}, {access});");
                return;
            }
            if (shape != null && shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                sb.AppendLine($"{pad}FoxRunProtobufWire.WriteInt32({buffer}, {number}, (int){access});");
                return;
            }
            var method = WriterMethod(shape?.CanonicalType ?? canonicalType);
            if (method == null) throw new InvalidOperationException("FoxRun Protobuf emission does not support '" + canonicalType + "'.");
            sb.AppendLine($"{pad}FoxRunProtobufWire.{method}({buffer}, {number}, {access});");
        }

        private static void EmitObjectWriter(
            StringBuilder sb,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata,
            int index,
            string pad,
            IReadOnlyList<ObjectWriterShape> objectShapes)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void __WriteFoxRunProtobufObject_{index}(global::System.Collections.Generic.List<byte> __target, int __fieldNumber, global::{shape.TypeName} __value)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if ((object)__value == null) return;");
            sb.AppendLine($"{pad}        var __nested = new global::System.Collections.Generic.List<byte>(32);");
            foreach (var field in shape.Fields.OrderBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                var fieldMetadata = metadata?.Find(field.MemberName, field.JsonName);
                var presenceOnly = fieldMetadata?.PresenceOnly
                                   ?? IsOriginPresenceSentinel(shape, field);
                var fieldIdentity = shape.TypeName + "|" + field.MemberName
                                    + (presenceOnly ? "|presence" : string.Empty);
                var number = FoxRunProtobufFieldNumber.Resolve(
                    fieldIdentity,
                    fieldMetadata?.FieldNumber ?? 0);
                var access = "__value." + field.MemberName;
                if (presenceOnly)
                {
                    var presence = (fieldMetadata?.PresenceUsesHasValue
                                    ?? field.IsNullable)
                        ? access + ".HasValue"
                        : "(object)" + access + " != null";
                    sb.AppendLine($"{pad}        FoxRunProtobufWire.WriteBool(__nested, {number}, {presence});");
                    continue;
                }
                if (field.Repeated)
                {
                    var valueShape = CollectionElementOrSelf(field.TypeShape);
                    sb.AppendLine($"{pad}        if ({access} != null) foreach (var __item in {access})");
                    EmitOptionalValue(
                        sb,
                        valueShape.CanonicalType,
                        valueShape,
                        fieldMetadata?.TypeMetadata,
                        number,
                        "__item",
                        "__nested",
                        pad + "            ",
                        objectShapes,
                        field.IsNullable);
                }
                else
                {
                    var valueShape = CollectionElementOrSelf(field.TypeShape);
                    EmitOptionalValue(
                        sb,
                        valueShape.CanonicalType,
                        valueShape,
                        fieldMetadata?.TypeMetadata,
                        number,
                        access,
                        "__nested",
                        pad + "        ",
                        objectShapes,
                        field.IsNullable);
                }
            }
            sb.AppendLine($"{pad}        FoxRunProtobufWire.WriteBytes(__target, __fieldNumber, __nested);");
            sb.AppendLine($"{pad}    }}");
        }

        private static bool IsOriginPresenceSentinel(
            FoxRunTypeShape shape,
            FoxRunTypeField candidate)
            => candidate != null
               && candidate.TypeShape?.Kind == FoxRunTypeShapeKind.Canonical
               && string.Equals(
                   candidate.TypeShape.CanonicalType,
                   "bool",
                   StringComparison.Ordinal)
               && shape.Fields.Count(field =>
                   string.Equals(
                       field.MemberName,
                       candidate.MemberName,
                       StringComparison.Ordinal)) > 1;

        private static void CollectObjects(
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata,
            ICollection<ObjectWriterShape> shapes)
        {
            if (shape == null)
                return;
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                CollectObjects(shape.ElementShape, metadata, shapes);
                return;
            }
            shape = FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
            if (shape.Kind != FoxRunTypeShapeKind.Object
                || IndexOfObjectShape(shapes, shape, metadata) >= 0)
                return;
            shapes.Add(new ObjectWriterShape(shape, metadata));
            foreach (var field in shape.Fields)
            {
                var fieldMetadata = metadata?.Find(field.MemberName, field.JsonName);
                CollectObjects(
                    field.TypeShape,
                    fieldMetadata?.TypeMetadata,
                    shapes);
            }
        }

        private static FoxRunTypeShape CollectionElementOrSelf(FoxRunTypeShape shape)
            => shape != null && shape.Kind == FoxRunTypeShapeKind.Collection
                ? shape.ElementShape
                : shape;

        private static int IndexOfObjectShape(
            IEnumerable<ObjectWriterShape> shapes,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata)
        {
            var identity = ObjectWriterIdentity(shape, metadata);
            var index = 0;
            foreach (var candidate in shapes)
            {
                if (string.Equals(
                        candidate.Identity,
                        identity,
                        StringComparison.Ordinal))
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        private static string ObjectWriterIdentity(
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata)
            => FoxRunProtobufObjectShapeIdentity.Build(shape, metadata);

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
