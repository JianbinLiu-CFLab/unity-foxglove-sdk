// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Native/FoxRun
// Purpose: Phase192 partial source section for Ros2CustomDtoMapperEmitter.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class Ros2CustomDtoMapperEmitter
    {
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
