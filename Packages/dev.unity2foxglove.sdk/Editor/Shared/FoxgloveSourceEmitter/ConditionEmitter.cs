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
    /// <c>OnlyIf</c> attribute settings.
    /// </summary>
    internal static class ConditionEmitter
    {
        internal static void EmitConditions(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            var hasConditions = topicMap.Values
                .SelectMany(fields => fields)
                .Any(field => !string.IsNullOrWhiteSpace(field.OnlyIf));
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
                if (!string.IsNullOrWhiteSpace(member.OnlyIf))
                    parts.Add(ConditionAccess(
                        member.OnlyIf,
                        member.ConditionMemberKind));
            }

            return parts.Count == 0 ? "true" : string.Join(" && ", parts);
        }

        internal static string ConditionAccess(
            string conditionName,
            FoxRunConditionMemberKind memberKind)
        {
            var name = (conditionName ?? string.Empty).Trim();
            var access = IdentifierUtils.EscapeIdentifier(name);
            return memberKind == FoxRunConditionMemberKind.Method
                ? access + "()"
                : access;
        }
    }
}
