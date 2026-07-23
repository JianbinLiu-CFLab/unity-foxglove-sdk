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
    /// <summary>
    /// Emits the optional custom-interface half of a generated FoxRun class.
    /// This is deliberately separate from <see cref="Ros2InputDispatchEmitter"/>:
    /// Phase179 packaged ROS2 messages keep their byte-for-byte established
    /// generated path, while Phase181 DTOs use only generated project message
    /// types behind the additional custom-interface compile symbol.
    /// </summary>
    internal static class Ros2CustomDtoMapperEmitter
    {
        internal const string NativeNamespace = "global::Unity2Foxglove.Ros2ForUnity.Native.";
        internal const string CustomMessageNamespace = "global::unity2foxglove_foxrun_interfaces_v1.msg.";
        internal const string StaticInterfacePackageId = "dev.unity2foxglove.foxrun.ros2.interfaces";
        internal const string RosPackageName = "unity2foxglove_foxrun_interfaces_v1";
        internal const int InterfaceRevision = 1;
        // This public, add-on-owned metadata seam keeps generated user code
        // locked to the exact selected typesupport catalog without making the
        // ROS-free SDK reference the optional add-on assembly.
        internal const string TypesupportMetadataType =
            "global::Unity2Foxglove.FoxRun.CustomRos2Typesupport.FoxRunRos2CustomTypesupportMetadata";

        internal static void EmitConditionalPartial(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
            => EmitConditionalPartial(
                sb,
                ns,
                className,
                members,
                new InputTriggerMethodRegistry(members));

        internal static void EmitConditionalPartial(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            InputTriggerMethodRegistry inputTriggerMethods)
        {
            if (members == null || members.Count == 0)
                return;

            var pad = string.IsNullOrEmpty(ns) ? string.Empty : "    ";
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES");
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("namespace " + ns);
                sb.AppendLine("{");
            }

            sb.AppendLine(pad + "partial class " + className + " : " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource");
            sb.AppendLine(pad + "{");
            for (var index = 0; index < members.Count; index++)
            {
                var envelope = EnvelopeType(members[index]);
                sb.AppendLine(pad + "    private " + envelope + " __foxRunRos2CustomAppliedOwned_" + index + ";");
                sb.AppendLine(pad + "    private object __foxRunRos2CustomAppliedDto_" + index + ";");
                if (members[index].Policy == 4)
                    sb.AppendLine(pad + "    private int __foxRunRos2Trigger_" + TriggerFieldSuffix(members[index]) + ";");
            }

            sb.AppendLine();
            sb.AppendLine(pad + "    int " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource.FoxRunRos2CustomSubscriptionCount => " + members.Count + ";");
            sb.AppendLine();
            sb.AppendLine(pad + "    void " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource.FoxRunRos2RegisterCustomSubscriptions(");
            sb.AppendLine(pad + "        " + NativeNamespace + "IFoxRunRos2SubscriptionRegistrar registrar)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (registrar == null) throw new global::System.ArgumentNullException(nameof(registrar));");
            for (var index = 0; index < members.Count; index++)
                EmitRegistration(sb, pad, declaringType, members[index], index);
            sb.AppendLine(pad + "    }");

            for (var index = 0; index < members.Count; index++)
                EmitMemberMappers(sb, pad, members[index], index, inputTriggerMethods);

            sb.AppendLine(pad + "}");
            if (!string.IsNullOrEmpty(ns))
                sb.AppendLine("}");
            sb.AppendLine("#endif");
        }

        private static void EmitRegistration(
            StringBuilder sb,
            string pad,
            string declaringType,
            FoxgloveSourceEmitter.TopicMember member,
            int index)
        {
            var envelope = EnvelopeType(member);
            var canonicalEnvelope = CanonicalEnvelopeType(member);
            var id = Ros2InputDispatchEmitter.BuildContractId(
                declaringType,
                member.MemberName,
                member.Topic,
                member.SubscriptionProvider,
                canonicalEnvelope,
                member.Ros2Qos);
            sb.AppendLine(pad + "        registrar.Register<" + envelope + ">(");
            sb.AppendLine(pad + "            new " + NativeNamespace + "FoxRunRos2GeneratedContract(");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(id) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.Topic) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(declaringType) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.MemberName) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(canonicalEnvelope) + "\",");
            sb.AppendLine(pad + "                " + ModeLiteral(member.Mode) + ",");
            sb.AppendLine(pad + "                " + ProviderLiteral(member.SubscriptionProvider) + ",");
            sb.AppendLine(pad + "                " + QosLiteral(member.Ros2Qos) + ",");
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
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(member.RateHz) + ",");
            sb.AppendLine(pad + "                " + (member.HasExplicitRateHz ? "true" : "false") + ",");
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(member.ForceIntervalSeconds < 0f ? 0f : member.ForceIntervalSeconds) + "),");
            sb.AppendLine(pad + "            static (source, budget) => __FoxRunRos2CustomCopyEnvelope_" + index + "(source, budget),");
            sb.AppendLine(pad + "            static owned => __FoxRunRos2CustomDisposeEnvelope_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2CustomApply_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2CustomClearIfOwned_" + index + "(owned),");
            sb.AppendLine(pad + "            static (left, right) => __FoxRunRos2CustomEqualsEnvelope_" + index + "(left, right),");
            sb.AppendLine(pad + "            " + (member.Policy == 4
                ? "() => global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_" + TriggerFieldSuffix(member) + ", 0) != 0"
                : "static () => false") + ");");
        }

        private static void EmitMemberMappers(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            InputTriggerMethodRegistry inputTriggerMethods)
        {
            var registry = new ShapeRegistry(index);
            var root = registry.Get(member.Ros2CustomDtoShape);
            sb.AppendLine();
            EmitMapDtoToEnvelope(sb, pad, member, index, root);
            sb.AppendLine();
            EmitCopyEnvelope(sb, pad, member, index, root);
            sb.AppendLine();
            EmitApplyAndClear(sb, pad, member, index, root, inputTriggerMethods);
            sb.AppendLine();
            EmitEnvelopeEquals(sb, pad, member, index, root);

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

        private static void EmitMapDtoToEnvelope(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
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
            sb.AppendLine(pad + "            __FoxRunRos2CustomDisposeEnvelope_" + index + "(target);");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitCopyEnvelope(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
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
            sb.AppendLine(pad + "            __FoxRunRos2CustomDisposeEnvelope_" + index + "(target);");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
            sb.AppendLine();
            sb.AppendLine(pad + "    private static void __FoxRunRos2CustomDisposeEnvelope_" + index + "(" + envelope + " value)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (value == null) return;");
            sb.AppendLine(pad + "        var payload = value.Payload;");
            sb.AppendLine(pad + "        value.Payload = null;");
            sb.AppendLine(pad + "        " + root.DisposeRosMethod + "(payload);");
            sb.AppendLine(pad + "        var stamp = value.Foxrun_stamp;");
            sb.AppendLine(pad + "        value.Foxrun_stamp = null;");
            sb.AppendLine(pad + "        if (stamp != null) stamp.Dispose();");
            sb.AppendLine(pad + "        value.Dispose();");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitApplyAndClear(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            ShapeEntry root,
            InputTriggerMethodRegistry inputTriggerMethods)
        {
            var envelope = EnvelopeType(member);
            var access = TypeExprEmitter.MemberAccess(member.MemberName);
            sb.AppendLine(pad + "    private void __FoxRunRos2CustomApply_" + index + "(" + envelope + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        var dto = " + root.RosToDtoMethod + "(owned == null ? null : owned.Payload);");
            sb.AppendLine(pad + "        " + access + " = dto;");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedOwned_" + index + " = owned;");
            sb.AppendLine(pad + "        __foxRunRos2CustomAppliedDto_" + index + " = dto;");
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

            if (member.Policy == 4
                && string.Equals(
                    member.SubscriptionProvider,
                    FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider,
                    StringComparison.Ordinal)
                && inputTriggerMethods != null
                && inputTriggerMethods.TryClaim(member, out var methodName))
            {
                sb.AppendLine();
                sb.AppendLine(pad + "    public bool " + methodName + "()");
                sb.AppendLine(pad + "    {");
                sb.AppendLine(pad + "        global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_" + TriggerFieldSuffix(member) + ", 1);");
                sb.AppendLine(pad + "        return true;");
                sb.AppendLine(pad + "    }");
            }
        }

        private static void EmitEnvelopeEquals(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
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
            sb.AppendLine(pad + "            " + entry.DisposeRosMethod + "(target);");
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
                    sb.AppendLine(pad + target + " = (" + RosPrimitiveType(member.RosType) + ")" + source + ";");
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
                    EmitSequenceDtoToRos(sb, pad, source, target, member);
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
            FoxRunRos2CustomDtoMemberShape member)
        {
            var count = member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List
                ? source + ".Count"
                : source + ".Length";
            var element = CSharpType(member.SequenceElementTypeName);
            sb.AppendLine(pad + "if (" + source + " == null)");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + target + " = global::System.Array.Empty<" + element + ">();");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "else");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    budget.RequireBytes(checked((long)" + count + " * " + ElementSizeLiteral(member.SequenceElementTypeName) + "));" );
            sb.AppendLine(pad + "    var __source_" + member.Name + " = " + source + ";");
            sb.AppendLine(pad + "    var __target_" + member.Name + " = new " + element + "[__source_" + member.Name + (member.SequenceRepresentation == FoxRunRos2CustomDtoSequenceRepresentation.List ? ".Count" : ".Length") + "];" );
            sb.AppendLine(pad + "    for (var __i = 0; __i < __target_" + member.Name + ".Length; __i++)");
            sb.AppendLine(pad + "        __target_" + member.Name + "[__i] = __source_" + member.Name + "[__i];");
            sb.AppendLine(pad + "    " + target + " = __target_" + member.Name + ";");
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
            sb.AppendLine(pad + "            " + entry.DisposeRosMethod + "(target);");
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
                    var element = CSharpType(member.SequenceElementTypeName);
                    sb.AppendLine(pad + "if (" + source + " == null)");
                    sb.AppendLine(pad + "{");
                    sb.AppendLine(pad + "    " + target + " = null;");
                    sb.AppendLine(pad + "}");
                    sb.AppendLine(pad + "else");
                    sb.AppendLine(pad + "{");
                    sb.AppendLine(pad + "    budget.RequireBytes(checked((long)" + source + ".Length * " + ElementSizeLiteral(member.SequenceElementTypeName) + "));" );
                    sb.AppendLine(pad + "    var __values_" + member.Name + " = new " + element + "[" + source + ".Length];");
                    sb.AppendLine(pad + "    global::System.Array.Copy(" + source + ", __values_" + member.Name + ", " + source + ".Length);");
                    sb.AppendLine(pad + "    " + target + " = __values_" + member.Name + ";");
                    sb.AppendLine(pad + "}");
                    return;
                default:
                    throw new InvalidOperationException("Unsupported custom DTO member kind: " + member.Kind + ".");
            }
        }

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
                    sb.AppendLine(pad + target + " = (" + GlobalTypeName(member.FullyQualifiedTypeName) + ")" + source + ";");
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
                        member);
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
            FoxRunRos2CustomDtoMemberShape member)
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
                sb.AppendLine(pad + "        __values_" + member.Name + ".Add(" + source + "[__i]);");
            }
            else
            {
                sb.AppendLine(pad + "    var __values_" + member.Name + " = new " + element + "[" + source + ".Length];");
                sb.AppendLine(pad + "    global::System.Array.Copy(" + source + ", __values_" + member.Name + ", " + source + ".Length);");
            }
            sb.AppendLine(pad + "    " + target + " = __values_" + member.Name + ";");
            sb.AppendLine(pad + "}");
        }

        private static void EmitDisposeRosPayload(StringBuilder sb, string pad, ShapeEntry entry)
        {
            sb.AppendLine(pad + "    private static void " + entry.DisposeRosMethod + "(" + entry.RosType + " value)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (value == null) return;");
            foreach (var member in entry.Shape.Members.Where(candidate => candidate.Kind == FoxRunRos2CustomDtoMemberKind.NestedDto))
            {
                var nested = entry.Registry.Get(member.NestedShape);
                var property = "value." + RosProperty(member.RosFieldName);
                sb.AppendLine(pad + "        var nested_" + member.Name + " = " + property + ";");
                sb.AppendLine(pad + "        " + property + " = null;");
                sb.AppendLine(pad + "        " + nested.DisposeRosMethod + "(nested_" + member.Name + ");");
            }
            sb.AppendLine(pad + "        value.Dispose();");
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
                var elementType = CSharpType(member.SequenceElementTypeName);
                sb.AppendLine(pad + "        if (!global::System.Collections.Generic.EqualityComparer<" + elementType + ">.Default.Equals(" + leftSequence + "[__i], " + rightSequence + "[__i])) return false;");
            }
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
        }

        internal static string EnvelopeType(FoxgloveSourceEmitter.TopicMember member)
            => CustomMessageNamespace + EnvelopeIdentity(member) + "Envelope";

        internal static string PayloadType(FoxRunRos2CustomDtoShape shape)
            => CustomMessageNamespace + RequirePayloadIdentity(shape);

        internal static string EnvelopeIdentity(FoxgloveSourceEmitter.TopicMember member)
            => RequirePayloadIdentity(member.Ros2CustomDtoShape);

        internal static string CanonicalEnvelopeType(FoxgloveSourceEmitter.TopicMember member)
            => RosPackageName + "/msg/" + EnvelopeIdentity(member) + "Envelope";

        internal static string CanonicalPayloadType(FoxgloveSourceEmitter.TopicMember member)
            => RosPackageName + "/msg/" + RequirePayloadIdentity(member.Ros2CustomDtoShape);

        private static string RequirePayloadIdentity(FoxRunRos2CustomDtoShape shape)
        {
            if (shape == null || string.IsNullOrWhiteSpace(shape.PayloadIdentity))
                throw new InvalidOperationException("Custom ROS2 DTO shape has no payload identity.");
            return shape.PayloadIdentity;
        }

        private static string ModeLiteral(int mode)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunFlow)" + mode.ToString(CultureInfo.InvariantCulture);

        private static string ProviderLiteral(string provider)
        {
            if (string.Equals(provider, FoxRunGenerationDescriptorConstants.Ros2NativeSubscriptionProvider, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider.Ros2Native";
            if (string.Equals(provider, FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSubscriptionProvider, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider.FoxgloveWebSocket";
            return "global::Unity.FoxgloveSDK.Components.FoxRunSubscriptionProvider.Inherit";
        }

        private static string QosLiteral(string qos)
        {
            if (string.Equals(qos, FoxRunGenerationDescriptorConstants.DefaultRos2Qos, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.Default";
            if (string.Equals(qos, FoxRunGenerationDescriptorConstants.ReliableRos2Qos, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.Reliable";
            if (string.Equals(qos, FoxRunGenerationDescriptorConstants.SensorDataRos2Qos, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.SensorData";
            if (string.Equals(qos, FoxRunGenerationDescriptorConstants.TransientLocalRos2Qos, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.TransientLocal";
            return "global::Unity.FoxgloveSDK.Components.FoxRunRos2QosPreset.Inherit";
        }

        private static string EncodingLiteral(string encoding)
        {
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunWireEncoding.Protobuf";
            if (string.Equals(encoding, FoxRunGenerationDescriptorConstants.JsonEncoding, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunWireEncoding.Json";
            return "global::Unity.FoxgloveSDK.Components.FoxRunWireEncoding.Inherit";
        }

        private static string PresenceExpression(string source, FoxRunRos2CustomDtoMemberShape member)
        {
            if (TryUnwrapNullable(member.FullyQualifiedTypeName, out _))
                return source + ".HasValue";
            return source + " != null";
        }

        private static bool TryUnwrapNullable(string typeName, out string elementType)
        {
            const string prefix = "System.Nullable<";
            if (!string.IsNullOrWhiteSpace(typeName)
                && typeName.StartsWith(prefix, StringComparison.Ordinal)
                && typeName.EndsWith(">", StringComparison.Ordinal))
            {
                elementType = typeName.Substring(prefix.Length, typeName.Length - prefix.Length - 1);
                return true;
            }
            elementType = string.Empty;
            return false;
        }

        private static string RosProperty(string rosFieldName)
        {
            if (string.IsNullOrEmpty(rosFieldName))
                throw new InvalidOperationException("Custom ROS2 field name must not be empty.");
            return char.ToUpperInvariant(rosFieldName[0]) + rosFieldName.Substring(1);
        }

        private static string GlobalTypeName(string typeName)
        {
            var type = CSharpType(typeName);
            return type.StartsWith("global::", StringComparison.Ordinal) || IsKeywordType(type)
                ? type
                : "global::" + type;
        }

        private static string CSharpType(string typeName)
        {
            switch (typeName)
            {
                case "System.Boolean": return "bool";
                case "System.Byte": return "byte";
                case "System.SByte": return "sbyte";
                case "System.Int16": return "short";
                case "System.UInt16": return "ushort";
                case "System.Int32": return "int";
                case "System.UInt32": return "uint";
                case "System.Int64": return "long";
                case "System.UInt64": return "ulong";
                case "System.Single": return "float";
                case "System.Double": return "double";
                case "System.String": return "string";
                default: return string.IsNullOrWhiteSpace(typeName) ? "object" : typeName;
            }
        }

        private static bool IsKeywordType(string typeName)
        {
            switch (typeName)
            {
                case "bool": case "byte": case "sbyte": case "short": case "ushort":
                case "int": case "uint": case "long": case "ulong": case "float":
                case "double": case "string": case "object": return true;
                default: return false;
            }
        }

        private static string RosPrimitiveType(string rosType)
        {
            switch (rosType)
            {
                case "bool": return "bool";
                case "int8": return "sbyte";
                case "uint8": return "byte";
                case "int16": return "short";
                case "uint16": return "ushort";
                case "int32": return "int";
                case "uint32": return "uint";
                case "int64": return "long";
                case "uint64": return "ulong";
                case "float32": return "float";
                case "float64": return "double";
                default: return CSharpType(rosType);
            }
        }

        private static string ElementSizeLiteral(string elementTypeName)
        {
            switch (CSharpType(elementTypeName))
            {
                case "bool": case "byte": case "sbyte": return "1L";
                case "short": case "ushort": return "2L";
                case "int": case "uint": case "float": return "4L";
                case "long": case "ulong": case "double": return "8L";
                default: return "8L";
            }
        }

        private sealed class ShapeRegistry
        {
            private readonly int _memberIndex;
            private readonly Dictionary<string, ShapeEntry> _byIdentity = new Dictionary<string, ShapeEntry>(StringComparer.Ordinal);
            private readonly List<ShapeEntry> _entries = new List<ShapeEntry>();

            public ShapeRegistry(int memberIndex)
            {
                _memberIndex = memberIndex;
            }

            public int Count => _entries.Count;
            public ShapeEntry this[int index] => _entries[index];

            public ShapeEntry Get(FoxRunRos2CustomDtoShape shape)
            {
                if (shape == null || string.IsNullOrWhiteSpace(shape.CanonicalIdentity))
                    throw new InvalidOperationException("Custom ROS2 DTO shape has no canonical identity.");
                if (_byIdentity.TryGetValue(shape.CanonicalIdentity, out var existing))
                    return existing;

                var suffix = _entries.Count == 0 ? string.Empty : "Nested_" + _entries.Count.ToString(CultureInfo.InvariantCulture);
                var entry = new ShapeEntry(this, shape, _memberIndex, suffix);
                _byIdentity.Add(shape.CanonicalIdentity, entry);
                _entries.Add(entry);
                foreach (var member in shape.Members)
                {
                    if (member.Kind == FoxRunRos2CustomDtoMemberKind.NestedDto)
                        Get(member.NestedShape);
                }
                return entry;
            }
        }

        private sealed class ShapeEntry
        {
            public ShapeEntry(ShapeRegistry registry, FoxRunRos2CustomDtoShape shape, int memberIndex, string suffix)
            {
                Registry = registry;
                Shape = shape;
                RosType = PayloadType(shape);
                DtoType = GlobalTypeName(shape.FullyQualifiedTypeName);
                var index = memberIndex.ToString(CultureInfo.InvariantCulture);
                var nestedPrefix = string.IsNullOrEmpty(suffix) ? string.Empty : suffix;
                DtoToRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "MapDtoToPayload_" + index;
                CopyRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "CopyPayload_" + index;
                RosToDtoMethod = "__FoxRunRos2Custom" + nestedPrefix + "MapPayloadToDto_" + index;
                DisposeRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "DisposePayload_" + index;
                EqualsRosMethod = "__FoxRunRos2Custom" + nestedPrefix + "EqualsPayload_" + index;
            }

            public ShapeRegistry Registry { get; }
            public FoxRunRos2CustomDtoShape Shape { get; }
            public string RosType { get; }
            public string DtoType { get; }
            public string DtoToRosMethod { get; }
            public string CopyRosMethod { get; }
            public string RosToDtoMethod { get; }
            public string DisposeRosMethod { get; }
            public string EqualsRosMethod { get; }
        }

        private static string PolicyLiteral(int policy)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunPolicy)"
               + policy.ToString(CultureInfo.InvariantCulture);

        private static string TriggerFieldSuffix(FoxgloveSourceEmitter.TopicMember member)
            => IdentifierUtils.SanitizeIdentifier(member.MemberName.TrimStart('_'))
               + "_"
               + TopicMetadataEmitter.Sha256Hex(
                       (member.MemberName ?? string.Empty) + "|" + (member.Topic ?? string.Empty))
                   .Substring(0, 8);
    }
}
