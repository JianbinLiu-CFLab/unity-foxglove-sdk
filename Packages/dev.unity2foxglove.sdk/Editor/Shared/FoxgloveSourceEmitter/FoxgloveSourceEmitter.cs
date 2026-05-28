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
            /// <summary>Topic string from <c>[FoxRun("/topic")]</c>.</summary>
            public readonly string Topic;
            /// <summary>Publishing rate in Hz.</summary>
            public readonly float RateHz;
            /// <summary>Optional schema name.</summary>
            public readonly string SchemaName;
            /// <summary>Publish mode from the attribute.</summary>
            public readonly int PublishMode;
            /// <summary>Change epsilon for numeric comparison.</summary>
            public readonly float ChangeEpsilon;
            /// <summary>Heartbeat interval for OnChangeOrInterval.</summary>
            public readonly float ForceIntervalSeconds;

            /// <summary>
            /// Creates a topic-member descriptor for the shared emitter.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName)
                : this(memberName, typeName, topic, rateHz, schemaName, 0, 0f, 0f) { }

            /// <summary>
            /// Creates a topic-member descriptor with publish policy.
            /// </summary>
            public TopicMember(string memberName, string typeName, string topic, float rateHz, string schemaName,
                int publishMode, float changeEpsilon, float forceIntervalSeconds)
            {
                MemberName = memberName;
                TypeName = typeName;
                Topic = topic;
                RateHz = rateHz;
                SchemaName = schemaName;
                PublishMode = publishMode;
                ChangeEpsilon = changeEpsilon;
                ForceIntervalSeconds = forceIntervalSeconds;
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
        /// Emits the generated partial class source for one class name / namespace
        /// pair.
        /// </summary>
        /// <param name="ns">Containing namespace (empty for global).</param>
        /// <param name="className">Declaring class name.</param>
        /// <param name="members">All <c>[FoxRun]</c> attributed members of this class.</param>
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

        internal static string EmitClass(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            return EmitClassCore(ns, className, members);
        }

        private static string EmitClassCore(string ns, string className, IReadOnlyList<TopicMember> members)
        {
            if (members == null || members.Count == 0)
                throw new ArgumentException("At least one member is required.", nameof(members));

            var topicMap = new Dictionary<string, List<TopicMember>>();
            foreach (var m in members)
            {
                if (!topicMap.TryGetValue(m.Topic, out var list))
                    topicMap[m.Topic] = list = new List<TopicMember>();
                list.Add(m);
            }

            var topics = topicMap.Keys.ToList();
            var topicModes = topicMap.ToDictionary(kvp => kvp.Key, kvp => TopicPublishMode(kvp.Value));
            var hasPolicy = members.Any(m => m.PublishMode != 0);
            var pad = string.IsNullOrEmpty(ns) ? "" : "    ";
            var sb = new StringBuilder();

            ClassFrameEmitter.EmitClassFrame(sb, ns, className, topics.Count, hasPolicy, pad);
            TopicMetadataEmitter.EmitGetTopic(sb, topics, topicMap, topicModes, pad);
            PublishDispatchEmitter.EmitPublish(sb, topics, topicMap, pad);

            var triggerMembers = TriggerEmitter.BuildTriggerMembers(members, topics, topicModes);
            TriggerEmitter.EmitTriggers(sb, triggerMembers, topics, topicModes, pad);

            PolicyEmitter.EmitPolicy(sb, topics, topicMap, topicModes, pad);

            sb.AppendLine($"{pad}}}");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");

            return sb.ToString();
        }

        private static int TopicPublishMode(IReadOnlyList<TopicMember> fields)
        {
            if (fields.Any(f => f.PublishMode == 3))
                return 3;
            if (fields.Any(f => f.PublishMode == 2))
                return 2;
            if (fields.Any(f => f.PublishMode == 1))
                return 1;
            return 0;
        }
    }
}
