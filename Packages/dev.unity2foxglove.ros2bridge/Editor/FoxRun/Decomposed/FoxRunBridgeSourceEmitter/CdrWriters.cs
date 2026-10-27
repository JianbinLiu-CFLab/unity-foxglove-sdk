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
        private static void EmitBuilders(
            StringBuilder sb,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, List<FoxRunGenerationMember>> topicMap,
            string pad)
        {
            for (var topicIndex = 0; topicIndex < topics.Count; topicIndex++)
            {
                var fields = topicMap[topics[topicIndex]];
                if (fields.Count != 1 || !IsSupportedCustomPublish(fields[0]))
                    continue;

                var member = fields[0];
                var projectedShape = ProjectShape(member.TypeShape);
                var registry = new ShapeRegistry(topicIndex);
                var root = registry.Get(projectedShape);
                var schemaContent = BuildSchemaContent(
                    projectedShape,
                    RosPackageName);
                sb.AppendLine();
                sb.AppendLine($"{pad}    private const string __foxRunRos2Schema_{topicIndex} = \"{CSharpStringLiteral(schemaContent)}\";");
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
                sb.AppendLine($"{pad}            var __writer = new global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriter(");
                sb.AppendLine($"{pad}                4,");
                sb.AppendLine($"{pad}                checked((int)global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumBytes));");
                sb.AppendLine($"{pad}            __writer.WriteString(__foxRunOrigin);");
                sb.AppendLine($"{pad}            __writer.WriteUInt64(__foxRunCaptureSequence_{topicIndex});");
                sb.AppendLine($"{pad}            __writer.WriteInt32((int)__seconds);");
                sb.AppendLine($"{pad}            __writer.WriteUInt32((uint)(nowNs % 1000000000UL));");
                sb.AppendLine($"{pad}            {root.Method}(__writer, __source);");
                sb.AppendLine($"{pad}            payload = __writer.ToArray();");
                sb.AppendLine($"{pad}            return true;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}        catch (global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriterBudgetExceededException exception)");
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
            sb.AppendLine($"{pad}        global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrWriter writer,");
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
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal)
        {
            var access = "source." + EscapeIdentifier(member.Name);
            if (member.HasPresence)
            {
                var captured = "__member_" + ordinal;
                sb.AppendLine(
                    $"{pad}var {captured} = __hasSource ? {access} : default({GlobalTypeName(member.FullyQualifiedTypeName)});");
                access = captured;
            }
            switch (member.Kind)
            {
                case BridgeDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    var nestedSource = member.NestedShape.IsValueType
                        ? access
                        : "__hasSource ? " + access + " : null";
                    sb.AppendLine(
                        $"{pad}{nested.Method}(writer, {nestedSource});");
                    break;
                case BridgeDtoMemberKind.Sequence:
                    EmitSequence(sb, pad, member, access, registry, ordinal);
                    break;
                case BridgeDtoMemberKind.String:
                    sb.AppendLine($"{pad}writer.WriteString(__hasSource ? {access} : null);");
                    break;
                case BridgeDtoMemberKind.Enum:
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
                case BridgeDtoMemberKind.Scalar:
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
            BridgeDtoMemberShape member,
            string access,
            ShapeRegistry registry,
            int ordinal)
        {
            var variable = "__sequence_" + ordinal;
            sb.AppendLine($"{pad}var {variable} = __hasSource ? {access} : null;");
            var countExpression = member.SequenceRepresentation == BridgeSequenceRepresentation.List
                ? variable + " == null ? 0 : " + variable + ".Count"
                : variable + " == null ? 0 : " + variable + ".Length";
            var count = "__sequenceCount_" + ordinal;
            sb.AppendLine($"{pad}var {count} = {countExpression};");
            sb.AppendLine(
                $"{pad}global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.EnsureSequenceItems({count});");
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

    }
}
