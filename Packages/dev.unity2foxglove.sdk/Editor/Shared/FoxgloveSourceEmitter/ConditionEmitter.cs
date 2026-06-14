// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Emits per-topic conditional publish gates for FoxRun members with
    /// <c>When</c> or <c>Unless</c> attribute settings.
    /// </summary>
    internal static class ConditionEmitter
    {
        internal static void EmitConditions(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            var hasConditions = topicMap.Values
                .SelectMany(fields => fields)
                .Any(field => !string.IsNullOrWhiteSpace(field.When) || !string.IsNullOrWhiteSpace(field.Unless));
            if (!hasConditions)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxgloveLogConditionSource.FoxgloveLog_CanPublish(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");

            for (var i = 0; i < topics.Count; i++)
            {
                var condition = ConditionExpression(topicMap[topics[i]]);
                sb.AppendLine($"{pad}            case {i}: return {condition};");
            }

            sb.AppendLine($"{pad}            default: return true;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static string ConditionExpression(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> members)
        {
            var parts = new List<string>();
            foreach (var member in members)
            {
                if (!string.IsNullOrWhiteSpace(member.When))
                    parts.Add(ConditionAccess(member.When));
                if (!string.IsNullOrWhiteSpace(member.Unless))
                    parts.Add("!" + ConditionAccess(member.Unless));
            }

            return parts.Count == 0 ? "true" : string.Join(" && ", parts);
        }

        private static string ConditionAccess(string conditionName)
        {
            var name = (conditionName ?? string.Empty).Trim();
            if (name.EndsWith("()", System.StringComparison.Ordinal))
                return IdentifierUtils.EscapeIdentifier(name.Substring(0, name.Length - 2)) + "()";
            return IdentifierUtils.EscapeIdentifier(name);
        }
    }
}
