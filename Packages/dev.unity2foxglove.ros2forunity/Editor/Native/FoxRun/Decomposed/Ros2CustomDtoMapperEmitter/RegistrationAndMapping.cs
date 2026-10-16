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
        private static bool ContainsMember(
            IReadOnlyList<IFoxRunR2fuEmitterMember> members,
            IFoxRunR2fuEmitterMember member)
        {
            if (members == null)
                return false;
            for (var index = 0; index < members.Count; index++)
            {
                if (ReferenceEquals(members[index], member))
                    return true;
            }
            return false;
        }

        private static void EmitRegistration(
            StringBuilder sb,
            string pad,
            string declaringType,
            IFoxRunR2fuEmitterMember member,
            int index)
        {
            var envelope = EnvelopeType(member);
            var canonicalEnvelope = CanonicalEnvelopeType(member);
            var id = Ros2InputDispatchEmitter.BuildContractId(
                declaringType,
                member.MemberName,
                member.Topic,
                member.Source,
                canonicalEnvelope,
                member);
            if (member.IsStream)
            {
                var access = TypeExprEmitter.MemberAccess(member.MemberName);
                sb.AppendLine(pad + "        var __foxRunRos2CustomStream_" + index + " = " + access + ";");
                sb.AppendLine(
                    pad + "        var __foxRunRos2CustomTryAdmit_" + index
                    + " = __foxRunRos2CustomStream_" + index
                    + " == null ? null : new global::System.Func<bool>(__foxRunRos2CustomStream_"
                    + index + ".TryAdmitInput);");
                sb.AppendLine(
                    pad + "        var __foxRunRos2CustomCancelAdmission_" + index
                    + " = __foxRunRos2CustomStream_" + index
                    + " == null ? null : new global::System.Func<bool>(__foxRunRos2CustomStream_"
                    + index + ".CancelAdmissionCredit);");
            }
            sb.AppendLine(
                pad + "        registrar."
                + (member.IsStream
                    ? "RegisterStream<" + envelope + ", " + envelope + ">("
                    : "Register<" + envelope + ">("));
            sb.AppendLine(pad + "            new " + NativeNamespace + "FoxRunRos2GeneratedContract(");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(id) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.Topic) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(declaringType) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.MemberName) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(canonicalEnvelope) + "\",");
            sb.AppendLine(pad + "                " + ModeLiteral(member.Mode) + ",");
            sb.AppendLine(pad + "                " + ProviderLiteral(member.Source) + ",");
            Ros2InputDispatchEmitter.AppendQosArguments(sb, pad + "                ", member);
            sb.AppendLine(pad + "                true,");
            sb.AppendLine(pad + "                " + EncodingLiteral(member.Encoding) + ",");
            sb.AppendLine(pad + "                " + NativeNamespace + "FoxRunRos2GeneratedContractKind.CustomInterface,");
            sb.AppendLine(pad + "                \"" + StaticInterfacePackageId + "\",");
            sb.AppendLine(pad + "                \"" + RosPackageName + "\",");
            sb.AppendLine(pad + "                " + TypesupportMetadataType + ".InterfaceRevision,");
            sb.AppendLine(pad + "                " + TypesupportMetadataType + ".InterfaceDigest,");
            sb.AppendLine(pad + "                " + TypesupportMetadataType + ".BaseRuntimePackageId,");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(CanonicalPayloadType(member)) + "\",");
            sb.AppendLine(pad + "                static value => value is " + envelope + " typed ? typed.Foxrun_origin_id : global::System.String.Empty,");
            sb.AppendLine(pad + "                " + PolicyLiteral(member.Policy) + ",");
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(member.Hz) + ",");
            sb.AppendLine(pad + "                " + (member.HasExplicitHz ? "true" : "false") + ",");
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(
                member.Policy == 2 && member.HasExplicitHz && member.Hz > 0f
                    ? 1f / member.Hz
                    : 0f) + "),");
            sb.AppendLine(pad + "            "
                + (member.IsStream
                    ? "__foxRunRos2CustomTryAdmit_" + index + ","
                    : "static (source, budget) => __FoxRunRos2CustomCopyEnvelope_" + index + "(source, budget),"));
            if (member.IsStream)
            {
                sb.AppendLine(pad + "            static (source, budget) => __FoxRunRos2CustomCopyEnvelope_" + index + "(source, budget),");
                sb.AppendLine(pad + "            owned =>");
                sb.AppendLine(pad + "            {");
                sb.AppendLine(pad + "                __foxRunRos2CustomStream_" + index + ".TryEnqueueDeferredOwnedAfterAdmission(");
                sb.AppendLine(pad + "                    owned,");
                sb.AppendLine(pad + "                    static value => __FoxRunRos2CustomMaterialize_" + index + "(value),");
                sb.AppendLine(pad + "                    static value => __FoxRunRos2CustomDisposeEnvelope_" + index + "(value),");
                sb.AppendLine(pad + "                    static _ => { });");
                sb.AppendLine(pad + "            },");
                sb.AppendLine(pad + "            () => __foxRunRos2CustomStream_" + index + ".Clear(),");
                sb.AppendLine(pad + "            __foxRunRos2CustomCancelAdmission_" + index + ");");
                return;
            }
            sb.AppendLine(pad + "            static owned => __FoxRunRos2CustomDisposeEnvelope_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2CustomApply_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2CustomClearIfOwned_" + index + "(owned),");
            sb.AppendLine(pad + "            static (left, right) => __FoxRunRos2CustomEqualsEnvelope_" + index + "(left, right),");
            sb.AppendLine(pad + "            " + (member.Policy == 4
                ? "() => global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_" + TriggerFieldSuffix(member) + ", 0) != 0"
                : "static () => false") + ",");
            sb.AppendLine(pad + "            " + ConditionDelegate(member) + ");");
        }

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

    }
}
