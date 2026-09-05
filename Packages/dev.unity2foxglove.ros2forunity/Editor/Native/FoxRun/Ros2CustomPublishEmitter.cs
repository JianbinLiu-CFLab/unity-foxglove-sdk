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
            IReadOnlyList<IFoxRunR2fuEmitterMember> members,
            IReadOnlyList<IFoxRunR2fuEmitterMember> mapperMembers)
        {
            if (members == null || members.Count == 0)
                return;

            var escapedNamespace = IdentifierUtils.EscapeQualifiedName(ns);
            var escapedClassName = IdentifierUtils.EscapeIdentifier(className);
            var pad = string.IsNullOrEmpty(ns) ? string.Empty : "    ";
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY && UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES");
            if (!string.IsNullOrEmpty(escapedNamespace))
            {
                sb.AppendLine("namespace " + escapedNamespace);
                sb.AppendLine("{");
            }

            sb.AppendLine(pad + "partial class " + escapedClassName + " : " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    int " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource.FoxRunRos2CustomPublisherCount => " + members.Count + ";");
            sb.AppendLine();
            sb.AppendLine(pad + "    void " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherSource.FoxRunRos2RegisterCustomPublishers(");
            sb.AppendLine(pad + "        " + Ros2CustomDtoMapperEmitter.NativeNamespace + "IFoxRunRos2CustomPublisherRegistrar registrar)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (registrar == null) throw new global::System.ArgumentNullException(nameof(registrar));");
            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                EmitRegistration(
                    sb,
                    pad,
                    declaringType,
                    member,
                    Ros2CustomDtoMapperEmitter.MapperIndexOf(mapperMembers, member));
            }
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
            if (!string.IsNullOrEmpty(escapedNamespace))
                sb.AppendLine("}");
            sb.AppendLine("#endif");
        }

        private static void EmitRegistration(
            StringBuilder sb,
            string pad,
            string declaringType,
            IFoxRunR2fuEmitterMember member,
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
                member.Source,
                canonicalEnvelope,
                member);
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
            sb.AppendLine(pad + "                " + ModeLiteral(member.Mode) + ",");
            Ros2InputDispatchEmitter.AppendQosArguments(
                sb,
                pad + "                ",
                member,
                trailingComma: true);
            sb.AppendLine(pad + "                declaredSource: " + SourceLiteral(member.Source) + ",");
            sb.AppendLine(
                pad + "                hasExplicitSource: "
                    + BoolLiteral(HasExplicit(
                        member,
                        FoxRunNamedArgumentPresence
                            .SubscribeTransportId))
                    + ",");
            sb.AppendLine(pad + "                declaredTargets: " + TargetsLiteral(member.Targets) + ",");
            sb.AppendLine(
                pad + "                hasExplicitTargets: "
                    + BoolLiteral(HasExplicit(
                        member,
                        FoxRunNamedArgumentPresence
                            .PublishTransportIds)));
            sb.AppendLine(pad + "            ),");
            sb.AppendLine(pad + "            static (source, origin, sequence, nowNs, budget) => __FoxRunRos2CustomMapDtoToEnvelope_" + index + "(source, origin, sequence, nowNs, budget),");
            sb.AppendLine(pad + "            static owned => __FoxRunRos2CustomDisposeEnvelope_" + index + "(owned));");
        }

        private static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            if (string.IsNullOrWhiteSpace(escaped))
                return "object";
            return escaped.StartsWith("global::", StringComparison.Ordinal)
                ? escaped
                : "global::" + escaped;
        }

        private static string ModeLiteral(int mode)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunFlow)"
               + mode.ToString(CultureInfo.InvariantCulture);

        private static string SourceLiteral(string source)
        {
            if (string.Equals(
                    source,
                    FoxRunR2fuGenerationConstants
                        .WebSocketProviderId,
                    StringComparison.Ordinal))
            {
                return Ros2CustomDtoMapperEmitter.NativeNamespace
                       + "FoxRunRos2RouteEndpoint.WebSocket";
            }
            if (string.Equals(
                    source,
                    FoxRunR2fuGenerationConstants.ProviderId,
                    StringComparison.Ordinal))
            {
                return Ros2CustomDtoMapperEmitter.NativeNamespace
                       + "FoxRunRos2RouteEndpoint.R2fu";
            }
            return "("
                   + Ros2CustomDtoMapperEmitter.NativeNamespace
                   + "FoxRunRos2RouteEndpoint)0";
        }

        private static string TargetsLiteral(string targets)
        {
            if (string.Equals(
                    targets,
                    FoxRunR2fuGenerationConstants.Inherit,
                    StringComparison.Ordinal))
            {
                return "("
                       + Ros2CustomDtoMapperEmitter.NativeNamespace
                       + "FoxRunRos2RouteEndpoint)0";
            }

            var literals = new List<string>();
            foreach (var target in (targets ?? string.Empty).Split(','))
            {
                if (string.Equals(
                        target,
                        FoxRunR2fuGenerationConstants
                            .WebSocketProviderId,
                        StringComparison.Ordinal))
                {
                    literals.Add(
                        Ros2CustomDtoMapperEmitter.NativeNamespace
                        + "FoxRunRos2RouteEndpoint.WebSocket");
                }
                else if (string.Equals(
                             target,
                             FoxRunR2fuGenerationConstants.ProviderId,
                             StringComparison.Ordinal))
                {
                    literals.Add(
                        Ros2CustomDtoMapperEmitter.NativeNamespace
                        + "FoxRunRos2RouteEndpoint.R2fu");
                }
            }

            return literals.Count == 0
                ? "("
                  + Ros2CustomDtoMapperEmitter.NativeNamespace
                  + "FoxRunRos2RouteEndpoint)0"
                : string.Join(" | ", literals);
        }

        private static bool HasExplicit(
            IFoxRunR2fuEmitterMember member,
            FoxRunNamedArgumentPresence argument)
            => (member.NamedArgumentPresence & argument) == argument;

        private static string BoolLiteral(bool value) => value ? "true" : "false";
    }
}
