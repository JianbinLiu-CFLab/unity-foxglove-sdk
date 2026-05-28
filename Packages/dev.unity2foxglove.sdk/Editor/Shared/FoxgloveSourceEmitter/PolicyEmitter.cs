// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class PolicyEmitter
    {
        internal static void EmitPolicy(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, Dictionary<string, int> topicModes, string pad)
        {
            var hasPolicy = topicModes.Values.Any(m => m != 0);
            if (!hasPolicy)
                return;

            sb.AppendLine();
            // Last-value storage per topic
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 0 || mode == 3) continue;
                sb.AppendLine($"{pad}    private bool __hasLast_{i};");
                sb.AppendLine($"{pad}    private double __lastPublishSec_{i};");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}    private {fields[j].TypeName} __last_{i}_{j};");
            }
            sb.AppendLine();

            // ShouldPublish
            sb.AppendLine($"{pad}    bool IFoxgloveLogPolicySource.FoxgloveLog_ShouldPublish(int topicIndex, double nowSec)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        bool changed;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 0)
                {
                    sb.AppendLine($"{pad}            case {i}: return true;");
                    continue;
                }
                if (mode == 3)
                {
                    sb.AppendLine($"{pad}            case {i}: return false;");
                    continue;
                }
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                changed = !__hasLast_{i};");
                for (int j = 0; j < fields.Count; j++)
                {
                    var f = fields[j];
                    var eps = f.ChangeEpsilon;
                    sb.AppendLine($"{pad}                if (!changed) changed = {TypeExprEmitter.ChangeExpr(f.MemberName, f.TypeName, "__last_" + i + "_" + j, eps)};");
                }
                var forceInt = fields.Max(f => f.ForceIntervalSeconds);
                sb.AppendLine($"{pad}                return Unity.FoxgloveSDK.Util.FoxRunPublishPolicy.ShouldPublish(" +
                    $"{TopicMetadataEmitter.PublishModeLiteral(mode)}, nowSec, __hasLast_{i}, changed, __lastPublishSec_{i}, {TypeExprEmitter.FloatLiteral(forceInt < 0 ? 0 : forceInt)});");
            }
            sb.AppendLine($"{pad}            default: return true;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();

            // MarkPublished
            sb.AppendLine($"{pad}    void IFoxgloveLogPolicySource.FoxgloveLog_MarkPublished(int topicIndex, double nowSec)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 0 || mode == 3) continue;
                sb.AppendLine($"{pad}            case {i}:");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}                __last_{i}_{j} = {TypeExprEmitter.MemberAccess(fields[j].MemberName)};");
                sb.AppendLine($"{pad}                __hasLast_{i} = true;");
                sb.AppendLine($"{pad}                __lastPublishSec_{i} = nowSec;");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }
    }
}
