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
        private static void EmitMemberMappers(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            IReadOnlyList<string> publishTopics,
            bool emitSubscriptionMappers)
        {
            var registry = new ShapeRegistry(index);
            var root = registry.Get(member.Ros2CustomDtoShape);
            sb.AppendLine();
            EmitMapDtoToEnvelope(sb, pad, member, index, root);
            sb.AppendLine();
            EmitCopyEnvelope(sb, pad, member, index, root);
            if (emitSubscriptionMappers)
            {
                if (member.IsStream)
                {
                    sb.AppendLine();
                    EmitStreamMaterializer(sb, pad, member, index, root);
                }
                else
                {
                    sb.AppendLine();
                    EmitApplyAndClear(sb, pad, member, index, root, publishTopics);
                    sb.AppendLine();
                    EmitEnvelopeEquals(sb, pad, member, index, root);
                }
            }

            for (var shapeIndex = 0; shapeIndex < registry.Count; shapeIndex++)
            {
                var entry = registry[shapeIndex];
                sb.AppendLine();
                EmitDtoToRosPayload(sb, pad, entry);
                sb.AppendLine();
                EmitCopyRosPayload(sb, pad, entry);
                sb.AppendLine();
                EmitRosPayloadToDto(sb, pad, entry);
                sb.AppendLine();
                EmitDisposeRosPayload(sb, pad, entry);
                sb.AppendLine();
                EmitRosPayloadEquals(sb, pad, entry);
            }
        }

        private static void EmitStreamMaterializer(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            ShapeEntry root)
        {
            var envelope = EnvelopeType(member);
            var dtoType = GlobalTypeName(member.TypeName);
            sb.AppendLine(pad + "    private static " + dtoType + " __FoxRunRos2CustomMaterialize_" + index + "(");
            sb.AppendLine(pad + "        " + envelope + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        return " + root.RosToDtoMethod + "(owned == null ? null : owned.Payload);");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitMapDtoToEnvelope(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            ShapeEntry root)
        {
            var dtoType = GlobalTypeName(member.TypeName);
            var envelope = EnvelopeType(member);
            sb.AppendLine(pad + "    private static " + envelope + " __FoxRunRos2CustomMapDtoToEnvelope_" + index + "(");
            sb.AppendLine(pad + "        " + dtoType + " source,");
            sb.AppendLine(pad + "        string origin,");
            sb.AppendLine(pad + "        ulong sequence,");
            sb.AppendLine(pad + "        ulong nowNs,");
            sb.AppendLine(pad + "        " + NativeNamespace + "FoxRunRos2CustomOutboundMappingContext budget)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        if (budget == null) throw new global::System.ArgumentNullException(nameof(budget));");
            sb.AppendLine(pad + "        if (!" + NativeNamespace + "FoxRunRos2CustomEnvelopeTimestamp.TryFromUnixNanoseconds(nowNs, out var stamp))");
            sb.AppendLine(pad + "            throw new global::System.InvalidOperationException(\"FoxRun custom ROS2 envelope timestamp is out of range.\");");
            sb.AppendLine(pad + "        var target = new " + envelope + "();");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            target.Foxrun_origin_id = origin ?? string.Empty;");
            sb.AppendLine(pad + "            target.Foxrun_sequence = sequence;");
            sb.AppendLine(pad + "            target.Foxrun_stamp = new global::builtin_interfaces.msg.Time");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                Sec = stamp.Seconds,");
            sb.AppendLine(pad + "                Nanosec = stamp.Nanoseconds,");
            sb.AppendLine(pad + "            };");
            sb.AppendLine(pad + "            target.Payload = " + root.DtoToRosMethod + "(source, budget);");
            sb.AppendLine(pad + "            return target;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            try");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                __FoxRunRos2CustomDisposeEnvelope_" + index + "(target);");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            catch (global::System.Exception)");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                // Preserve the mapping failure as the primary exception.");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitCopyEnvelope(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            ShapeEntry root)
        {
            var envelope = EnvelopeType(member);
            sb.AppendLine(pad + "    private static " + envelope + " __FoxRunRos2CustomCopyEnvelope_" + index + "(");
            sb.AppendLine(pad + "        " + envelope + " source,");
            sb.AppendLine(pad + "        " + NativeNamespace + "FoxRunRos2CopyContext budget)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        if (budget == null) throw new global::System.ArgumentNullException(nameof(budget));");
            sb.AppendLine(pad + "        var target = new " + envelope + "();");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            var origin = source.Foxrun_origin_id;");
            sb.AppendLine(pad + "            if (origin != null) budget.RequireBytes(checked((long)origin.Length * 2L));");
            sb.AppendLine(pad + "            target.Foxrun_origin_id = origin;");
            sb.AppendLine(pad + "            target.Foxrun_sequence = source.Foxrun_sequence;");
            sb.AppendLine(pad + "            var stamp = source.Foxrun_stamp;");
            sb.AppendLine(pad + "            if (stamp != null)");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                target.Foxrun_stamp = new global::builtin_interfaces.msg.Time");
            sb.AppendLine(pad + "                {");
            sb.AppendLine(pad + "                    Sec = stamp.Sec,");
            sb.AppendLine(pad + "                    Nanosec = stamp.Nanosec,");
            sb.AppendLine(pad + "                };");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            target.Payload = " + root.CopyRosMethod + "(source.Payload, budget);");
            sb.AppendLine(pad + "            return target;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            try");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                __FoxRunRos2CustomDisposeEnvelope_" + index + "(target);");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            catch (global::System.Exception)");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                // Preserve the copy failure as the primary exception.");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
            sb.AppendLine();
            sb.AppendLine(pad + "    private static void __FoxRunRos2CustomDisposeEnvelope_" + index + "(" + envelope + " value)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (value == null) return;");
            sb.AppendLine(pad + "        global::System.Exception __foxRunRos2CustomDisposeFailure = null;");
            sb.AppendLine(pad + "        var payload = value.Payload;");
            sb.AppendLine(pad + "        value.Payload = null;");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            " + root.DisposeRosMethod + "(payload);");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch (global::System.Exception exception)");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            __foxRunRos2CustomDisposeFailure = exception;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        var stamp = value.Foxrun_stamp;");
            sb.AppendLine(pad + "        value.Foxrun_stamp = null;");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            if (stamp != null) stamp.Dispose();");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch (global::System.Exception exception)");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            if (__foxRunRos2CustomDisposeFailure == null) __foxRunRos2CustomDisposeFailure = exception;");
            sb.AppendLine(pad + "        }");
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

        private static void EmitApplyAndClear(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            int index,
            ShapeEntry root,
            IReadOnlyList<string> publishTopics)
        {
            var envelope = EnvelopeType(member);
            var access = TypeExprEmitter.MemberAccess(member.MemberName);
            sb.AppendLine(pad + "    private void __FoxRunRos2CustomApply_" + index + "(" + envelope + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        var dto = " + root.RosToDtoMethod + "(owned == null ? null : owned.Payload);");
            sb.AppendLine(pad + "        " + access + " = dto;");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedOwned_" + index + " = owned;");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedDto_" + index + " = dto;");
            EmitRemoteOriginMark(sb, pad + "        ", member, publishTopics);
            sb.AppendLine(pad + "    }");
            sb.AppendLine();
            sb.AppendLine(pad + "    private bool __FoxRunRos2CustomClearIfOwned_" + index + "(" + envelope + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (!global::System.Object.ReferenceEquals(__foxRunRos2CustomAppliedOwned_" + index + ", owned))");
            sb.AppendLine(pad + "            return false;");
            sb.AppendLine(pad + "        var cleared = global::System.Object.ReferenceEquals((object)" + access + ", __foxRunRos2CustomAppliedDto_" + index + ");");
            sb.AppendLine(pad + "        if (cleared)");
            sb.AppendLine(pad + "            " + access + " = default(" + GlobalTypeName(member.TypeName) + ");");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedOwned_" + index + " = null;");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedDto_" + index + " = null;");
            sb.AppendLine(pad + "        return cleared;");
            sb.AppendLine(pad + "    }");

        }

        private static void EmitRemoteOriginMark(
            StringBuilder sb,
            string pad,
            IFoxRunR2fuEmitterMember member,
            IReadOnlyList<string> publishTopics)
        {
            if (member.Mode != 3 || publishTopics == null)
                return;

            for (var index = 0; index < publishTopics.Count; index++)
            {
                if (!string.Equals(publishTopics[index], member.Topic, StringComparison.Ordinal))
                    continue;

                sb.AppendLine(pad + "__FoxRunMarkRemoteApplied_" + index + "();");
                return;
            }
        }

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

    }
}
