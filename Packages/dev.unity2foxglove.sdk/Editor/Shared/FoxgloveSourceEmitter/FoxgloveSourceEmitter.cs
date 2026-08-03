// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Provider-neutral FoxRun partial-class source emitter.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxgloveSourceEmitter
    {
        private const int PolicyFixedRate = 1;
        private const int PolicyChange = 2;
        private const int PolicyTrigger = 4;
        private const int FlowPublish = 1;
        private const int FlowSubscribe = 2;
        private const int FlowPublishAndSubscribe = 3;

        public sealed class TopicMember
        {
            public readonly string MemberName;
            public readonly string TypeName;
            public readonly string CanonicalType;
            public readonly string Topic;
            public readonly float Hz;
            public readonly bool HasExplicitHz;
            public readonly string SchemaName;
            public readonly int Policy;
            public readonly int Mode;
            public readonly float Tolerance;
            public readonly string OnlyIf;
            public readonly FoxRunConditionMemberKind ConditionMemberKind;
            public readonly bool IsAggregateMember;
            public readonly string JsonFieldName;
            public readonly string Encoding;
            public readonly int ProtobufFieldNumber;
            public readonly FoxRunProtobufMetadata ProtobufMetadata;
            public readonly FoxRunTypeShape TypeShape;
            public readonly bool GeneratesWebSocketCodec;
            public readonly FoxRunNamedArgumentPresence NamedArgumentPresence;
            public readonly bool IsStream;
            public readonly IReadOnlyList<string> PublishTransportIds;
            public readonly string SubscribeTransportId;
            public readonly string Reliability;
            public readonly string Durability;
            public readonly string History;
            public readonly int Depth;
            public readonly object ProviderData;

            public TopicMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName)
                : this(
                    memberName,
                    typeName,
                    topic,
                    hz,
                    schemaName,
                    PolicyFixedRate,
                    0f,
                    mode: FlowPublish)
            {
            }

            public TopicMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName,
                string encoding,
                int protobufFieldNumber = 0,
                FoxRunTypeShape typeShape = null)
                : this(
                    memberName,
                    typeName,
                    topic,
                    hz,
                    schemaName,
                    PolicyFixedRate,
                    0f,
                    mode: FlowPublish,
                    encoding: encoding,
                    protobufFieldNumber: protobufFieldNumber,
                    typeShape: typeShape)
            {
            }

            public TopicMember(
                string memberName,
                string typeName,
                string topic,
                float hz,
                string schemaName,
                int policy,
                float tolerance,
                string onlyIf = "",
                bool isAggregateMember = false,
                string jsonFieldName = "",
                int mode = FlowPublish,
                string canonicalType = "",
                string encoding =
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                int protobufFieldNumber = 0,
                FoxRunTypeShape typeShape = null,
                bool generatesWebSocketCodec = true,
                bool hasExplicitHz = true,
                FoxRunConditionMemberKind conditionMemberKind =
                    FoxRunConditionMemberKind.None,
                FoxRunNamedArgumentPresence namedArgumentPresence =
                    FoxRunNamedArgumentPresence.None,
                bool isStream = false,
                FoxRunProtobufMetadata protobufMetadata = null,
                IReadOnlyList<string> publishTransportIds = null,
                string subscribeTransportId = null,
                string reliability = "inherit",
                string durability = "inherit",
                string history = "inherit",
                int depth = 0,
                object providerData = null)
            {
                MemberName = memberName ?? string.Empty;
                TypeName = typeName ?? string.Empty;
                CanonicalType = string.IsNullOrWhiteSpace(canonicalType)
                    ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                        TypeName)
                    : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(
                        canonicalType);
                Topic = topic ?? string.Empty;
                Hz = hz;
                HasExplicitHz = hasExplicitHz;
                SchemaName = schemaName ?? string.Empty;
                Policy = policy;
                Mode = mode;
                Tolerance = tolerance;
                OnlyIf = onlyIf ?? string.Empty;
                ConditionMemberKind = conditionMemberKind;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                    ? DefaultJsonFieldName(MemberName)
                    : jsonFieldName;
                Encoding = string.IsNullOrWhiteSpace(encoding)
                    ? FoxRunGenerationDescriptorConstants.InheritEncoding
                    : encoding;
                TypeShape = typeShape;
                ProtobufMetadata =
                    protobufMetadata
                    ?? (protobufFieldNumber != 0
                        || string.Equals(
                            Encoding,
                            FoxRunGenerationDescriptorConstants
                                .ProtobufEncoding,
                            StringComparison.Ordinal)
                        || string.Equals(
                            Encoding,
                            FoxRunGenerationDescriptorConstants
                                .InheritEncoding,
                            StringComparison.Ordinal)
                            ? FoxRunProtobufMetadata.FromTypeShape(
                                typeShape,
                                protobufFieldNumber)
                            : null);
                ProtobufFieldNumber =
                    ProtobufMetadata?.FieldNumber ?? 0;
                GeneratesWebSocketCodec = generatesWebSocketCodec;
                NamedArgumentPresence = namedArgumentPresence;
                IsStream = isStream;
                PublishTransportIds = publishTransportIds;
                SubscribeTransportId = subscribeTransportId;
                Reliability = reliability ?? "inherit";
                Durability = durability ?? "inherit";
                History = history ?? "inherit";
                Depth = depth;
                ProviderData = providerData;
            }
        }

        public static string GeneratedSourceName(
            string ns,
            string className)
        {
            var identity = string.IsNullOrEmpty(ns)
                ? className
                : ns + "." + className;
            return IdentifierUtils.SanitizeFileStem(identity)
                   + "_FoxRun.g.cs";
        }

        public static string EmitClass(FoxRunGenerationType type)
            => EmitCoreClass(type);

        public static string EmitCoreClass(FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            return EmitClassCore(
                type.Namespace,
                type.ClassName,
                type.Members
                    .Select(member => member.ToTopicMember())
                    .ToList(),
                type);
        }

        public static string ChangeExpr(
            string member,
            string type,
            string lastVar,
            float epsilon)
            => TypeExprEmitter.ChangeExpr(
                member,
                type,
                lastVar,
                epsilon);

        public static string ValueExpr(string name, string type)
            => TypeExprEmitter.ValueExpr(name, type);

        internal static string EmitClass(
            string ns,
            string className,
            IReadOnlyList<TopicMember> members)
            => EmitClassCore(
                ns,
                className,
                members,
                generationType: null);

        internal static IReadOnlyList<string> GeneratedMethodNames(
            FoxRunGenerationType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            return TriggerEmitter.BuildGeneratedMethodNames(
                type.Members
                    .Select(member => member.ToTopicMember())
                    .ToList());
        }

        private static string EmitClassCore(
            string ns,
            string className,
            IReadOnlyList<TopicMember> members,
            FoxRunGenerationType generationType)
        {
            if (members == null || members.Count == 0)
                throw new ArgumentException(
                    "At least one member is required.",
                    nameof(members));
            if (members.Any(member => member == null))
                throw new ArgumentException(
                    "TopicMember cannot be null.",
                    nameof(members));

            var publishMembers = members
                .Where(member => member.Mode != FlowSubscribe)
                .ToList();
            var inputMembers = members
                .Where(
                    member =>
                        member.Mode == FlowSubscribe
                        || member.Mode == FlowPublishAndSubscribe)
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(
                    member => member.MemberName,
                    StringComparer.Ordinal)
                .ToList();
            var webSocketInputMembers = inputMembers
                .Where(member => member.GeneratesWebSocketCodec)
                .ToList();
            var legacyWebSocketInputMembers = webSocketInputMembers
                .Where(
                    member =>
                        !string.Equals(
                            member.Encoding,
                            FoxRunGenerationDescriptorConstants
                                .MessagePackEncoding,
                            StringComparison.Ordinal))
                .ToList();

            ValidateMembers(members, inputMembers);
            var topicMap =
                new Dictionary<string, List<TopicMember>>(
                    StringComparer.Ordinal);
            foreach (var member in publishMembers)
            {
                if (!topicMap.TryGetValue(
                        member.Topic,
                        out var topicMembers))
                {
                    topicMembers = new List<TopicMember>();
                    topicMap.Add(member.Topic, topicMembers);
                }

                topicMembers.Add(member);
            }

            var topics = topicMap.Keys
                .OrderBy(topic => topic, StringComparer.Ordinal)
                .ToList();
            var topicModes = topicMap.ToDictionary(
                pair => pair.Key,
                pair => TopicPolicy(pair.Value),
                StringComparer.Ordinal);
            var hasPolicy =
                publishMembers.Any(
                    member => member.Policy != PolicyFixedRate);
            var hasConditions =
                publishMembers.Any(
                    member =>
                        !string.IsNullOrWhiteSpace(member.OnlyIf));
            var hasTransactionalInput =
                MessagePackInputDispatchEmitter.HasTransactionalInput(
                    webSocketInputMembers);
            var hasTransactionalOwnedInput =
                MessagePackInputDispatchEmitter.HasTransactionalOwnedInput(
                    webSocketInputMembers);
            var hasProviderAccess =
                generationType != null
                && TransportMemberAccessEmitter
                    .EligibleMembers(generationType)
                    .Count > 0;
            var pad = string.IsNullOrEmpty(ns) ? string.Empty : "    ";
            var sb = new StringBuilder();

            ClassFrameEmitter.EmitClassFrame(
                sb,
                ns,
                className,
                topics.Count,
                hasPolicy,
                hasConditions,
                hasProviderAccess,
                webSocketInputMembers.Count > 0,
                legacyWebSocketInputMembers.Any(
                    member => member.IsStream),
                hasTransactionalInput,
                hasTransactionalOwnedInput,
                pad);
            if (topics.Count > 0)
            {
                TopicMetadataEmitter.EmitGetTopic(
                    sb,
                    topics,
                    topicMap,
                    topicModes,
                    pad);
                TopicMetadataEmitter.EmitGetContract(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    pad);
                PublishDispatchEmitter.EmitCaptureAndTargets(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    pad);
                PublishDispatchEmitter.EmitPublish(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    pad);
                MessagePackPublishDispatchEmitter.EmitFieldsAndBuilders(
                    sb,
                    topics,
                    topicMap,
                    pad);
                PublishDispatchEmitter.EmitPublishToBus(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    pad);
                PublishDispatchEmitter.EmitPublishToSinks(
                    sb,
                    ns,
                    className,
                    topics,
                    topicMap,
                    pad);
                ConditionEmitter.EmitConditions(
                    sb,
                    topics,
                    topicMap,
                    pad);
            }

            InputDispatchEmitter.EmitInput(
                sb,
                ns,
                className,
                legacyWebSocketInputMembers,
                topics,
                pad,
                hasTransactionalInput);
            MessagePackInputDispatchEmitter.EmitInput(
                sb,
                webSocketInputMembers,
                topics,
                pad);
            TriggerEmitter.EmitApplyMethods(
                sb,
                TriggerEmitter.BuildApplyMembers(inputMembers),
                legacyWebSocketInputMembers,
                webSocketInputMembers,
                pad);
            TriggerEmitter.EmitPublishMethods(
                sb,
                TriggerEmitter.BuildPublishMembers(
                    publishMembers,
                    topics),
                pad);
            TransportMemberAccessEmitter.Emit(
                sb,
                generationType,
                pad);
            if (topics.Count > 0)
                PolicyEmitter.EmitPolicy(
                    sb,
                    topics,
                    topicMap,
                    topicModes,
                    pad);

            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns))
                sb.AppendLine("}");
            return sb.ToString();
        }

        private static void ValidateMembers(
            IReadOnlyList<TopicMember> members,
            IReadOnlyList<TopicMember> inputMembers)
        {
            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member.MemberName))
                    throw new ArgumentException(
                        "TopicMember has empty MemberName.",
                        nameof(members));
                if (string.IsNullOrWhiteSpace(member.TypeName))
                    throw new ArgumentException(
                        "TopicMember has empty TypeName.",
                        nameof(members));
                if (string.IsNullOrWhiteSpace(member.Topic))
                    throw new ArgumentException(
                        "TopicMember has empty Topic.",
                        nameof(members));
            }

            foreach (var member in inputMembers)
            {
                if (member.IsAggregateMember)
                    throw new ArgumentException(
                        "Aggregate input members are not supported.",
                        nameof(members));
            }
        }

        private static int TopicPolicy(
            IReadOnlyList<TopicMember> fields)
        {
            if (fields.Any(field => field.Policy == PolicyTrigger))
                return PolicyTrigger;
            if (fields.Any(field => field.Policy == PolicyChange))
                return PolicyChange;
            return PolicyFixedRate;
        }

        internal static string DefaultJsonFieldName(string memberName)
        {
            var name =
                memberName != null
                && memberName.StartsWith(
                    "@",
                    StringComparison.Ordinal)
                    ? memberName.Substring(1)
                    : memberName ?? string.Empty;
            return name.TrimStart('_');
        }
    }
}
