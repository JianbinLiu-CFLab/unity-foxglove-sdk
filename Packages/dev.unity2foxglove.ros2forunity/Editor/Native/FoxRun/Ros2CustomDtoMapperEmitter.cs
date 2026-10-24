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
    /// <remarks>Phase192 source sections: Ros2CustomDtoMapperEmitter.Mapping.cs, Ros2CustomDtoMapperEmitter.RosPayload.cs, Ros2CustomDtoMapperEmitter.Equality.cs, Ros2CustomDtoMapperEmitter.Utilities.cs</remarks>
    internal static partial class Ros2CustomDtoMapperEmitter
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
            IReadOnlyList<IFoxRunR2fuEmitterMember> mapperMembers,
            IReadOnlyList<IFoxRunR2fuEmitterMember> subscriptionMembers,
            IReadOnlyList<string> publishTopics)
        {
            if (mapperMembers == null || mapperMembers.Count == 0)
                return;

            var escapedNamespace = IdentifierUtils.EscapeQualifiedName(ns);
            var escapedClassName = IdentifierUtils.EscapeIdentifier(className);
            var pad = string.IsNullOrEmpty(ns) ? string.Empty : "    ";
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES && (UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN)");
            if (!string.IsNullOrEmpty(escapedNamespace))
            {
                sb.AppendLine("namespace " + escapedNamespace);
                sb.AppendLine("{");
            }

            sb.AppendLine(
                pad + "partial class " + escapedClassName
                + (subscriptionMembers != null && subscriptionMembers.Count > 0
                    ? " : " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource"
                    : string.Empty));
            sb.AppendLine(pad + "{");
            for (var index = 0; index < mapperMembers.Count; index++)
            {
                if (!ContainsMember(subscriptionMembers, mapperMembers[index]))
                    continue;
                if (mapperMembers[index].IsStream)
                    continue;
                var envelope = EnvelopeType(mapperMembers[index]);
                sb.AppendLine(pad + "    private " + envelope + " __foxRunRos2CustomAppliedOwned_" + index + ";");
                sb.AppendLine(pad + "    private object __foxRunRos2CustomAppliedDto_" + index + ";");
                if (mapperMembers[index].Policy == 4)
                    sb.AppendLine(pad + "    private int __foxRunRos2Trigger_" + TriggerFieldSuffix(mapperMembers[index]) + ";");
            }

            if (subscriptionMembers != null && subscriptionMembers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(pad + "    int " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource.FoxRunRos2CustomSubscriptionCount => " + subscriptionMembers.Count + ";");
                sb.AppendLine();
                sb.AppendLine(pad + "    void " + NativeNamespace + "IFoxRunRos2CustomSubscriptionSource.FoxRunRos2RegisterCustomSubscriptions(");
                sb.AppendLine(pad + "        " + NativeNamespace + "IFoxRunRos2SubscriptionRegistrar registrar)");
                sb.AppendLine(pad + "    {");
                sb.AppendLine(pad + "        if (registrar == null) throw new global::System.ArgumentNullException(nameof(registrar));");
                for (var index = 0; index < subscriptionMembers.Count; index++)
                {
                    var member = subscriptionMembers[index];
                    EmitRegistration(
                        sb,
                        pad,
                        declaringType,
                        member,
                        MapperIndexOf(mapperMembers, member));
                }
                sb.AppendLine(pad + "    }");
            }

            for (var index = 0; index < mapperMembers.Count; index++)
            {
                var member = mapperMembers[index];
                EmitMemberMappers(
                    sb,
                    pad,
                    member,
                    index,
                    publishTopics,
                    ContainsMember(subscriptionMembers, member));
            }

            sb.AppendLine(pad + "}");
            if (!string.IsNullOrEmpty(escapedNamespace))
                sb.AppendLine("}");
            sb.AppendLine("#endif");
        }

        internal static int MapperIndexOf(
            IReadOnlyList<IFoxRunR2fuEmitterMember> mapperMembers,
            IFoxRunR2fuEmitterMember member)
        {
            if (mapperMembers == null)
                throw new ArgumentNullException(nameof(mapperMembers));
            for (var index = 0; index < mapperMembers.Count; index++)
            {
                if (ReferenceEquals(mapperMembers[index], member))
                    return index;
            }
            throw new InvalidOperationException(
                "Custom ROS2 member is missing from the shared mapper registry.");
        }

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

    }
}
