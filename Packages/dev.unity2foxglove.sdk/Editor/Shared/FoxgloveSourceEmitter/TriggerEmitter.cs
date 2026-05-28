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
    /// Builds and emits trigger methods (per-member and <c>FoxRun_TriggerAll</c>)
    /// for FoxRun partial classes that have OnTrigger-mode topics.
    /// </summary>
    internal static class TriggerEmitter
    {
        /// <summary>
        /// Describes a single trigger method: its name and the set of topic
        /// indexes it publishes.
        /// </summary>
        internal sealed class TriggerMember
        {
            public readonly string MethodName;
            public readonly List<int> TopicIndexes;

            /// <summary>
            /// Creates a <see cref="TriggerMember"/> with the given method name and
            /// topic index list.
            /// </summary>
            public TriggerMember(string methodName, List<int> topicIndexes)
            {
                MethodName = methodName;
                TopicIndexes = topicIndexes;
            }
        }

        /// <summary>
        /// Groups OnTrigger-mode members by origin member name and produces a
        /// list of <see cref="TriggerMember"/> descriptors with deduplicated
        /// method names.
        /// </summary>
        internal static List<TriggerMember> BuildTriggerMembers(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members,
            IReadOnlyList<string> topics,
            IReadOnlyDictionary<string, int> topicModes)
        {
            var usedNames = new HashSet<string>();
            var result = new List<TriggerMember>();

            foreach (var group in members.Where(m => m.PublishMode == 3).GroupBy(m => m.MemberName).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var topicIndexes = group
                    .Select(m => IndexOfTopic(topics, m.Topic))
                    .Where(i => i >= 0 && topicModes[topics[i]] == 3)
                    .Distinct()
                    .OrderBy(i => i)
                    .ToList();
                if (topicIndexes.Count == 0)
                    continue;

                var baseName = "FoxRun_Trigger_" + IdentifierUtils.SanitizeIdentifier(group.Key.TrimStart('_'));
                var methodName = baseName;
                var suffix = 2;
                while (!usedNames.Add(methodName))
                    methodName = baseName + "_" + suffix++;

                result.Add(new TriggerMember(methodName, topicIndexes));
            }

            return result;
        }

        /// <summary>
        /// Emits per-member trigger methods and a <c>FoxRun_TriggerAll</c> method
        /// that fire FoxgloveLogHub triggers for all OnTrigger-mode topics.
        /// </summary>
        internal static void EmitTriggers(StringBuilder sb, IReadOnlyList<TriggerMember> triggerMembers, IReadOnlyList<string> topics, Dictionary<string, int> topicModes, string pad)
        {
            if (triggerMembers.Count == 0)
                return;

            sb.AppendLine();
            foreach (var trigger in triggerMembers)
            {
                sb.AppendLine($"{pad}    public bool {trigger.MethodName}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var published = false;");
                foreach (var topicIndex in trigger.TopicIndexes)
                    sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
                sb.AppendLine($"{pad}        return published;");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
            }

            var triggerTopicIndexes = topics
                .Select((topic, index) => new { topic, index })
                .Where(x => topicModes[x.topic] == 3)
                .Select(x => x.index)
                .ToList();

            sb.AppendLine($"{pad}    public bool FoxRun_TriggerAll()");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        var published = false;");
            foreach (var topicIndex in triggerTopicIndexes)
                sb.AppendLine($"{pad}        published |= FoxgloveLogHub.Trigger(this, {topicIndex});");
            sb.AppendLine($"{pad}        return published;");
            sb.AppendLine($"{pad}    }}");
        }

        private static int IndexOfTopic(IReadOnlyList<string> topics, string topic)
        {
            for (var i = 0; i < topics.Count; i++)
                if (topics[i] == topic)
                    return i;
            return -1;
        }
    }
}
