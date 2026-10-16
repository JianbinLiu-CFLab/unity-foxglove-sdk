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
    /// <remarks>Phase192 source sections: RegistrationAndMapping.cs, DtoToRos.cs, RosToDto.cs, TypeHelpers.cs</remarks>
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
    }
}
