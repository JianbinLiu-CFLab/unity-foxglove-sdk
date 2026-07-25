// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Emits closed native ROS2 registration, owned deep copy/dispose, and main-thread apply.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class Ros2InputDispatchEmitter
    {
        private const string NativeNamespace = "global::Unity2Foxglove.Ros2ForUnity.Native.";

        internal static void EmitConditionalPartial(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> publishTopics)
        {
            if (members == null || members.Count == 0)
                return;

            var pad = string.IsNullOrEmpty(ns) ? string.Empty : "    ";
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY");
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine("namespace " + ns);
                sb.AppendLine("{");
            }

            sb.AppendLine(pad + "partial class " + className + " : " + NativeNamespace + "IFoxRunRos2SubscriptionSource");
            sb.AppendLine(pad + "{");
            for (var i = 0; i < members.Count; i++)
            {
                var typeName = GlobalTypeName(members[i].Ros2MessageShape.FullyQualifiedTypeName);
                sb.AppendLine(pad + "    private " + typeName + " __foxRunRos2AppliedOwned_" + i + ";");
                if (members[i].Policy == 4)
                    sb.AppendLine(pad + "    private int __foxRunRos2Trigger_" + TriggerFieldSuffix(members[i]) + ";");
            }

            sb.AppendLine();
            sb.AppendLine(pad + "    int " + NativeNamespace + "IFoxRunRos2SubscriptionSource.FoxRunRos2SubscriptionCount => " + members.Count + ";");
            sb.AppendLine();
            sb.AppendLine(pad + "    void " + NativeNamespace + "IFoxRunRos2SubscriptionSource.FoxRunRos2RegisterSubscriptions(");
            sb.AppendLine(pad + "        " + NativeNamespace + "IFoxRunRos2SubscriptionRegistrar registrar)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (registrar == null) throw new global::System.ArgumentNullException(nameof(registrar));");
            for (var i = 0; i < members.Count; i++)
                EmitRegistration(sb, pad, declaringType, members[i], i);
            sb.AppendLine(pad + "    }");

            for (var i = 0; i < members.Count; i++)
                EmitBindingMethods(sb, pad, members[i], i, publishTopics);

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
            var shape = member.Ros2MessageShape;
            var typeName = GlobalTypeName(shape.FullyQualifiedTypeName);
            var id = BuildContractId(
                declaringType,
                member.MemberName,
                member.Topic,
                member.Source,
                shape.CanonicalRosType,
                member);
            sb.AppendLine(pad + "        registrar.Register<" + typeName + ">(");
            sb.AppendLine(pad + "            new " + NativeNamespace + "FoxRunRos2GeneratedContract(");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(id) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.Topic) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(declaringType) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(member.MemberName) + "\",");
            sb.AppendLine(pad + "                \"" + StringLiteralEmitter.CSharpStringLiteral(shape.CanonicalRosType) + "\",");
            sb.AppendLine(pad + "                " + ModeLiteral(member.Mode) + ",");
            sb.AppendLine(pad + "                " + SourceLiteral(member.Source) + ",");
            AppendQosArguments(sb, pad + "                ", member);
            sb.AppendLine(pad + "                " + (member.GeneratesRos2NativeRegistration ? "true" : "false") + ",");
            sb.AppendLine(pad + "                " + PolicyLiteral(member.Policy) + ",");
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(member.Hz) + ",");
            sb.AppendLine(pad + "                " + (member.HasExplicitHz ? "true" : "false") + ",");
            sb.AppendLine(pad + "                " + TypeExprEmitter.FloatLiteral(
                member.Policy == 2 && member.HasExplicitHz && member.Hz > 0f
                    ? 1f / member.Hz
                    : 0f) + "),");
            sb.AppendLine(pad + "            static (source, budget) => __FoxRunRos2Copy_" + index + "(source, budget),");
            sb.AppendLine(pad + "            static owned => __FoxRunRos2Dispose_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2Apply_" + index + "(owned),");
            sb.AppendLine(pad + "            owned => __FoxRunRos2ClearIfOwned_" + index + "(owned),");
            sb.AppendLine(pad + "            static (left, right) => __FoxRunRos2Equals_" + index + "(left, right),");
            sb.AppendLine(pad + "            " + (member.Policy == 4
                ? "() => global::System.Threading.Interlocked.Exchange(ref __foxRunRos2Trigger_" + TriggerFieldSuffix(member) + ", 0) != 0"
                : "static () => false") + ",");
            sb.AppendLine(pad + "            " + ConditionDelegate(member) + ");");
        }

        private static string ModeLiteral(int mode)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunFlow)" +
               mode.ToString(CultureInfo.InvariantCulture);

        private static string PolicyLiteral(int policy)
            => "(global::Unity.FoxgloveSDK.Components.FoxRunPolicy)" +
               policy.ToString(CultureInfo.InvariantCulture);

        private static string TriggerFieldSuffix(FoxgloveSourceEmitter.TopicMember member)
            => IdentifierUtils.SanitizeIdentifier(member.MemberName.TrimStart('_'))
               + "_"
               + TopicMetadataEmitter.Sha256Hex(
                       (member.MemberName ?? string.Empty) + "|" + (member.Topic ?? string.Empty))
                   .Substring(0, 8);

        private static string SourceLiteral(string provider)
        {
            if (string.Equals(provider, FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                    StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunEndpoint.Ros2Native";
            if (string.Equals(provider, FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                    StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunEndpoint.Foxglove";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunEndpoint)0";
        }

        internal static void AppendQosArguments(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
            bool trailingComma = true)
        {
            sb.AppendLine(pad + QosProfileLiteral(member.QosProfile) + ",");
            sb.AppendLine(pad + Has(member, FoxRunNamedArgumentPresence.QoS) + ",");
            sb.AppendLine(pad + QosReliabilityLiteral(member.QosReliability) + ",");
            sb.AppendLine(pad + Has(member, FoxRunNamedArgumentPresence.Reliability) + ",");
            sb.AppendLine(pad + QosDurabilityLiteral(member.QosDurability) + ",");
            sb.AppendLine(pad + Has(member, FoxRunNamedArgumentPresence.Durability) + ",");
            sb.AppendLine(pad + QosHistoryLiteral(member.QosHistory) + ",");
            sb.AppendLine(pad + Has(member, FoxRunNamedArgumentPresence.History) + ",");
            sb.AppendLine(pad + member.QosDepth.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine(
                pad
                + Has(member, FoxRunNamedArgumentPresence.Depth)
                + (trailingComma ? "," : string.Empty));
        }

        internal static string BuildContractId(
            string declaringType,
            string memberName,
            string topic,
            string source,
            string canonicalRosType,
            FoxgloveSourceEmitter.TopicMember member)
        {
            var id = new StringBuilder("foxrun-ros2-subscription:v2|");
            AppendContractIdSegment(id, declaringType);
            AppendContractIdSegment(id, memberName);
            AppendContractIdSegment(id, topic);
            AppendContractIdSegment(id, source);
            AppendContractIdSegment(id, canonicalRosType);
            AppendContractIdSegment(id, member.QosProfile);
            AppendContractIdSegment(id, member.QosReliability);
            AppendContractIdSegment(id, member.QosDurability);
            AppendContractIdSegment(id, member.QosHistory);
            AppendContractIdSegment(id, member.QosDepth.ToString(CultureInfo.InvariantCulture));
            AppendContractIdSegment(
                id,
                ((long)(member.NamedArgumentPresence
                        & (FoxRunNamedArgumentPresence.QoS
                           | FoxRunNamedArgumentPresence.Reliability
                           | FoxRunNamedArgumentPresence.Durability
                           | FoxRunNamedArgumentPresence.History
                           | FoxRunNamedArgumentPresence.Depth)))
                .ToString(CultureInfo.InvariantCulture));
            return id.ToString();
        }

        private static string Has(
            FoxgloveSourceEmitter.TopicMember member,
            FoxRunNamedArgumentPresence presence)
            => (member.NamedArgumentPresence & presence) != 0 ? "true" : "false";

        private static string QosProfileLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.DefaultQosProfile, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosProfile.Default";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SensorDataQosProfile, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosProfile.SensorData";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosProfile, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosProfile.SystemDefault";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunQosProfile)0";
        }

        private static string QosReliabilityLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.ReliableQosReliability, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosReliability.Reliable";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.BestEffortQosReliability, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosReliability.BestEffort";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosReliability.SystemDefault";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunQosReliability)0";
        }

        private static string QosDurabilityLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.VolatileQosDurability, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosDurability.Volatile";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.TransientLocalQosDurability, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosDurability.TransientLocal";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosDurability.SystemDefault";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunQosDurability)0";
        }

        private static string QosHistoryLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.KeepLastQosHistory, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosHistory.KeepLast";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.KeepAllQosHistory, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosHistory.KeepAll";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "global::Unity.FoxgloveSDK.Components.FoxRunQosHistory.SystemDefault";
            return "(global::Unity.FoxgloveSDK.Components.FoxRunQosHistory)0";
        }

        private static void AppendContractIdSegment(StringBuilder id, string value)
        {
            value = value ?? string.Empty;
            id.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            id.Append(':');
            id.Append(value);
        }

        private static void EmitBindingMethods(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
            int index,
            IReadOnlyList<string> publishTopics)
        {
            var shape = member.Ros2MessageShape;
            var typeName = GlobalTypeName(shape.FullyQualifiedTypeName);
            var helpers = new NestedHelperRegistry(index);
            sb.AppendLine();
            EmitCopyMethod(sb, pad, "__FoxRunRos2Copy_" + index, "__FoxRunRos2Dispose_" + index, shape, helpers);
            sb.AppendLine();
            EmitDisposeMethod(sb, pad, "__FoxRunRos2Dispose_" + index, shape, helpers);

            for (var helperIndex = 0; helperIndex < helpers.Count; helperIndex++)
            {
                var helper = helpers[helperIndex];
                sb.AppendLine();
                EmitCopyMethod(sb, pad, helper.CopyName, helper.DisposeName, helper.Shape, helpers);
                sb.AppendLine();
                EmitDisposeMethod(sb, pad, helper.DisposeName, helper.Shape, helpers);
            }

            sb.AppendLine();
            EmitEqualsMethod(sb, pad, "__FoxRunRos2Equals_" + index, shape, helpers);
            for (var helperIndex = 0; helperIndex < helpers.Count; helperIndex++)
            {
                var helper = helpers[helperIndex];
                sb.AppendLine();
                EmitEqualsMethod(sb, pad, helper.EqualsName, helper.Shape, helpers);
            }

            var access = TypeExprEmitter.MemberAccess(member.MemberName);
            sb.AppendLine();
            sb.AppendLine(pad + "    private void __FoxRunRos2Apply_" + index + "(" + typeName + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            " + access + " = owned;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            if (global::System.Object.ReferenceEquals(" + access + ", owned))");
            sb.AppendLine(pad + "            {");
            sb.AppendLine(pad + "                try");
            sb.AppendLine(pad + "                {");
            sb.AppendLine(pad + "                    " + access + " = null;");
            sb.AppendLine(pad + "                }");
            sb.AppendLine(pad + "                catch");
            sb.AppendLine(pad + "                {");
            sb.AppendLine(pad + "                }");
            sb.AppendLine(pad + "                if (global::System.Object.ReferenceEquals(" + access + ", owned))");
            sb.AppendLine(pad + "                {");
            sb.AppendLine(pad + "                    __foxRunRos2AppliedOwned_" + index + " = owned;");
            EmitRemoteOriginMark(sb, pad + "                    ", member, publishTopics);
            sb.AppendLine(pad + "                    return;");
            sb.AppendLine(pad + "                }");
            sb.AppendLine(pad + "            }");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        __foxRunRos2AppliedOwned_" + index + " = owned;");
            EmitRemoteOriginMark(sb, pad + "        ", member, publishTopics);
            sb.AppendLine(pad + "    }");
            sb.AppendLine();
            sb.AppendLine(pad + "    private bool __FoxRunRos2ClearIfOwned_" + index + "(" + typeName + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        var cleared = false;");
            sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(" + access + ", owned))");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            " + access + " = null;");
            sb.AppendLine(pad + "            cleared = true;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(__foxRunRos2AppliedOwned_" + index + ", owned))");
            sb.AppendLine(pad + "            __foxRunRos2AppliedOwned_" + index + " = null;");
            sb.AppendLine(pad + "        return cleared;");
            sb.AppendLine(pad + "    }");

        }

        private static string ConditionDelegate(FoxgloveSourceEmitter.TopicMember member)
        {
            return string.IsNullOrWhiteSpace(member.OnlyIf)
                ? "null"
                : "() => " + ConditionEmitter.ConditionAccess(
                    member.OnlyIf,
                    member.ConditionMemberKind);
        }

        private static void EmitEqualsMethod(
            StringBuilder sb,
            string pad,
            string methodName,
            FoxRunRos2MessageShape shape,
            NestedHelperRegistry helpers)
        {
            var typeName = GlobalTypeName(shape.FullyQualifiedTypeName);
            sb.AppendLine(pad + "    private static bool " + methodName + "(" + typeName + " left, " + typeName + " right)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(left, right)) return true;");
            sb.AppendLine(pad + "        if (left == null || right == null) return false;");
            for (var memberIndex = 0; memberIndex < shape.Members.Count; memberIndex++)
                EmitEqualsMember(sb, pad + "        ", shape.Members[memberIndex], memberIndex, helpers);
            sb.AppendLine(pad + "        return true;");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitRemoteOriginMark(
            StringBuilder sb,
            string pad,
            FoxgloveSourceEmitter.TopicMember member,
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

        private static void EmitEqualsMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            int memberIndex,
            NestedHelperRegistry helpers)
        {
            var name = IdentifierUtils.EscapeIdentifier(member.Name);
            var left = "left." + name;
            var right = "right." + name;
            if (member.Kind == FoxRunRos2MessageMemberKind.NestedMessage)
            {
                var helper = helpers.Get(member.NestedShape);
                sb.AppendLine(pad + "if (!" + helper.EqualsName + "(" + left + ", " + right + ")) return false;");
                return;
            }

            if (member.Kind != FoxRunRos2MessageMemberKind.Sequence)
            {
                var valueType = GlobalTypeName(member.FullyQualifiedTypeName);
                sb.AppendLine(pad + "if (!global::System.Collections.Generic.EqualityComparer<" + valueType + ">.Default.Equals(" + left + ", " + right + ")) return false;");
                return;
            }

            var suffix = memberIndex.ToString(CultureInfo.InvariantCulture);
            var leftSequence = "__leftSequence_" + suffix;
            var rightSequence = "__rightSequence_" + suffix;
            var leftCount = member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List
                ? leftSequence + ".Count"
                : leftSequence + ".Length";
            var rightCount = member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List
                ? rightSequence + ".Count"
                : rightSequence + ".Length";
            sb.AppendLine(pad + "var " + leftSequence + " = " + left + ";");
            sb.AppendLine(pad + "var " + rightSequence + " = " + right + ";");
            sb.AppendLine(pad + "if (!global::System.Object.ReferenceEquals(" + leftSequence + ", " + rightSequence + "))");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    if (" + leftSequence + " == null || " + rightSequence + " == null) return false;");
            sb.AppendLine(pad + "    if (" + leftCount + " != " + rightCount + ") return false;");
            sb.AppendLine(pad + "    for (var __i = 0; __i < " + leftCount + "; __i++)");
            sb.AppendLine(pad + "    {");
            if (member.NestedShape != null)
            {
                var helper = helpers.Get(member.NestedShape);
                sb.AppendLine(pad + "        if (!" + helper.EqualsName + "(" + leftSequence + "[__i], " + rightSequence + "[__i])) return false;");
            }
            else
            {
                var elementType = GlobalTypeName(member.SequenceElementTypeName);
                sb.AppendLine(pad + "        if (!global::System.Collections.Generic.EqualityComparer<" + elementType + ">.Default.Equals(" + leftSequence + "[__i], " + rightSequence + "[__i])) return false;");
            }
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
        }

        private static void EmitCopyMethod(
            StringBuilder sb,
            string pad,
            string copyName,
            string disposeName,
            FoxRunRos2MessageShape shape,
            NestedHelperRegistry helpers)
        {
            var typeName = GlobalTypeName(shape.FullyQualifiedTypeName);
            sb.AppendLine(pad + "    private static " + typeName + " " + copyName + "(");
            sb.AppendLine(pad + "        " + typeName + " source,");
            sb.AppendLine(pad + "        " + NativeNamespace + "FoxRunRos2CopyContext budget)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (source == null) return null;");
            sb.AppendLine(pad + "        if (budget == null) throw new global::System.ArgumentNullException(nameof(budget));");
            sb.AppendLine(pad + "        var target = new " + typeName + "();");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            foreach (var member in shape.Members)
                EmitCopyMember(sb, pad + "            ", member, helpers);
            sb.AppendLine(pad + "            return target;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            EmitBestEffortDisposeCall(sb, pad + "            ", disposeName + "(target);");
            sb.AppendLine(pad + "            throw;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitCopyMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            NestedHelperRegistry helpers)
        {
            var name = IdentifierUtils.EscapeIdentifier(member.Name);
            var source = "source." + name;
            var target = "target." + name;
            var local = IdentifierUtils.SanitizeIdentifier(member.Name);
            switch (member.Kind)
            {
                case FoxRunRos2MessageMemberKind.Scalar:
                case FoxRunRos2MessageMemberKind.Enum:
                    sb.AppendLine(pad + target + " = " + source + ";");
                    return;
                case FoxRunRos2MessageMemberKind.String:
                    sb.AppendLine(pad + "var __source_" + local + " = " + source + ";");
                    sb.AppendLine(pad + "if (__source_" + local + " != null)");
                    sb.AppendLine(pad + "    budget.RequireBytes(checked((long)__source_" + local + ".Length * 2L));");
                    sb.AppendLine(pad + target + " = __source_" + local + ";");
                    return;
                case FoxRunRos2MessageMemberKind.NestedMessage:
                    EmitNestedCopyMember(sb, pad, member, source, target, local, helpers);
                    return;
                case FoxRunRos2MessageMemberKind.Sequence:
                    EmitSequenceCopyMember(sb, pad, member, source, target, local, helpers);
                    return;
                default:
                    throw new InvalidOperationException("Unsupported ROS2 copy member kind: " + member.Kind + ".");
            }
        }

        private static void EmitNestedCopyMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            string source,
            string target,
            string local,
            NestedHelperRegistry helpers)
        {
            var helper = helpers.Get(member.NestedShape);
            sb.AppendLine(pad + "var __defaultOwned_" + local + " = " + target + ";");
            sb.AppendLine(pad + "var __detachedDefault_" + local + " = false;");
            sb.AppendLine(pad + "try");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + target + " = null;");
            sb.AppendLine(pad + "    __detachedDefault_" + local + " = !global::System.Object.ReferenceEquals(" + target + ", __defaultOwned_" + local + ");");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "catch");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    __detachedDefault_" + local + " = !global::System.Object.ReferenceEquals(" + target + ", __defaultOwned_" + local + ");");
            sb.AppendLine(pad + "    if (__detachedDefault_" + local + ")");
            EmitBestEffortDisposeCall(sb, pad + "        ", helper.DisposeName + "(__defaultOwned_" + local + ");");
            sb.AppendLine(pad + "    throw;");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "if (__detachedDefault_" + local + ")");
            sb.AppendLine(pad + "    " + helper.DisposeName + "(__defaultOwned_" + local + ");");
            sb.AppendLine(pad + "var __copied_" + local + " = " + helper.CopyName + "(" + source + ", budget);");
            sb.AppendLine(pad + "try");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + target + " = __copied_" + local + ";");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "catch");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    if (global::System.Object.ReferenceEquals(" + target + ", __copied_" + local + "))");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        try");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "            " + target + " = null;");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        catch");
            sb.AppendLine(pad + "        {");
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "    if (!global::System.Object.ReferenceEquals(" + target + ", __copied_" + local + "))");
            EmitBestEffortDisposeCall(sb, pad + "        ", helper.DisposeName + "(__copied_" + local + ");");
            sb.AppendLine(pad + "    throw;");
            sb.AppendLine(pad + "}");
        }

        private static void EmitSequenceCopyMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            string source,
            string target,
            string local,
            NestedHelperRegistry helpers)
        {
            var elementType = GlobalTypeName(member.SequenceElementTypeName);
            sb.AppendLine(pad + "var __source_" + local + " = " + source + ";");
            var detachesWritableDefault = member.NestedShape != null
                                          && member.CanWrite
                                          && member.SequenceRepresentation != FoxRunRos2SequenceRepresentation.FixedArray;
            if (detachesWritableDefault)
            {
                var helper = helpers.Get(member.NestedShape);
                sb.AppendLine(pad + "var __defaultOwned_" + local + " = " + target + ";");
                sb.AppendLine(pad + "var __detachedDefault_" + local + " = false;");
                sb.AppendLine(pad + "try");
                sb.AppendLine(pad + "{");
                sb.AppendLine(pad + "    " + target + " = null;");
                sb.AppendLine(pad + "    __detachedDefault_" + local + " = !global::System.Object.ReferenceEquals(" + target + ", __defaultOwned_" + local + ");");
                sb.AppendLine(pad + "}");
                sb.AppendLine(pad + "catch");
                sb.AppendLine(pad + "{");
                sb.AppendLine(pad + "    __detachedDefault_" + local + " = !global::System.Object.ReferenceEquals(" + target + ", __defaultOwned_" + local + ");");
                sb.AppendLine(pad + "    if (__detachedDefault_" + local + " && __defaultOwned_" + local + " != null)");
                sb.AppendLine(pad + "        foreach (var __owned in __defaultOwned_" + local + ")");
                EmitBestEffortDisposeCall(sb, pad + "            ", helper.DisposeName + "(__owned);");
                sb.AppendLine(pad + "    throw;");
                sb.AppendLine(pad + "}");
                sb.AppendLine(pad + "if (__detachedDefault_" + local + " && __defaultOwned_" + local + " != null)");
                sb.AppendLine(pad + "    foreach (var __owned in __defaultOwned_" + local + ")");
                sb.AppendLine(pad + "        " + helper.DisposeName + "(__owned);");
            }
            if (member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.FixedArray)
            {
                sb.AppendLine(pad + "if (__source_" + local + " == null)");
                sb.AppendLine(pad + "    throw new global::System.InvalidOperationException(\"Fixed ROS2 sequence source must not be null.\");");
                sb.AppendLine(pad + "var __target_" + local + " = " + target + ";");
                var sourceLength = "sourceLength_" + local;
                var targetLength = "targetLength_" + local;
                sb.AppendLine(pad + "var " + sourceLength + " = __source_" + local + ".Length;");
                sb.AppendLine(pad + "var " + targetLength + " = __target_" + local + " == null ? -1 : __target_" + local + ".Length;");
                var expected = member.FixedSize > 0 ? member.FixedSize.ToString() : sourceLength;
                sb.AppendLine(pad + "if (" + targetLength + " != " + expected + " || " + sourceLength + " != " + targetLength + ")");
                sb.AppendLine(pad + "    throw new global::System.InvalidOperationException(\"Fixed ROS2 sequence target length does not match sourceLength/targetLength.\");");
                EmitBudget(sb, pad, sourceLength, member.SequenceElementTypeName);
                sb.AppendLine(pad + "for (var __i = 0; __i < " + sourceLength + "; __i++)");
                sb.AppendLine(pad + "{");
                EmitSequenceElementAssignment(sb, pad + "    ", member, "__target_" + local + "[__i]", "__source_" + local + "[__i]", helpers);
                sb.AppendLine(pad + "}");
                return;
            }

            sb.AppendLine(pad + "if (__source_" + local + " == null)");
            sb.AppendLine(pad + "{");
            if (!detachesWritableDefault)
                sb.AppendLine(pad + "    " + target + " = null;");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "else");
            sb.AppendLine(pad + "{");
            var count = member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List
                ? "__source_" + local + ".Count"
                : "__source_" + local + ".Length";
            EmitBudget(sb, pad + "    ", count, member.SequenceElementTypeName);
            var storageType = member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List
                ? "global::System.Collections.Generic.List<" + elementType + ">"
                : elementType + "[]";
            var allocation = member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List
                ? "new " + storageType + "(" + count + ")"
                : "new " + elementType + "[" + count + "]";
            sb.AppendLine(pad + "    var __values_" + local + " = " + allocation + ";");
            sb.AppendLine(pad + "    try");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        for (var __i = 0; __i < " + count + "; __i++)");
            sb.AppendLine(pad + "        {");
            if (member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.List)
                EmitSequenceElementAdd(sb, pad + "            ", member, "__values_" + local, "__source_" + local + "[__i]", helpers);
            else
                EmitSequenceElementAssignment(sb, pad + "            ", member, "__values_" + local + "[__i]", "__source_" + local + "[__i]", helpers);
            sb.AppendLine(pad + "        }");
            sb.AppendLine(pad + "        " + target + " = __values_" + local + ";");
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "    catch");
            sb.AppendLine(pad + "    {");
            if (member.NestedShape != null)
            {
                var helper = helpers.Get(member.NestedShape);
                sb.AppendLine(pad + "        if (global::System.Object.ReferenceEquals(" + target + ", __values_" + local + "))");
                sb.AppendLine(pad + "        {");
                sb.AppendLine(pad + "            try");
                sb.AppendLine(pad + "            {");
                sb.AppendLine(pad + "                " + target + " = null;");
                sb.AppendLine(pad + "            }");
                sb.AppendLine(pad + "            catch");
                sb.AppendLine(pad + "            {");
                sb.AppendLine(pad + "            }");
                sb.AppendLine(pad + "        }");
                sb.AppendLine(pad + "        if (!global::System.Object.ReferenceEquals(" + target + ", __values_" + local + "))");
                sb.AppendLine(pad + "            foreach (var __owned in __values_" + local + ")");
                EmitBestEffortDisposeCall(sb, pad + "                ", helper.DisposeName + "(__owned);");
            }
            sb.AppendLine(pad + "        throw;");
            sb.AppendLine(pad + "    }");
            sb.AppendLine(pad + "}");
        }

        private static void EmitSequenceElementAssignment(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            string destination,
            string source,
            NestedHelperRegistry helpers)
        {
            if (member.NestedShape != null)
            {
                var helper = helpers.Get(member.NestedShape);
                if (member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.FixedArray)
                {
                    sb.AppendLine(pad + "var __copiedItem = " + helper.CopyName + "(" + source + ", budget);");
                    sb.AppendLine(pad + "try");
                    sb.AppendLine(pad + "{");
                    sb.AppendLine(pad + "    var __defaultItem = " + destination + ";");
                    sb.AppendLine(pad + "    " + destination + " = null;");
                    sb.AppendLine(pad + "    " + helper.DisposeName + "(__defaultItem);");
                    sb.AppendLine(pad + "    " + destination + " = __copiedItem;");
                    sb.AppendLine(pad + "}");
                    sb.AppendLine(pad + "catch");
                    sb.AppendLine(pad + "{");
                    EmitBestEffortDisposeCall(sb, pad + "    ", helper.DisposeName + "(__copiedItem);");
                    sb.AppendLine(pad + "    throw;");
                    sb.AppendLine(pad + "}");
                }
                else
                {
                    sb.AppendLine(pad + destination + " = " + helper.CopyName + "(" + source + ", budget);");
                }
            }
            else if (string.Equals(member.SequenceElementTypeName, "System.String", StringComparison.Ordinal))
            {
                sb.AppendLine(pad + "var __item = " + source + ";");
                sb.AppendLine(pad + "if (__item != null) budget.RequireBytes(checked((long)__item.Length * 2L));");
                sb.AppendLine(pad + destination + " = __item;");
            }
            else
            {
                sb.AppendLine(pad + destination + " = " + source + ";");
            }
        }

        private static void EmitSequenceElementAdd(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            string destination,
            string source,
            NestedHelperRegistry helpers)
        {
            if (member.NestedShape != null)
            {
                var helper = helpers.Get(member.NestedShape);
                sb.AppendLine(pad + "var __copiedItem = " + helper.CopyName + "(" + source + ", budget);");
                sb.AppendLine(pad + "try");
                sb.AppendLine(pad + "{");
                sb.AppendLine(pad + "    " + destination + ".Add(__copiedItem);");
                sb.AppendLine(pad + "}");
                sb.AppendLine(pad + "catch");
                sb.AppendLine(pad + "{");
                sb.AppendLine(pad + "    try");
                sb.AppendLine(pad + "    {");
                sb.AppendLine(pad + "        " + helper.DisposeName + "(__copiedItem);");
                sb.AppendLine(pad + "    }");
                sb.AppendLine(pad + "    catch");
                sb.AppendLine(pad + "    {");
                sb.AppendLine(pad + "    }");
                sb.AppendLine(pad + "    throw;");
                sb.AppendLine(pad + "}");
            }
            else if (string.Equals(member.SequenceElementTypeName, "System.String", StringComparison.Ordinal))
            {
                sb.AppendLine(pad + "var __item = " + source + ";");
                sb.AppendLine(pad + "if (__item != null) budget.RequireBytes(checked((long)__item.Length * 2L));");
                sb.AppendLine(pad + destination + ".Add(__item);");
            }
            else
            {
                sb.AppendLine(pad + destination + ".Add(" + source + ");");
            }
        }

        private static void EmitBudget(StringBuilder sb, string pad, string count, string elementType)
        {
            sb.AppendLine(pad + "budget.RequireBytes(checked((long)" + count + " * " + ElementSize(elementType) + "L));");
        }

        private static void EmitBestEffortDisposeCall(StringBuilder sb, string pad, string call)
        {
            sb.AppendLine(pad + "try");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + call);
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "catch");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "}");
        }

        private static void EmitDisposeMethod(
            StringBuilder sb,
            string pad,
            string methodName,
            FoxRunRos2MessageShape shape,
            NestedHelperRegistry helpers)
        {
            var typeName = GlobalTypeName(shape.FullyQualifiedTypeName);
            sb.AppendLine(pad + "    private static void " + methodName + "(" + typeName + " owned)");
            sb.AppendLine(pad + "    {");
            sb.AppendLine(pad + "        if (owned == null) return;");
            sb.AppendLine(pad + "        global::System.Runtime.ExceptionServices.ExceptionDispatchInfo __firstCleanupException = null;");
            foreach (var member in shape.Members)
                EmitDisposeMember(sb, pad + "        ", member, helpers);
            EmitCapturedCleanupCall(sb, pad + "        ", "owned.Dispose();");
            sb.AppendLine(pad + "        if (__firstCleanupException != null)");
            sb.AppendLine(pad + "            __firstCleanupException.Throw();");
            sb.AppendLine(pad + "    }");
        }

        private static void EmitDisposeMember(
            StringBuilder sb,
            string pad,
            FoxRunRos2MessageMemberShape member,
            NestedHelperRegistry helpers)
        {
            if (member.NestedShape == null)
                return;

            var name = IdentifierUtils.EscapeIdentifier(member.Name);
            var local = IdentifierUtils.SanitizeIdentifier(member.Name);
            var helper = helpers.Get(member.NestedShape);
            sb.AppendLine(pad + "var __owned_" + local + " = owned." + name + ";");
            if (member.Kind == FoxRunRos2MessageMemberKind.Sequence)
            {
                if (member.SequenceRepresentation == FoxRunRos2SequenceRepresentation.FixedArray && !member.CanWrite)
                {
                    sb.AppendLine(pad + "if (__owned_" + local + " != null)");
                    sb.AppendLine(pad + "    for (var __i = 0; __i < __owned_" + local + ".Length; __i++)");
                    sb.AppendLine(pad + "    {");
                    sb.AppendLine(pad + "        var __item = __owned_" + local + "[__i];");
                    sb.AppendLine(pad + "        var __detachedItem = false;");
                    sb.AppendLine(pad + "        try");
                    sb.AppendLine(pad + "        {");
                    sb.AppendLine(pad + "            __owned_" + local + "[__i] = null;");
                    sb.AppendLine(pad + "            __detachedItem = !global::System.Object.ReferenceEquals(__owned_" + local + "[__i], __item);");
                    sb.AppendLine(pad + "        }");
                    sb.AppendLine(pad + "        catch (global::System.Exception __cleanupException)");
                    sb.AppendLine(pad + "        {");
                    sb.AppendLine(pad + "            if (__firstCleanupException == null)");
                    sb.AppendLine(pad + "                __firstCleanupException = global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(__cleanupException);");
                    sb.AppendLine(pad + "            __detachedItem = !global::System.Object.ReferenceEquals(__owned_" + local + "[__i], __item);");
                    sb.AppendLine(pad + "        }");
                    sb.AppendLine(pad + "        if (__detachedItem)");
                    EmitCapturedCleanupCall(sb, pad + "            ", helper.DisposeName + "(__item);");
                    sb.AppendLine(pad + "    }");
                }
                else
                {
                    EmitOwnedMemberDetach(sb, pad, name, local);
                    sb.AppendLine(pad + "if (__detachedOwned_" + local + " && __owned_" + local + " != null)");
                    sb.AppendLine(pad + "    foreach (var __item in __owned_" + local + ")");
                    EmitCapturedCleanupCall(sb, pad + "        ", helper.DisposeName + "(__item);");
                }
            }
            else
            {
                EmitOwnedMemberDetach(sb, pad, name, local);
                sb.AppendLine(pad + "if (__detachedOwned_" + local + ")");
                EmitCapturedCleanupCall(sb, pad + "    ", helper.DisposeName + "(__owned_" + local + ");");
            }
        }

        private static void EmitOwnedMemberDetach(StringBuilder sb, string pad, string name, string local)
        {
            sb.AppendLine(pad + "var __detachedOwned_" + local + " = false;");
            sb.AppendLine(pad + "try");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    owned." + name + " = null;");
            sb.AppendLine(pad + "    __detachedOwned_" + local + " = !global::System.Object.ReferenceEquals(owned." + name + ", __owned_" + local + ");");
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "catch (global::System.Exception __cleanupException)");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    if (__firstCleanupException == null)");
            sb.AppendLine(pad + "        __firstCleanupException = global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(__cleanupException);");
            sb.AppendLine(pad + "    __detachedOwned_" + local + " = !global::System.Object.ReferenceEquals(owned." + name + ", __owned_" + local + ");");
            sb.AppendLine(pad + "}");
        }

        private static void EmitCapturedCleanupCall(StringBuilder sb, string pad, string call)
        {
            sb.AppendLine(pad + "try");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    " + call);
            sb.AppendLine(pad + "}");
            sb.AppendLine(pad + "catch (global::System.Exception __cleanupException)");
            sb.AppendLine(pad + "{");
            sb.AppendLine(pad + "    if (__firstCleanupException == null)");
            sb.AppendLine(pad + "        __firstCleanupException = global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(__cleanupException);");
            sb.AppendLine(pad + "}");
        }

        private static string GlobalTypeName(string typeName)
        {
            var value = typeName ?? string.Empty;
            if (value.StartsWith("global::", StringComparison.Ordinal))
                return value;
            switch (value)
            {
                case "bool": case "byte": case "sbyte": case "short": case "ushort":
                case "int": case "uint": case "long": case "ulong": case "float":
                case "double": case "char": case "string": case "object":
                    return value;
                default:
                    return "global::" + value;
            }
        }

        private static int ElementSize(string typeName)
        {
            switch (typeName)
            {
                case "System.Boolean": case "System.Byte": case "System.SByte": return 1;
                case "System.Int16": case "System.UInt16": case "System.Char": return 2;
                case "System.Int32": case "System.UInt32": case "System.Single": return 4;
                case "System.Int64": case "System.UInt64": case "System.Double": return 8;
                case "System.String": return 8;
                default: return 8;
            }
        }

        private sealed class NestedHelperRegistry
        {
            private readonly int rootIndex;
            private readonly List<NestedHelper> helpers = new List<NestedHelper>();
            private readonly Dictionary<string, NestedHelper> byIdentity =
                new Dictionary<string, NestedHelper>(StringComparer.Ordinal);

            public NestedHelperRegistry(int rootIndex) => this.rootIndex = rootIndex;

            public int Count => helpers.Count;
            public NestedHelper this[int index] => helpers[index];

            public NestedHelper Get(FoxRunRos2MessageShape shape)
            {
                if (shape == null)
                    throw new InvalidOperationException("Nested ROS2 member is missing its recursive copy shape.");
                var identity = shape.CopyShapeIdentity + "|" + shape.FullyQualifiedTypeName;
                if (byIdentity.TryGetValue(identity, out var existing))
                    return existing;
                var index = helpers.Count;
                var helper = new NestedHelper(
                    shape,
                    "__FoxRunRos2CopyNested_" + rootIndex + "_" + index,
                    "__FoxRunRos2DisposeNested_" + rootIndex + "_" + index,
                    "__FoxRunRos2EqualsNested_" + rootIndex + "_" + index);
                helpers.Add(helper);
                byIdentity.Add(identity, helper);
                return helper;
            }
        }

        private sealed class NestedHelper
        {
            public NestedHelper(
                FoxRunRos2MessageShape shape,
                string copyName,
                string disposeName,
                string equalsName)
            {
                Shape = shape;
                CopyName = copyName;
                DisposeName = disposeName;
                EqualsName = equalsName;
            }

            public FoxRunRos2MessageShape Shape { get; }
            public string CopyName { get; }
            public string DisposeName { get; }
            public string EqualsName { get; }
        }
    }
}
