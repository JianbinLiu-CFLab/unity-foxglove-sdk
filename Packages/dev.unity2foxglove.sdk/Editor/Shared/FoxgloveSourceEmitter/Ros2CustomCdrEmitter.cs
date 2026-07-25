// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits ROS-free XCDR1 writers for Phase181 custom FoxRun DTO envelopes.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class Ros2CustomCdrEmitter
    {
        internal static void EmitBuilders(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count != 1 || !IsSupportedCustom(fields[0]))
                    continue;

                var member = fields[0];
                var registry = new ShapeRegistry(topicIndex);
                var root = registry.Get(member.Ros2CustomDtoShape);
                var schemaContent = BuildSchemaContent(
                    member.Ros2CustomDtoShape,
                    Ros2CustomDtoMapperEmitter.RosPackageName);
                sb.AppendLine();
                sb.AppendLine($"{pad}    private const string __foxRunRos2Schema_{topicIndex} = \"{StringLiteralEmitter.CSharpStringLiteral(schemaContent)}\";");
                sb.AppendLine($"{pad}    private bool __TryBuildFoxRunRos2Cdr_{topicIndex}(ulong nowNs, out byte[] payload, out string reason)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        payload = null;");
                sb.AppendLine($"{pad}        reason = string.Empty;");
                sb.AppendLine($"{pad}        var __source = __foxRunCapture_{topicIndex}_0;");
                sb.AppendLine($"{pad}        if ((object)__source == null) {{ reason = \"Custom ROS 2 DTO is null.\"; return false; }}");
                sb.AppendLine($"{pad}        var __seconds = nowNs / 1000000000UL;");
                sb.AppendLine($"{pad}        if (__seconds > int.MaxValue) {{ reason = \"ROS 2 envelope timestamp exceeds builtin_interfaces/Time.\"; return false; }}");
                sb.AppendLine($"{pad}        if (__foxRunCaptureSequence_{topicIndex} == 0) {{ reason = \"ROS 2 envelope sequence was not captured.\"; return false; }}");
                sb.AppendLine($"{pad}        try");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            var __writer = new global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriter(");
                sb.AppendLine($"{pad}                4,");
                sb.AppendLine($"{pad}                checked((int)global::Unity.FoxgloveSDK.Components.FoxRunRos2CustomOutboundBudgetPolicy.MaximumBytes));");
                sb.AppendLine($"{pad}            __writer.WriteString(__foxRunOrigin);");
                sb.AppendLine($"{pad}            __writer.WriteUInt64(__foxRunCaptureSequence_{topicIndex});");
                sb.AppendLine($"{pad}            __writer.WriteInt32((int)__seconds);");
                sb.AppendLine($"{pad}            __writer.WriteUInt32((uint)(nowNs % 1000000000UL));");
                sb.AppendLine($"{pad}            {root.Method}(__writer, __source);");
                sb.AppendLine($"{pad}            payload = __writer.ToArray();");
                sb.AppendLine($"{pad}            return true;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        catch (global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException exception)");
                sb.AppendLine($"{pad}        {{");
                sb.AppendLine($"{pad}            reason = exception.Message;");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");

                for (var shapeIndex = 0; shapeIndex < registry.Count; shapeIndex++)
                    EmitShapeWriter(sb, pad, registry[shapeIndex], registry);
            }
        }

        private static void EmitShapeWriter(
            StringBuilder sb,
            string pad,
            ShapeEntry entry,
            ShapeRegistry registry)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void {entry.Method}(");
            sb.AppendLine($"{pad}        global::Unity.FoxgloveSDK.Schemas.Ros2Msg.Ros2CdrWriter writer,");
            sb.AppendLine($"{pad}        {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)} source)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var __hasSource = (object)source != null;");
            var ordinal = 0;
            foreach (var member in entry.Shape.Members
                         .OrderBy(value => value.RosFieldName, StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                EmitMember(sb, pad + "        ", member, registry, ordinal++);
            }
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal)
        {
            var access = "source." + IdentifierUtils.EscapeIdentifier(member.Name);
            switch (member.Kind)
            {
                case FoxRunRos2CustomDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    sb.AppendLine($"{pad}{nested.Method}(writer, __hasSource ? {access} : null);");
                    break;
                case FoxRunRos2CustomDtoMemberKind.Sequence:
                    EmitSequence(sb, pad, member, access, registry, ordinal);
                    break;
                case FoxRunRos2CustomDtoMemberKind.String:
                    sb.AppendLine($"{pad}writer.WriteString(__hasSource ? {access} : null);");
                    break;
                case FoxRunRos2CustomDtoMemberKind.Enum:
                    var enumExpression = TryUnwrapNullable(
                        member.FullyQualifiedTypeName,
                        out var nullableEnumType)
                        ? "__hasSource ? "
                          + access
                          + ".GetValueOrDefault() : default("
                          + GlobalTypeName(nullableEnumType)
                          + ")"
                        : "__hasSource ? "
                          + access
                          + " : default("
                          + GlobalTypeName(member.FullyQualifiedTypeName)
                          + ")";
                    EmitPrimitive(
                        sb,
                        pad,
                        member.RosType,
                        enumExpression);
                    break;
                case FoxRunRos2CustomDtoMemberKind.Scalar:
                    var scalar = IsNullable(member.FullyQualifiedTypeName)
                        ? "__hasSource ? " + access + ".GetValueOrDefault() : default"
                        : "__hasSource ? " + access + " : default";
                    EmitPrimitive(sb, pad, member.RosType, scalar);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported custom ROS 2 DTO member kind: " + member.Kind + ".");
            }

            if (member.HasPresence)
            {
                var presence = IsNullable(member.FullyQualifiedTypeName)
                    ? "__hasSource && " + access + ".HasValue"
                    : "__hasSource && (object)" + access + " != null";
                sb.AppendLine($"{pad}writer.WriteBool({presence});");
            }
        }

        private static void EmitSequence(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            string access,
            ShapeRegistry registry,
            int ordinal)
        {
            var variable = "__sequence_" + ordinal;
            sb.AppendLine($"{pad}var {variable} = __hasSource ? {access} : null;");
            var countExpression = member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List
                ? variable + " == null ? 0 : " + variable + ".Count"
                : variable + " == null ? 0 : " + variable + ".Length";
            var count = "__sequenceCount_" + ordinal;
            sb.AppendLine($"{pad}var {count} = {countExpression};");
            sb.AppendLine($"{pad}writer.WriteSequenceLength({count});");
            sb.AppendLine($"{pad}if ({variable} != null)");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    for (var __index = 0; __index < {count}; __index++)");
            sb.AppendLine($"{pad}    {{");
            var item = variable + "[__index]";
            if (member.NestedShape != null)
            {
                sb.AppendLine($"{pad}        {registry.Get(member.NestedShape).Method}(writer, {item});");
            }
            else if (string.Equals(StripArray(member.RosType), "string", StringComparison.Ordinal))
            {
                sb.AppendLine($"{pad}        writer.WriteString({item} ?? string.Empty);");
            }
            else
            {
                EmitPrimitive(sb, pad + "        ", StripArray(member.RosType), item);
            }
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitPrimitive(StringBuilder sb, string pad, string rosType, string expression)
        {
            switch (StripArray(rosType))
            {
                case "bool": sb.AppendLine($"{pad}writer.WriteBool((bool)({expression}));"); return;
                case "int8": sb.AppendLine($"{pad}writer.WriteUInt8(unchecked((byte)(sbyte)({expression})));"); return;
                case "uint8": sb.AppendLine($"{pad}writer.WriteUInt8((byte)({expression}));"); return;
                case "int16": sb.AppendLine($"{pad}writer.WriteInt16((short)({expression}));"); return;
                case "uint16": sb.AppendLine($"{pad}writer.WriteUInt16((ushort)({expression}));"); return;
                case "int32": sb.AppendLine($"{pad}writer.WriteInt32((int)({expression}));"); return;
                case "uint32": sb.AppendLine($"{pad}writer.WriteUInt32((uint)({expression}));"); return;
                case "int64": sb.AppendLine($"{pad}writer.WriteInt64((long)({expression}));"); return;
                case "uint64": sb.AppendLine($"{pad}writer.WriteUInt64((ulong)({expression}));"); return;
                case "float32": sb.AppendLine($"{pad}writer.WriteFloat32((float)({expression}));"); return;
                case "float64": sb.AppendLine($"{pad}writer.WriteFloat64((double)({expression}));"); return;
                default:
                    throw new InvalidOperationException("Unsupported custom ROS 2 CDR primitive: " + rosType + ".");
            }
        }

        private static bool IsSupportedCustom(FoxgloveSourceEmitter.TopicMember member)
            => member != null
               && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
               && member.Ros2CustomDtoShape != null
               && member.Ros2CustomDtoShape.IsSupported
               && member.Ros2CustomDtoShape.Diagnostics.Count == 0;

        private static string BuildSchemaContent(
            FoxRunRos2CustomDtoShape root,
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
                    var rosType = member.Kind == FoxRunRos2CustomDtoMemberKind.NestedDto
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

        private static bool IsNullable(string typeName)
        {
            var value = (typeName ?? string.Empty).Trim();
            return value.EndsWith("?", StringComparison.Ordinal)
                   || value.StartsWith("System.Nullable<", StringComparison.Ordinal)
                   || value.StartsWith("Nullable<", StringComparison.Ordinal);
        }

        private static bool TryUnwrapNullable(string typeName, out string elementType)
        {
            var value = (typeName ?? string.Empty).Trim();
            if (value.EndsWith("?", StringComparison.Ordinal))
            {
                elementType = value.Substring(0, value.Length - 1);
                return elementType.Length > 0;
            }

            const string systemPrefix = "System.Nullable<";
            const string prefix = "Nullable<";
            var matchedPrefix = value.StartsWith(systemPrefix, StringComparison.Ordinal)
                ? systemPrefix
                : value.StartsWith(prefix, StringComparison.Ordinal)
                    ? prefix
                    : null;
            if (matchedPrefix != null && value.EndsWith(">", StringComparison.Ordinal))
            {
                elementType = value.Substring(
                    matchedPrefix.Length,
                    value.Length - matchedPrefix.Length - 1);
                return elementType.Length > 0;
            }

            elementType = string.Empty;
            return false;
        }

        private static string StripArray(string rosType)
        {
            var value = rosType ?? string.Empty;
            var bracket = value.IndexOf('[', StringComparison.Ordinal);
            return bracket < 0 ? value : value.Substring(0, bracket);
        }

        private static string GlobalTypeName(string typeName)
            => string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", StringComparison.Ordinal)
                ? typeName
                : "global::" + typeName;

        private sealed class ShapeRegistry
        {
            private readonly List<ShapeEntry> _entries = new List<ShapeEntry>();
            private readonly int _topicIndex;

            internal ShapeRegistry(int topicIndex)
            {
                _topicIndex = topicIndex;
            }

            internal int Count => _entries.Count;
            internal ShapeEntry this[int index] => _entries[index];

            internal ShapeEntry Get(FoxRunRos2CustomDtoShape shape)
            {
                if (shape == null)
                    throw new InvalidOperationException("Custom ROS 2 CDR shape is missing.");
                for (var index = 0; index < _entries.Count; index++)
                {
                    if (string.Equals(
                            _entries[index].Shape.CanonicalIdentity,
                            shape.CanonicalIdentity,
                            StringComparison.Ordinal))
                    {
                        return _entries[index];
                    }
                }

                var entry = new ShapeEntry(
                    shape,
                    "__WriteFoxRunRos2CustomCdr_"
                    + _topicIndex
                    + "_"
                    + _entries.Count);
                _entries.Add(entry);
                foreach (var member in shape.Members)
                {
                    if (member.NestedShape != null)
                        Get(member.NestedShape);
                }
                return entry;
            }
        }

        private sealed class ShapeEntry
        {
            internal ShapeEntry(FoxRunRos2CustomDtoShape shape, string method)
            {
                Shape = shape;
                Method = method;
            }

            internal FoxRunRos2CustomDtoShape Shape { get; }
            internal string Method { get; }
        }
    }
}
