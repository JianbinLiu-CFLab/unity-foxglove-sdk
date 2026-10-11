// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun/Decomposed/Ros2CustomCdrEmitter

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
        private static void EmitReaders(
            StringBuilder sb,
            IReadOnlyList<SubscribeBindingShape> bindings,
            string pad)
        {
            foreach (var binding in bindings)
            {
                if (binding.IsStandard)
                    continue;

                var registry = new ShapeRegistry(binding.BindingIndex);
                registry.Get(binding.Shape);
                for (var shapeIndex = 0;
                     shapeIndex < registry.Count;
                     shapeIndex++)
                {
                    EmitShapeReader(
                        sb,
                        pad,
                        registry[shapeIndex],
                        registry);
                }
            }
        }

        private static void EmitShapeReader(
            StringBuilder sb,
            string pad,
            ShapeEntry entry,
            ShapeRegistry registry)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"{pad}    private static {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)} {entry.ReadMethod}(");
            sb.AppendLine(
                $"{pad}        global::Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrReader reader)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine(
                $"{pad}        var source = new {GlobalTypeName(entry.Shape.FullyQualifiedTypeName)}();");
            var ordinal = 0;
            foreach (var member in entry.Shape.Members
                         .OrderBy(
                             value => value.RosFieldName,
                             StringComparer.Ordinal)
                         .ThenBy(value => value.Name, StringComparer.Ordinal))
            {
                EmitMemberReader(
                    sb,
                    pad + "        ",
                    member,
                    registry,
                    ordinal++);
            }
            sb.AppendLine($"{pad}        return source;");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitMemberReader(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal)
        {
            var value = "__value_" + ordinal;
            var access = "source." + EscapeIdentifier(member.Name);
            switch (member.Kind)
            {
                case BridgeDtoMemberKind.NestedDto:
                    sb.AppendLine(
                        $"{pad}var {value} = {registry.Get(member.NestedShape).ReadMethod}(reader);");
                    break;
                case BridgeDtoMemberKind.Sequence:
                    EmitSequenceReader(
                        sb,
                        pad,
                        member,
                        registry,
                        ordinal,
                        value);
                    break;
                case BridgeDtoMemberKind.String:
                    sb.AppendLine(
                        $"{pad}var {value} = reader.ReadString();");
                    break;
                case BridgeDtoMemberKind.Enum:
                    var enumType = member.FullyQualifiedTypeName;
                    if (TryUnwrapNullable(enumType, out var unwrappedEnum))
                        enumType = unwrappedEnum;
                    sb.AppendLine(
                        $"{pad}var {value} = ({GlobalTypeName(enumType)}){PrimitiveReadExpression(member.RosType)};");
                    break;
                case BridgeDtoMemberKind.Scalar:
                    sb.AppendLine(
                        $"{pad}var {value} = {PrimitiveReadExpression(member.RosType)};");
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported custom ROS 2 DTO member kind: "
                        + member.Kind
                        + ".");
            }

            if (!member.HasPresence)
            {
                sb.AppendLine($"{pad}{access} = {value};");
                return;
            }

            var hasValue = "__hasValue_" + ordinal;
            sb.AppendLine($"{pad}var {hasValue} = reader.ReadBool();");
            if (IsNullable(member.FullyQualifiedTypeName))
            {
                sb.AppendLine(
                    $"{pad}{access} = {hasValue} ? ({GlobalTypeName(member.FullyQualifiedTypeName)}){value} : default({GlobalTypeName(member.FullyQualifiedTypeName)});");
            }
            else
            {
                var absent = member.NestedShape != null
                             && member.NestedShape.IsValueType
                    ? "default("
                      + GlobalTypeName(member.FullyQualifiedTypeName)
                      + ")"
                    : "null";
                sb.AppendLine(
                    $"{pad}{access} = {hasValue} ? {value} : {absent};");
            }
        }

        private static void EmitSequenceReader(
            StringBuilder sb,
            string pad,
            BridgeDtoMemberShape member,
            ShapeRegistry registry,
            int ordinal,
            string value)
        {
            var count = "__count_" + ordinal;
            sb.AppendLine($"{pad}var {count} = reader.ReadSequenceLength();");
            sb.AppendLine(
                $"{pad}if ({count} > global::Unity2Foxglove.Ros2Bridge.FoxRunBridgeCustomDtoBudgetPolicy.MaximumSequenceItems)");
            sb.AppendLine(
                $"{pad}    throw new global::System.IO.InvalidDataException(\"Bridge CDR sequence exceeds the custom DTO item budget.\");");
            var elementType = GlobalTypeName(
                member.SequenceElementTypeName);
            if (member.SequenceRepresentation
                == BridgeSequenceRepresentation.List)
            {
                sb.AppendLine(
                    $"{pad}var {value} = new global::System.Collections.Generic.List<{elementType}>({count});");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}var {value} = new {elementType}[{count}];");
            }
            sb.AppendLine(
                $"{pad}for (var __index_{ordinal} = 0; __index_{ordinal} < {count}; __index_{ordinal}++)");
            sb.AppendLine($"{pad}{{");
            string item;
            if (member.NestedShape != null)
            {
                item = registry.Get(member.NestedShape).ReadMethod
                       + "(reader)";
            }
            else if (member.SequenceElementIsEnum)
            {
                item = "(" + elementType + ")"
                       + PrimitiveReadExpression(
                           StripArray(member.RosType));
            }
            else if (string.Equals(
                         StripArray(member.RosType),
                         "string",
                         StringComparison.Ordinal))
            {
                item = "reader.ReadString()";
            }
            else
            {
                item = PrimitiveReadExpression(
                    StripArray(member.RosType));
            }
            if (member.SequenceRepresentation
                == BridgeSequenceRepresentation.List)
            {
                sb.AppendLine($"{pad}    {value}.Add({item});");
            }
            else
            {
                sb.AppendLine(
                    $"{pad}    {value}[__index_{ordinal}] = {item};");
            }
            sb.AppendLine($"{pad}}}");
        }

        private static string PrimitiveReadExpression(string rosType)
        {
            switch (StripArray(rosType))
            {
                case "bool": return "reader.ReadBool()";
                case "int8": return "unchecked((global::System.SByte)reader.ReadUInt8())";
                case "uint8": return "reader.ReadUInt8()";
                case "int16": return "reader.ReadInt16()";
                case "uint16": return "reader.ReadUInt16()";
                case "int32": return "reader.ReadInt32()";
                case "uint32": return "reader.ReadUInt32()";
                case "int64": return "reader.ReadInt64()";
                case "uint64": return "reader.ReadUInt64()";
                case "float32": return "reader.ReadFloat32()";
                case "float64": return "reader.ReadFloat64()";
                default:
                    throw new InvalidOperationException(
                        "Unsupported custom ROS 2 CDR primitive: "
                        + rosType
                        + ".");
            }
        }

        private static string BuildStableMemberId(
            string declaringType,
            string memberKind,
            string memberName,
            string topic,
            int flow,
            string jsonFieldName)
            => (declaringType ?? string.Empty)
               + "\n"
               + (memberKind ?? string.Empty)
               + "\n"
               + (memberName ?? string.Empty)
               + "\n"
               + (topic ?? string.Empty)
               + "\n"
               + flow.ToString(CultureInfo.InvariantCulture)
               + "\n"
               + (jsonFieldName ?? string.Empty);

        private static string ReliabilityLiteral(string value)
        {
            if (string.Equals(value, "reliable", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.Reliable";
            if (string.Equals(value, "best-effort", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.BestEffort";
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryReliability.SystemDefault";
            }
            return "FoxRunDeliveryReliability.ProviderDefault";
        }

        private static string DurabilityLiteral(string value)
        {
            if (string.Equals(value, "volatile", StringComparison.Ordinal))
                return "FoxRunDeliveryDurability.Volatile";
            if (string.Equals(
                    value,
                    "transient-local",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryDurability.TransientLocal";
            }
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryDurability.SystemDefault";
            }
            return "FoxRunDeliveryDurability.ProviderDefault";
        }

        private static string HistoryLiteral(string value)
        {
            if (string.Equals(value, "keep-last", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepLast";
            if (string.Equals(value, "keep-all", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepAll";
            if (string.Equals(
                    value,
                    "system-default",
                    StringComparison.Ordinal))
            {
                return "FoxRunDeliveryHistory.SystemDefault";
            }
            return "FoxRunDeliveryHistory.ProviderDefault";
        }
    }
}
