// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits closed custom-ROS2 publisher registration for Phase181 DTO contracts.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Emits the generated publisher-source seam.  The optional runtime owns
    /// the topic-bus subscription and native endpoint; generated code exposes
    /// only closed DTO/envelope mappers and immutable contract metadata.
    /// </summary>
    internal static class Ros2CustomPublishEmitter
    {
        internal static void EmitConditionalPartial(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
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

            sb.AppendLine(pad + "partial class " + className + " : " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    int " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource.FoxRunRos2CustomPublisherCount => " + members.Count + ";");
            sb.AppendLine();
            sb.AppendLine(pad + "    void " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource.FoxRunRos2RegisterCustomPublishers(");
            sb.AppendLine(pad + "        " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherRegistrar registrar)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (registrar == null) throw new global::System.ArgumentNullException(nameof(registrar));");
            for (var index = 0; index < members.Count; index++)
                EmitRegistration(sb, pad, declaringType, members[index], index);
            sb.AppendLine(pad + "    }");
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
            var dto = GlobalTypeName(member.TypeName);
            var envelope = Ros2CustomDtoMapperEmitter.EnvelopeType(member);
            var canonicalEnvelope = Ros2CustomDtoMapperEmitter.CanonicalEnvelopeType(member);
            var canonicalPayload = Ros2CustomDtoMapperEmitter.CanonicalPayloadType(member);
            var id = Ros2InputDispatchEmitter.BuildContractId(
                declaringType,
                member.MemberName,
                member.Topic,
                member.SubscriptionProvider,
                canonicalEnvelope,
                member.Ros2Qos);
            sb.AppendLine(pad + "        registrar.Register<" + dto + ", " + envelope + ">(");
            sb.AppendLine(pad + "            new " + Ros2CustomDtoMapperEmitter.NativeNamespace + "FoxRunRos2CustomPublisherContract(");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(id) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.Topic) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(declaringType) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.MemberName) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(canonicalPayload) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(canonicalEnvelope) + "\",");
            sb.AppendLine(pad + "                \"" + Ros2CustomDtoMapperEmitter.StaticInterfacePackageId + "\",");
            sb.AppendLine(pad + "                \"" + Ros2CustomDtoMapperEmitter.RosPackageName + "\",");
            sb.AppendLine(pad + "                " + Ros2CustomDtoMapperEmitter.TypesupportMetadataType + ".InterfaceRevision,");
            sb.AppendLine(pad + "                " + Ros2CustomDtoMapperEmitter.TypesupportMetadataType + ".InterfaceDigest,");
            sb.AppendLine(pad + "                " + Ros2CustomDtoMapperEmitter.TypesupportMetadataType + ".BaseRuntimePackageId,");
            sb.AppendLine(pad + "                " + ModeLiteral(member.Mode) + "),");
            sb.AppendLine(pad + "            static (source, origin, sequence, nowNs, budget) => __FoxRunRos2CustomMapDtoToEnvelope_" + index + "(source, origin, sequence, nowNs, budget),");
            sb.AppendLine(pad + "            static owned => __FoxRunRos2CustomDisposeEnvelope_" + index + "(owned));");
        }

        private static string GlobalTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return "object";
            return typeName.StartsWith("global::", StringComparison.Ordinal)
                ? typeName
                : "global::" + typeName;
        }

        private static string ModeLiteral(int mode)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunFlow)"
               + mode.ToString(CultureInfo.InvariantCulture);
    }
}
