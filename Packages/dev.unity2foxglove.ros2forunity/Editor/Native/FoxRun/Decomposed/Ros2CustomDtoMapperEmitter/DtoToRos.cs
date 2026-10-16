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
        private static string ConditionDelegate(IFoxRunR2fuEmitterMember member)
        {
            return string.IsNullOrWhiteSpace(member.OnlyIf)
                ? "null"
                : "() => " + ConditionEmitter.ConditionAccess(
                    member.OnlyIf,
                    member.ConditionMemberKind);
        }

        private static void EmitEnvelopeEquals(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            ShapeEntry root)
        {
            var envelope = EnvelopeType(member);
            sb.AppendLine(pad + "    private static bool __FoxRunRos2CustomEqualsEnvelope_" + index + "(" + envelope + " left, " + envelope + " right)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(left, right)) return true;");
            sb.AppendLine(pad + "        if (left == null || right == null) return false;");
            sb.AppendLine(pad + "        return " + root.EqualsRosMethod + "(left.Payload, right.Payload);");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitDtoToRosPayload(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static " + entry.RosType + " " + entry.DtoToRosMethod + "(");
            sb.AppendLine(pad + "        " + entry.DtoType + " source,");
            sb.AppendLine(pad + "        " + NativeNamespace + "FoxRunRos2CustomOutboundMappingContext budget)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        var target = new " + entry.RosType + "();");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            foreach (var member in entry.Shape.Members)
                EmitDtoToRosMember(sb, pad + "            ", member, entry.Registry);
            sb.AppendLine(pad + "            return target;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            try");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                " + entry.DisposeRosMethod + "(target);");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            catch (global::System.Exception)");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                // Preserve the mapping failure as the primary exception.");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitDtoToRosMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            var source = "source." + IdentifierUtils.EscapeIdentifier(member.Name);
            var target = "target." + RosProperty(member.RosFieldName);
            if (member.HasPresence)
                sb.AppendLine(
                    pad
                    + "target."
                    + RosProperty(member.PresenceFieldName)
                    + " = "
                    + PresenceExpression(source, member)
                    + ";");

            switch (member.Kind)
            {
                case FoxRunRos2CustomDtoMemberKind.Scalar:
                    EmitScalarDtoToRos(sb, pad, source, target, member);
                    return;
                case FoxRunRos2CustomDtoMemberKind.Enum:
                    var enumSource = TryUnwrapNullable(member.FullyQualifiedTypeName, out _)
                        ? source + ".GetValueOrDefault()"
                        : source;
                    sb.AppendLine(pad + target + " = (" + RosPrimitiveType(member.RosType) + ")" + enumSource + ";");
                    return;
                case FoxRunRos2CustomDtoMemberKind.String:
                    sb.AppendLine(pad + "if (" + source + " != null) budget.RequireBytes(checked((long)" + source + ".Length * 2L));");
                    sb.AppendLine(pad + target + " = " + source + " ?? string.Empty;");
                    return;
                case FoxRunRos2CustomDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    // ros2cs writes every nested field through the generated
                    // managed wrapper even when this project's presence bit is
                    // false. Preserve null at the DTO/wire-contract level via
                    // foxrun_has_* while retaining a default wrapper that can
                    // be serialized safely by the native message writer.
                    sb.AppendLine(pad + target + " = " + source + " == null ? new " + nested.RosType + "() : " + nested.DtoToRosMethod + "(" + source + ", budget);");
                    return;
                case FoxRunRos2CustomDtoMemberKind.Sequence:
                    EmitSequenceDtoToRos(sb, pad, source, target, member, registry);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported custom DTO member kind: " + member.Kind + ".");
            }
        }

        private static void EmitScalarDtoToRos(
            StringBuilder sb,
            string pad,
            string source,
            string target,
            FoxRunRos2CustomDtoMemberShape member)
        {
            if (TryUnwrapNullable(member.FullyQualifiedTypeName, out _))
                sb.AppendLine(pad + target + " = " + source + ".GetValueOrDefault();");
            else
                sb.AppendLine(pad + target + " = " + source + ";");
        }

        private static void EmitSequenceDtoToRos(
            StringBuilder sb,
            string pad,
            string source,
            string target,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            var count = member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List
                ? source + ".Count"
                : source + ".Length";
            var element = RosSequenceElementType(member, registry);
            var sourceItem = "__source_" + member.Name + "[__i]";
            var targetItem = "__target_" + member.Name + "[__i]";
            sb.AppendLine(pad + "if (" + source + " == null)");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + target + " = global::System.Array.Empty<" + element + ">();");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "else");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    budget.RequireBytes(checked((long)" + count + " * " + ElementSizeLiteral(member.SequenceElementTypeName) + "));" );
            sb.AppendLine(pad + "    var __source_" + member.Name + " = " + source + ";");
            sb.AppendLine(pad + "    var __target_" + member.Name + " = new " + element + "[__source_" + member.Name + (member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List ? ".Count" : ".Length") + "];" );
            // Publish the owned array into the parent before filling it so the
            // parent catch/dispose path owns every successfully mapped item
            // when a later recursive mapping fails.
            sb.AppendLine(pad + "    " + target + " = __target_" + member.Name + ";");
            sb.AppendLine(pad + "    for (var __i = 0; __i < __target_" + member.Name + ".Length; __i++)");
            sb.AppendLine(pad + "    {");
            if (member.NestedShape != null)
            {
                var nested = registry.Get(member.NestedShape);
                sb.AppendLine(pad + "        " + targetItem + " = " + sourceItem + " == null ? new " + nested.RosType + "() : " + nested.DtoToRosMethod + "(" + sourceItem + ", budget);");
            }
            else if (IsStringSequence(member))
            {
                sb.AppendLine(pad + "        var __item_" + member.Name + " = " + sourceItem + " ?? string.Empty;");
                sb.AppendLine(pad + "        budget.RequireBytes(checked((long)__item_" + member.Name + ".Length * 2L));");
                sb.AppendLine(pad + "        " + targetItem + " = __item_" + member.Name + ";");
            }
            else if (IsEnumTypeName(member.SequenceElementTypeName))
            {
                sb.AppendLine(pad + "        " + targetItem + " = (" + element + ")" + sourceItem + ";");
            }
            else
            {
                sb.AppendLine(pad + "        " + targetItem + " = " + sourceItem + ";");
            }
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
        }

        private static void EmitCopyRosPayload(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static " + entry.RosType + " " + entry.CopyRosMethod + "(");
            sb.AppendLine(pad + "        " + entry.RosType + " source,");
            sb.AppendLine(pad + "        " + NativeNamespace + "FoxRunRos2CopyContext budget)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        var target = new " + entry.RosType + "();");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            foreach (var member in entry.Shape.Members)
                EmitCopyRosMember(sb, pad + "            ", member, entry.Registry);
            sb.AppendLine(pad + "            return target;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            try");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                " + entry.DisposeRosMethod + "(target);");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            catch (global::System.Exception)");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                // Preserve the copy failure as the primary exception.");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitCopyRosMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2CustomDtoMemberShape member,
            ShapeRegistry registry)
        {
            var source = "source." + RosProperty(member.RosFieldName);
            var target = "target." + RosProperty(member.RosFieldName);
            if (member.HasPresence)
                sb.AppendLine(pad + "target." + RosProperty(member.PresenceFieldName) + " = source." + RosProperty(member.PresenceFieldName) + ";");

            switch (member.Kind)
            {
                case FoxRunRos2CustomDtoMemberKind.Scalar:
                case FoxRunRos2CustomDtoMemberKind.Enum:
                    sb.AppendLine(pad + target + " = " + source + ";");
                    return;
                case FoxRunRos2CustomDtoMemberKind.String:
                    sb.AppendLine(pad + "if (" + source + " != null) budget.RequireBytes(checked((long)" + source + ".Length * 2L));");
                    sb.AppendLine(pad + target + " = " + source + ";");
                    return;
                case FoxRunRos2CustomDtoMemberKind.NestedDto:
                    var nested = registry.Get(member.NestedShape);
                    sb.AppendLine(pad + target + " = " + nested.CopyRosMethod + "(" + source + ", budget);");
                    return;
                case FoxRunRos2CustomDtoMemberKind.Sequence:
                    var element = RosSequenceElementType(member, registry);
                    sb.AppendLine(pad + "if (" + source + " == null)");
                    sb.AppendLine(pad + "{");
                    sb.AppendLine(pad + "    " + target + " = null;");
                    sb.AppendLine(pad + "}");
                    sb.AppendLine(pad + "else");
                    sb.AppendLine(pad + "{");
                    sb.AppendLine(pad + "    budget.RequireBytes(checked((long)" + source + ".Length * " + ElementSizeLiteral(member.SequenceElementTypeName) + "));" );
                    sb.AppendLine(pad + "    var __values_" + member.Name + " = new " + element + "[" + source + ".Length];");
                    sb.AppendLine(pad + "    " + target + " = __values_" + member.Name + ";");
                    sb.AppendLine(pad + "    for (var __i = 0; __i < __values_" + member.Name + ".Length; __i++)");
                    sb.AppendLine(pad + "    {");
                    if (member.NestedShape != null)
                    {
                        var nestedSequence = registry.Get(member.NestedShape);
                        sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = " + source + "[__i] == null ? new " + nestedSequence.RosType + "() : " + nestedSequence.CopyRosMethod + "(" + source + "[__i], budget);");
                    }
                    else if (IsStringSequence(member))
                    {
                        sb.AppendLine(pad + "        var __item_" + member.Name + " = " + source + "[__i] ?? string.Empty;");
                        sb.AppendLine(pad + "        budget.RequireBytes(checked((long)__item_" + member.Name + ".Length * 2L));");
                        sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = __item_" + member.Name + ";");
                    }
                    else
                    {
                        sb.AppendLine(pad + "        __values_" + member.Name + "[__i] = " + source + "[__i];");
                    }
                    sb.AppendLine(pad + "    }");
                    sb.AppendLine(pad + "}");
                    return;
                default:
                    throw new InvalidOperationException("Unsupported custom DTO member kind: " + member.Kind + ".");
            }
        }

    }
}
