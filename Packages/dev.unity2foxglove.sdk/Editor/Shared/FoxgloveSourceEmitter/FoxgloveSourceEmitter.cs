// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Pure C# string-builder that produces the generated partial class
    /// implementing <c>IFoxgloveLogSource</c>. Both the Roslyn ISG and the
    /// build-time physical fallback call this emitter so policy and payload
    /// generation cannot drift between Editor and Player paths.
    /// </summary>
    /// <remarks>
    /// This file lives under <c>Editor/Shared/</c> and is compiled by both:
    /// <list type="bullet">
    ///   <item>Unity Editor assembly (via <c>Editor/</c> .asmdef)</item>
    ///   <item>Source Generator project (via linked compile item in the
    ///       <c>.csproj</c>)</item>
    /// </list>
    /// It must NOT depend on any Roslyn, UnityEngine, or UnityEditor types.
    /// </remarks>
    public static class FoxgloveSourceEmitter
    {
        private const int PolicyFixedRate = 1;
        private const int PolicyChange = 2;
        private const int PolicyTrigger = 4;
        private const int FlowPublish = 1;
        private const int FlowSubscribe = 2;
        private const int FlowPublishAndSubscribe = 3;

        /// <summary>
        /// Descriptor for a single topic-member mapping used by the shared
        /// emitter. Backs both <c>FoxrunCodeGenerator.MemberData</c> and the
        /// ISG's <c>ExtractMember</c> output.
        /// </summary>
        public sealed class TopicMember
        {
            /// <summary>Field or property name as declared in source.</summary>
            public readonly string MemberName;
            /// <summary>Fully-qualified type name (e.g.
            /// <c>UnityEngine.Vector3</c>).</summary>
            public readonly string TypeName;
            /// <summary>Canonical schema identity token for this member.</summary>
            public readonly string CanonicalType;
            /// <summary>Topic string from <c>[FoxRun("/topic")]</c>.</summary>
            public readonly string Topic;
            /// <summary>Publishing rate in Hz.</summary>
            public readonly float Hz;
            /// <summary>True when the declaration explicitly supplied Hz.</summary>
            public readonly bool HasExplicitHz;
            /// <summary>Optional schema name.</summary>
            public readonly string SchemaName;
            /// <summary>Publish mode from the attribute.</summary>
            public readonly int Policy;
            /// <summary>FoxRun data-flow mode from the attribute.</summary>
            public readonly int Mode;
            /// <summary>Change tolerance for numeric comparison.</summary>
            public readonly float Tolerance;
            /// <summary>Optional bool member that must be true at the directional boundary.</summary>
            public readonly string OnlyIf;
            /// <summary>Resolved field/property/method shape for <see cref="OnlyIf"/>.</summary>
            public readonly FoxRunConditionMemberKind ConditionMemberKind;
            /// <summary>True when the member belongs to a class-level FoxRun aggregate message.</summary>
            public readonly bool IsAggregateMember;
            /// <summary>JSON property name emitted for aggregate and dictionary payloads.</summary>
            public readonly string JsonFieldName;
            /// <summary>Declared FoxRun wire policy: inherit, protobuf, or json.</summary>
            public readonly string Encoding;
            /// <summary>Optional stable Protobuf field-number override.</summary>
            public readonly int ProtobufFieldNumber;
            /// <summary>DTO/enum shape used for direct Protobuf code generation.</summary>
            public readonly FoxRunProtobufTypeShape ProtobufTypeShape;
            /// <summary>Normalized declared subscription provider.</summary>
            public readonly string Source;
            /// <summary>Normalized declared publish-target set.</summary>
            public readonly string Targets;
            public readonly string QosProfile;
            public readonly string QosReliability;
            public readonly string QosDurability;
            public readonly string QosHistory;
            public readonly int QosDepth;
            /// <summary>True when a byte-router codec is valid for this member.</summary>
            public readonly bool GeneratesWebSocketCodec;
            /// <summary>True when a validated closed native binding can be emitted.</summary>
            public readonly bool GeneratesRos2NativeRegistration;
            /// <summary>Validated host-neutral recursive native message shape.</summary>
            public readonly FoxRunRos2MessageShape Ros2MessageShape;
            /// <summary>Explicit native contract category; never infer this from a type name.</summary>
            public readonly FoxRunRos2ContractKind Ros2ContractKind;
            /// <summary>Schema for a generated custom ROS2 interface, if applicable.</summary>
            public readonly FoxRunRos2CustomDtoShape Ros2CustomDtoShape;
            /// <summary>Exact optional arguments written on the source declaration.</summary>
            public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;

            /// <summary>
            /// Creates a topic-member descriptor for the shared emitter.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float hz, string schemaName)
                : this(memberName, typeName, topic, hz, schemaName, PolicyFixedRate, 0f, mode: FlowPublish) { }

            /// <summary>
            /// Creates a topic-member descriptor with an explicit wire contract.
            /// </summary>
            public TopicMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName,
                string encoding,
                int protobufFieldNumber = 0,
                FoxRunProtobufTypeShape protobufTypeShape = null)
                : this(
                    memberName,
                    typeName,
                    topic,
                    hz,
                    schemaName,
                    PolicyFixedRate,
                    0f,
                    encoding: encoding,
                    protobufFieldNumber: protobufFieldNumber,
                    protobufTypeShape: protobufTypeShape,
                    mode: FlowPublish) { }

            /// <summary>
            /// Creates a topic-member descriptor with publish policy.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float hz, string schemaName,
                int policy, float tolerance, string onlyIf = "",
                bool isAggregateMember = false, string jsonFieldName = "", int mode = FlowPublish, string canonicalType = "",
                string encoding = FoxRunGenerationDescriptorConstants.JsonEncoding, int protobufFieldNumber = 0,
                FoxRunProtobufTypeShape protobufTypeShape = null,
                string source = FoxRunGenerationDescriptorConstants.InheritSource,
                string qosProfile = FoxRunGenerationDescriptorConstants.InheritQosProfile,
                bool generatesWebSocketCodec = true,
                bool generatesRos2NativeRegistration = false,
                FoxRunRos2MessageShape ros2MessageShape = null,
                FoxRunRos2CustomDtoShape ros2CustomDtoShape = null,
                FoxRunRos2ContractKind ros2ContractKind = FoxRunRos2ContractKind.Unsupported,
                bool hasExplicitHz = true,
                FoxRunConditionMemberKind conditionMemberKind = FoxRunConditionMemberKind.None,
                FoxRunNamedArgumentPresence namedArgumentPresence = FoxRunNamedArgumentPresence.None,
                string targets = FoxRunGenerationDescriptorConstants.InheritTargets,
                string qosReliability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
                string qosDurability = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
                string qosHistory = FoxRunGenerationDescriptorConstants.InheritQosPolicy,
                int qosDepth = 0)
            {
                MemberName = memberName;
                TypeName = typeName;
                CanonicalType = string.IsNullOrWhiteSpace(canonicalType)
                    ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(typeName)
                    : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(canonicalType);
                Topic = topic;
                Hz = hz;
                HasExplicitHz = hasExplicitHz;
                SchemaName = schemaName;
                Policy = policy;
                Mode = mode;
                Tolerance = tolerance;
                OnlyIf = onlyIf ?? string.Empty;
                ConditionMemberKind = conditionMemberKind;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                    ? DefaultJsonFieldName(memberName)
                    : jsonFieldName;
                Encoding = string.IsNullOrWhiteSpace(encoding)
                    ? FoxRunGenerationDescriptorConstants.JsonEncoding
                    : encoding;
                ProtobufFieldNumber = protobufFieldNumber;
                ProtobufTypeShape = protobufTypeShape;
                Source = source ?? FoxRunGenerationDescriptorConstants.InheritSource;
                Targets = targets ?? FoxRunGenerationDescriptorConstants.InheritTargets;
                QosProfile = qosProfile ?? FoxRunGenerationDescriptorConstants.InheritQosProfile;
                QosReliability = qosReliability ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                QosDurability = qosDurability ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                QosHistory = qosHistory ?? FoxRunGenerationDescriptorConstants.InheritQosPolicy;
                QosDepth = qosDepth;
                GeneratesWebSocketCodec = generatesWebSocketCodec;
                GeneratesRos2NativeRegistration = generatesRos2NativeRegistration;
                Ros2MessageShape = ros2MessageShape;
                Ros2CustomDtoShape = ros2CustomDtoShape;
                Ros2ContractKind = ros2ContractKind;
                NamedArgumentPresence = namedArgumentPresence;
            }
        }

        /// <summary>
        /// Returns the stable generated source file name for a FoxRun partial
        /// class. Global-namespace classes keep the historical
        /// <c>ClassName_FoxRun.g.cs</c> shape; namespaced classes include the
        /// namespace identity to avoid Roslyn hint-name and physical fallback
        /// file collisions.
        /// </summary>
        public static string GeneratedSourceName(string ns, string className)
        {
            var identity = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            return IdentifierUtils.SanitizeFileStem(identity) + "_FoxRun.g.cs";
        }

        /// <summary>
        /// Emits the generated partial class source for one generation model.
        /// </summary>
        /// <param name="type">Generation model for one class.</param>
        /// <returns>Generated C# source as a string.</returns>
        public static string EmitClass(FoxRunGenerationType type)
            => EmitClass(type, emitRos2NativePartial: true);

        /// <summary>
        /// Emits one class while allowing the Roslyn host to suppress only the
        /// optional native partial when the consuming assembly lacks the exact
        /// Native interface reference.
        /// </summary>
        public static string EmitClass(FoxRunGenerationType type, bool emitRos2NativePartial)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return EmitClassCore(
                type.Namespace,
                type.ClassName,
                type.Members.Select(member => member.ToTopicMember()).ToList(),
                emitRos2NativePartial);
        }

        // Public API forwarding wrappers — the implementations live in sub-emitters
        // to keep each file focused, but the public surface remains on FoxgloveSourceEmitter.

        /// <inheritdoc cref="TypeExprEmitter.ChangeExpr"/>
        public static string ChangeExpr(string member, string type, string lastVar, float epsilon)
            => TypeExprEmitter.ChangeExpr(member, type, lastVar, epsilon);

        /// <inheritdoc cref="TypeExprEmitter.ValueExpr"/>
        public static string ValueExpr(string name, string type)
            => TypeExprEmitter.ValueExpr(name, type);

        /// <summary>
        /// Internal convenience overload that delegates to the core emitter
        /// using a namespace, class name, and member list directly.
        /// </summary>
        internal static string EmitClass(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            return EmitClassCore(ns, className, members, emitRos2NativePartial: true);
        }

        internal static IReadOnlyList<string> GeneratedMethodNames(FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return TriggerEmitter.BuildGeneratedMethodNames(
                type.Members.Select(member => member.ToTopicMember()).ToList());
        }

        private static string EmitClassCore(
            string ns,
            string className,
            IReadOnlyList<TopicMember> members,
            bool emitRos2NativePartial)
        {
            if (members == null || members.Count == 0)
                throw new ArgumentException("At least one member is required.", nameof(members));

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] == null)
                    throw new ArgumentException("TopicMember cannot be null.", nameof(members));
            }

            var publishMembers = members
                .Where(member => member.Mode != FlowSubscribe)
                .ToList();
            var inputMembers = members
                .Where(member => member.Mode == FlowSubscribe || member.Mode == FlowPublishAndSubscribe)
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
                .ToList();
            var webSocketInputMembers = inputMembers
                .Where(member => member.GeneratesWebSocketCodec
                                 && !string.Equals(
                                     member.Source,
                                     FoxRunGenerationDescriptorConstants.Ros2NativeSource,
                                     StringComparison.Ordinal))
                .ToList();
            var nativeInputMembers = inputMembers
                .Where(member => member.GeneratesRos2NativeRegistration
                                 && member.Ros2MessageShape != null
                                 && !string.Equals(
                                     member.Source,
                                     FoxRunGenerationDescriptorConstants.FoxgloveWebSocketSource,
                                     StringComparison.Ordinal))
                .ToList();
            var customNativeInputMembers = inputMembers
                .Where(IsCustomNativeMember)
                .ToList();

            foreach (var m in inputMembers)
            {
                if (string.IsNullOrWhiteSpace(m.MemberName))
                    throw new ArgumentException("Input TopicMember has empty MemberName.", nameof(members));
                if (string.IsNullOrWhiteSpace(m.TypeName))
                    throw new ArgumentException("Input TopicMember '" + m.MemberName + "' has empty TypeName.", nameof(members));
                if (string.IsNullOrWhiteSpace(m.Topic))
                    throw new ArgumentException("Input TopicMember '" + m.MemberName + "' has empty Topic.", nameof(members));
            }

            var topicMap = new Dictionary<string, List<TopicMember>>();
            foreach (var m in publishMembers)
            {
                if (m == null)
                    throw new ArgumentException("TopicMember cannot be null.", nameof(members));
                if (string.IsNullOrWhiteSpace(m.MemberName))
                    throw new ArgumentException("TopicMember has empty MemberName.", nameof(members));
                if (string.IsNullOrWhiteSpace(m.TypeName))
                    throw new ArgumentException("TopicMember '" + m.MemberName + "' has empty TypeName.", nameof(members));
                if (string.IsNullOrWhiteSpace(m.Topic))
                    throw new ArgumentException("TopicMember '" + m.MemberName + "' has empty Topic.", nameof(members));

                if (!topicMap.TryGetValue(m.Topic, out var list))
                    topicMap[m.Topic] = list = new List<TopicMember>();
                list.Add(m);
            }

            var topics = topicMap.Keys.OrderBy(topic => topic, StringComparer.Ordinal).ToList();
            var topicModes = topicMap.ToDictionary(kvp => kvp.Key, kvp => TopicPolicy(kvp.Value));
            // A custom ROS2 contract is one ordinary DTO member per topic. A
            // field-level aggregate has a dictionary-shaped legacy bus payload
            // and must never be handed to the closed generic native publisher.
            var nativeBusMembers = topicMap
                .Where(pair => pair.Value.Count == 1
                               && pair.Value[0].Mode != FlowSubscribe
                               && IsCustomNativeMember(pair.Value[0]))
                .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);
            var customNativePublishMembers = nativeBusMembers
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList();
            var hasPolicy = publishMembers.Any(m => m.Policy != PolicyFixedRate);
            var hasConditions = publishMembers.Any(m => !string.IsNullOrWhiteSpace(m.OnlyIf));
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            ClassFrameEmitter.EmitClassFrame(
                sb,
                ns,
                className,
                topics.Count,
                hasPolicy,
                hasConditions,
                nativeBusMembers.Count > 0,
                webSocketInputMembers.Count > 0,
                pad);
            if (topics.Count > 0)
            {
                TopicMetadataEmitter.EmitGetTopic(sb, topics, topicMap, topicModes, pad);
                TopicMetadataEmitter.EmitGetContract(sb, ns, className, topics, topicMap, pad);
                PublishDispatchEmitter.EmitPublish(sb, ns, className, topics, topicMap, pad);
                PublishDispatchEmitter.EmitPublishToBus(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    nativeBusMembers,
                    pad);
                PublishDispatchEmitter.EmitPublishToSinks(sb, ns, className, topics, topicMap, pad);
                ConditionEmitter.EmitConditions(sb, topics, topicMap, pad);
            }
            InputDispatchEmitter.EmitInput(sb, ns, className, webSocketInputMembers, topics, pad);
            var applyMethods = TriggerEmitter.BuildApplyMembers(inputMembers);
            TriggerEmitter.EmitApplyMethods(
                sb,
                applyMethods,
                webSocketInputMembers,
                nativeInputMembers,
                customNativeInputMembers,
                pad,
                emitRos2NativePartial);

            var publishMethods = TriggerEmitter.BuildPublishMembers(publishMembers, topics);
            TriggerEmitter.EmitPublishMethods(sb, publishMethods, pad);

            if (topics.Count > 0)
                PolicyEmitter.EmitPolicy(sb, topics, topicMap, topicModes, pad);

            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");

            if (emitRos2NativePartial)
            {
                Ros2InputDispatchEmitter.EmitConditionalPartial(sb, ns, className, nativeInputMembers);
                Ros2CustomDtoMapperEmitter.EmitConditionalPartial(sb, ns, className, customNativeInputMembers);
                Ros2CustomPublishEmitter.EmitConditionalPartial(sb, ns, className, customNativePublishMembers);
            }

            return sb.ToString();
        }

        private static bool IsCustomNativeMember(TopicMember member)
        {
            return member != null
                   && member.GeneratesRos2NativeRegistration
                   && member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
                   && member.Ros2CustomDtoShape != null
                   && member.Ros2CustomDtoShape.IsSupported
                   && member.Ros2CustomDtoShape.HasPublicParameterlessConstructor
                   && member.Ros2CustomDtoShape.Diagnostics.Count == 0
                   && !string.IsNullOrWhiteSpace(member.Ros2CustomDtoShape.PayloadIdentity);
        }

        private static int TopicPolicy(IReadOnlyList<TopicMember> fields)
        {
            if (fields.Any(f => f.Policy == PolicyTrigger))
                return PolicyTrigger;
            if (fields.Any(f => f.Policy == PolicyChange))
                return PolicyChange;
            return PolicyFixedRate;
        }

        internal static string DefaultJsonFieldName(string memberName)
        {
            var name = memberName != null && memberName.StartsWith("@", StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? string.Empty;
            return name.TrimStart('_');
        }
    }
}
