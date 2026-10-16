// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits direct DTO/custom-ROS2-envelope mapping for Phase181 contracts.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class Ros2CustomDtoMapperEmitter
    {
        private static void EmitRosPayloadToDto(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static " + entry.DtoType + " " + entry.RosToDtoMethod + "(" + entry.RosType + " source)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        var target = new " + entry.DtoType + "();");
            foreach (var member in entry.Shape.Members)
                EmitRosToDtoMember(sb, pad + "        ", member, entry.Registry);
            sb.AppendLine(pad + "        return target;");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitRosToDtoMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            var source = "source." + RosProperty(member.RosFieldName);
            var target = "target." + IdentifierUtils.EscapeIdentifier(member.Name);
            switch (member.Kind)
            {
                case FoxRunRos2CustomDtoMemberKind.Scalar:
                    if (TryUnwrapNullable(member.FullyQualifiedTypeName, out var nullableElement))
                    {
                        var presence = "source." + RosProperty(member.PresenceFieldName);
                        sb.AppendLine(pad + target + " = " + presence + " ? new global::System.Nullable<" + CSharpType(nullableElement) + ">(" + source + ") : default(global::System.Nullable<" + CSharpType(nullableElement) + ">);");
                    }
                    else
                    {
                        sb.AppendLine(pad + target + " = " + source + ";");
                    }
                    return;
                case FoxRunRos2CustomDtoMemberKind.Enum:
                    if (TryUnwrapNullable(member.FullyQualifiedTypeName, out var nullableEnumElement))
                    {
                        var presence = "source." + RosProperty(member.PresenceFieldName);
                        var enumType = GlobalTypeName(nullableEnumElement);
                        sb.AppendLine(
                            pad + target + " = " + presence
                            + " ? new global::System.Nullable<" + enumType + ">(("
                            + enumType + ")" + source + ")"
                            + " : default(global::System.Nullable<" + enumType + ">);");
                    }
                    else
                    {
                        sb.AppendLine(pad + target + " = (" + GlobalTypeName(member.FullyQualifiedTypeName) + ")" + source + ";");
                    }
                    return;
                case FoxRunRos2CustomDtoMemberKind.String:
                    var stringPresence = member.HasPresence
                        ? "source." + RosProperty(member.PresenceFieldName)
                        : string.Empty;
                    sb.AppendLine(pad + target + " = " + (member.HasPresence ? stringPresence + " ? " + source + " : null" : source) + ";");
                    return;
                case FoxRunRos2CustomDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    var nestedPresence = member.HasPresence
                        ? "source." + RosProperty(member.PresenceFieldName)
                        : string.Empty;
                    sb.AppendLine(pad + target + " = " + (member.HasPresence ? nestedPresence + " ? " : string.Empty) + nested.RosToDtoMethod + "(" + source + ")" + (member.HasPresence ? " : null" : string.Empty) + ";");
                    return;
                case FoxRunRos2CustomDtoMemberKind.Sequence:
                    EmitSequenceRosToDto(
                        sb,
                        pad,
                        source,
                        target,
                        member.HasPresence
                            ? "source." + RosProperty(member.PresenceFieldName)
                            : string.Empty,
                        member,
                        registry);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported custom DTO member kind: " + member.Kind + ".");
            }
        }

        private static void EmitSequenceRosToDto(
            StringBuilder sb,
            string pad,
            string source,
            string target,
            string presence,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            var element = CSharpType(member.SequenceElementTypeName);
            var isList = member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List;
            var condition = member.HasPresence ? "!" + presence + " || " + source + " == null" : source + " == null";
            sb.AppendLine(pad + "if (" + condition + ")");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + target + " = null;");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "else");
            sb.AppendLine(pad + "{");
            if (isList)
            {
                sb.AppendLine(pad + "    var __values_" + member.Name + " = new global::System.Collections.Generic.List<" + element + ">(" + source + ".Length);");
                sb.AppendLine(pad + "    for (var __i = 0; __i < " + source + ".Length; __i++)");
                if (member.NestedShape != null)
                {
                    var nested = registry.Get(member.NestedShape);
                    sb.AppendLine(pad + "        __values_" + member.Name + ".Add(" + nested.RosToDtoMethod + "(" + source + "[__i]));");
                }
                else if (IsStringSequence(member))
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + ".Add(" + source + "[__i] ?? string.Empty);");
                }
                else if (IsEnumTypeName(member.SequenceElementTypeName))
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + ".Add((" + element + ")" + source + "[__i]);");
                }
                else
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + ".Add(" + source + "[__i]);");
                }
            }
            else
            {
                sb.AppendLine(pad + "    var __values_" + member.Name + " = new " + element + "[" + source + ".Length];");
                sb.AppendLine(pad + "    for (var __i = 0; __i < __values_" + member.Name + ".Length; __i++)");
                if (member.NestedShape != null)
                {
                    var nested = registry.Get(member.NestedShape);
                    sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = " + nested.RosToDtoMethod + "(" + source + "[__i]);");
                }
                else if (IsStringSequence(member))
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = " + source + "[__i] ?? string.Empty;");
                }
                else if (IsEnumTypeName(member.SequenceElementTypeName))
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = (" + element + ")" + source + "[__i];");
                }
                else
                {
                    sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = " + source + "[__i];");
                }
            }
            sb.AppendLine(pad + "    " + target + " = __values_" + member.Name + ";");
            sb.AppendLine(pad + "}");
        }

        private static void EmitDisposeRosPayload(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static void " + entry.DisposeRosMethod + "(" + entry.RosType + " value)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (value == null) return;");
            sb.AppendLine(pad + "        global::System.Exception __foxRunRos2CustomDisposeFailure = null;");
            foreach (var member in entry.Shape.Members.Where(candidate => candidate.Kind == FoxRunRos2CustomDtoMemberKind.NestedDto))
            {
                var nested = entry.Registry.Get(member.NestedShape);
                var property = "value." + RosProperty(member.RosFieldName);
                sb.AppendLine(pad + "        var nested_" + member.Name + " = " + property + ";");
                sb.AppendLine(pad + "        " + property + " = null;");
                sb.AppendLine(pad + "        try");
                sb.AppendLine(pad + "        {");
                sb.AppendLine(pad + "            " + nested.DisposeRosMethod + "(nested_" + member.Name + ");");
                sb.AppendLine(pad + "        }");
                sb.AppendLine(pad + "        catch (global::System.Exception exception)");
                sb.AppendLine(pad + "        {");
                sb.AppendLine(pad + "            if (__foxRunRos2CustomDisposeFailure == null) __foxRunRos2CustomDisposeFailure = exception;");
                sb.AppendLine(pad + "        }");
            }
            foreach (var member in entry.Shape.Members.Where(
                         candidate => candidate.Kind == FoxRunRos2CustomDtoMemberKind.Sequence
                                      && candidate.NestedShape != null))
            {
                var nested = entry.Registry.Get(member.NestedShape);
                var property = "value." + RosProperty(member.RosFieldName);
                sb.AppendLine(pad + "        var nestedSequence_" + member.Name + " = " + property + ";");
                sb.AppendLine(pad + "        " + property + " = null;");
                sb.AppendLine(pad + "        if (nestedSequence_" + member.Name + " != null)");
                sb.AppendLine(pad + "        {");
                sb.AppendLine(pad + "            for (var __i = 0; __i < nestedSequence_" + member.Name + ".Length; __i++)");
                sb.AppendLine(pad + "                try");
                sb.AppendLine(pad + "                {");
                sb.AppendLine(pad + "                    " + nested.DisposeRosMethod + "(nestedSequence_" + member.Name + "[__i]);");
                sb.AppendLine(pad + "                }");
                sb.AppendLine(pad + "                catch (global::System.Exception exception)");
                sb.AppendLine(pad + "                {");
                sb.AppendLine(pad + "                    if (__foxRunRos2CustomDisposeFailure == null) __foxRunRos2CustomDisposeFailure = exception;");
                sb.AppendLine(pad + "                }");
                sb.AppendLine(pad + "        }");
            }
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            value.Dispose();");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch (global::System.Exception exception)");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            if (__foxRunRos2CustomDisposeFailure == null) __foxRunRos2CustomDisposeFailure = exception;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        if (__foxRunRos2CustomDisposeFailure != null) throw __foxRunRos2CustomDisposeFailure;");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitRosPayloadEquals(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static bool " + entry.EqualsRosMethod + "(" + entry.RosType + " left, " + entry.RosType + " right)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(left, right)) return true;");
            sb.AppendLine(pad + "        if (left == null || right == null) return false;");
            for (var memberIndex = 0; memberIndex < entry.Shape.Members.Count; memberIndex++)
                EmitRosPayloadEqualsMember(sb, pad + "        ", entry.Shape.Members[memberIndex], memberIndex, entry.Registry);
            sb.AppendLine(pad + "        return true;");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitRosPayloadEqualsMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            int memberIndex,
            ShapeRegistry registry)
        {
            var left = "left." + RosProperty(member.RosFieldName);
            var right = "right." + RosProperty(member.RosFieldName);
            if (member.HasPresence)
            {
                var leftPresence = "left." + RosProperty(member.PresenceFieldName);
                var rightPresence = "right." + RosProperty(member.PresenceFieldName);
                sb.AppendLine(pad + "if (" + leftPresence + " != " + rightPresence + ") return false;");
                sb.AppendLine(pad + "if (" + leftPresence + ")");
                sb.AppendLine(pad + "{");
                EmitRosPayloadValueEquals(sb, pad + "    ", member, memberIndex, registry, left, right);
                sb.AppendLine(pad + "}");
                return;
            }
            EmitRosPayloadValueEquals(sb, pad, member, memberIndex, registry, left, right);
        }

        private static void EmitRosPayloadValueEquals(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            int memberIndex,
            ShapeRegistry registry,
            string left,
            string right)
        {
            if (member.Kind == FoxRunRos2CustomDtoMemberKind.NestedDto)
            {
                var nested = registry.Get(member.NestedShape);
                sb.AppendLine(pad + "if (!" + nested.EqualsRosMethod + "(" + left + ", " + right + ")) return false;");
                return;
            }
            if (member.Kind != FoxRunRos2CustomDtoMemberKind.Sequence)
            {
                var valueType = member.Kind == FoxRunRos2CustomDtoMemberKind.String
                    ? "string"
                    : RosPrimitiveType(member.RosType);
                sb.AppendLine(pad + "if (!global::System.Collections.Generic.EqualityComparer<" + valueType + ">.Default.Equals(" + left + ", " + right + ")) return false;");
                return;
            }

            var suffix = memberIndex.ToString(CultureInfo.InvariantCulture);
            var leftSequence = "__leftSequence_" + suffix;
            var rightSequence = "__rightSequence_" + suffix;
            sb.AppendLine(pad + "var " + leftSequence + " = " + left + ";");
            sb.AppendLine(pad + "var " + rightSequence + " = " + right + ";");
            sb.AppendLine(pad + "if (!global::System.Object.ReferenceEquals(" + leftSequence + ", " + rightSequence + "))");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    if (" + leftSequence + " == null || " + rightSequence + " == null) return false;");
            sb.AppendLine(pad + "    if (" + leftSequence + ".Length != " + rightSequence + ".Length) return false;");
            sb.AppendLine(pad + "    for (var __i = 0; __i < " + leftSequence + ".Length; __i++)");
            sb.AppendLine(pad + "    {");
            if (member.NestedShape != null)
            {
                var nested = registry.Get(member.NestedShape);
                sb.AppendLine(pad + "        if (!" + nested.EqualsRosMethod + "(" + leftSequence + "[__i], " + rightSequence + "[__i])) return false;");
            }
            else
            {
                var elementType = RosSequenceElementType(member, registry);
                if (IsStringSequence(member))
                {
                    sb.AppendLine(pad + "        if (!global::System.String.Equals(" + leftSequence + "[__i] ?? string.Empty, " + rightSequence + "[__i] ?? string.Empty, global::System.StringComparison.Ordinal)) return false;");
                }
                else
                {
                    sb.AppendLine(pad + "        if (!global::System.Collections.Generic.EqualityComparer<" + elementType + ">.Default.Equals(" + leftSequence + "[__i], " + rightSequence + "[__i])) return false;");
                }
            }
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
        }

    }
}
