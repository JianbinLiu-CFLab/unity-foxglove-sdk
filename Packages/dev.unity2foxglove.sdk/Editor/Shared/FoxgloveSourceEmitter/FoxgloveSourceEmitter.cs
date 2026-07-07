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
        private const int PublishModeFixedRate = 0;
        private const int PublishModeOnChange = 1;
        private const int PublishModeOnChangeOrInterval = 2;
        private const int PublishModeOnTrigger = 3;

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
            public readonly float RateHz;
            /// <summary>Optional schema name.</summary>
            public readonly string SchemaName;
            /// <summary>Publish mode from the attribute.</summary>
            public readonly int PublishMode;
            /// <summary>FoxRun data-flow mode from the attribute.</summary>
            public readonly int Mode;
            /// <summary>Change epsilon for numeric comparison.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval for OnChangeOrInterval.</summary>
            public readonly float ForceIntervalSeconds;
            /// <summary>Optional bool member that must be true to publish.</summary>
            public readonly string When;
            /// <summary>Optional bool member that must be false to publish.</summary>
            public readonly string Unless;
            /// <summary>True when the member belongs to a class-level FoxRun aggregate message.</summary>
            public readonly bool IsAggregateMember;
            /// <summary>JSON property name emitted for aggregate and dictionary payloads.</summary>
            public readonly string JsonFieldName;

            /// <summary>
            /// Creates a topic-member descriptor for the shared emitter.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName)
                : this(memberName, typeName, topic, rateHz, schemaName, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic-member descriptor with publish policy.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName,
                int publishMode, float changeEpsilon, float forceIntervalSeconds, string when = "", string unless = "",
                bool isAggregateMember = false, string jsonFieldName = "", int mode = 0, string canonicalType = "")
            {
                MemberName = memberName;
                TypeName = typeName;
                CanonicalType = string.IsNullOrWhiteSpace(canonicalType)
                    ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(typeName)
                    : FoxRunCanonicalTypeNormalizer.NormalizeTypeName(canonicalType);
                Topic = topic;
                RateHz = rateHz;
                SchemaName = schemaName;
                PublishMode = publishMode;
                Mode = mode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
                When = when ?? string.Empty;
                Unless = unless ?? string.Empty;
                IsAggregateMember = isAggregateMember;
                JsonFieldName = string.IsNullOrWhiteSpace(jsonFieldName)
                    ? DefaultJsonFieldName(memberName)
                    : jsonFieldName;
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
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return EmitClassCore(type.Namespace, type.ClassName, type.Members.Select(member => member.ToTopicMember()).ToList());
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
            return EmitClassCore(ns, className, members);
        }

        private static string EmitClassCore(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            if (members == null || members.Count == 0)
                throw new ArgumentException("At least one member is required.", nameof(members));

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] == null)
                    throw new ArgumentException("TopicMember cannot be null.", nameof(members));
            }

            var publishMembers = members
                .Where(member => member.Mode != 1)
                .ToList();
            var inputMembers = members
                .Where(member => member.Mode == 1 || member.Mode == 2)
                .OrderBy(member => member.Topic, StringComparer.Ordinal)
                .ThenBy(member => member.MemberName, StringComparer.Ordinal)
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
            var topicModes = topicMap.ToDictionary(kvp => kvp.Key, kvp => TopicPublishMode(kvp.Value));
            var hasPolicy = publishMembers.Any(m => m.PublishMode != 0);
            var hasConditions = publishMembers.Any(m => !string.IsNullOrWhiteSpace(m.When) || !string.IsNullOrWhiteSpace(m.Unless));
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            ClassFrameEmitter.EmitClassFrame(
                sb,
                ns,
                className,
                topics.Count,
                hasPolicy,
                hasConditions,
                inputMembers.Count > 0,
                pad);
            if (topics.Count > 0)
            {
                TopicMetadataEmitter.EmitGetTopic(sb, topics, topicMap, topicModes, pad);
                TopicMetadataEmitter.EmitGetContract(sb, ns, className, topics, topicMap, pad);
                PublishDispatchEmitter.EmitPublish(sb, topics, topicMap, pad);
                PublishDispatchEmitter.EmitPublishToBus(sb, ns, className, topics, topicMap, pad);
                PublishDispatchEmitter.EmitPublishToSinks(sb, ns, className, topics, topicMap, pad);
                ConditionEmitter.EmitConditions(sb, topics, topicMap, pad);
            }
            InputDispatchEmitter.EmitInput(sb, inputMembers, topics, pad);

            var triggerMembers = TriggerEmitter.BuildTriggerMembers(publishMembers, topics, topicModes);
            TriggerEmitter.EmitTriggers(sb, triggerMembers, topics, topicModes, pad);

            if (topics.Count > 0)
                PolicyEmitter.EmitPolicy(sb, topics, topicMap, topicModes, pad);

            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");

            return sb.ToString();
        }

        private static int TopicPublishMode(IReadOnlyList<TopicMember> fields)
        {
            if (fields.Any(f => f.PublishMode == PublishModeOnTrigger))
                return PublishModeOnTrigger;
            if (fields.Any(f => f.PublishMode == PublishModeOnChangeOrInterval))
                return PublishModeOnChangeOrInterval;
            if (fields.Any(f => f.PublishMode == PublishModeOnChange))
                return PublishModeOnChange;
            return PublishModeFixedRate;
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
