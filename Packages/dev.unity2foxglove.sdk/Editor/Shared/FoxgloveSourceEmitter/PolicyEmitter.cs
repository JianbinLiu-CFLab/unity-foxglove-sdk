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
    /// Emits the <c>ShouldPublish</c> and <c>MarkPublished</c> interface
    /// implementation methods for FoxRun partial classes with change-detection
    /// or heartbeat-based publish policies.
    /// </summary>
    internal static class PolicyEmitter
    {
        /// <summary>
        /// Emits last-value storage fields and the <c>IFoxgloveLogPolicySource</c>
        /// implementation (<c>ShouldPublish</c> and <c>MarkPublished</c>) for
        /// topics that use Change publish mode.
        /// </summary>
        internal static void EmitPolicy(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, Dictionary<string, int> topicModes, string pad)
        {
            var hasPolicy = topicModes.Values.Any(m => m != 1);
            if (!hasPolicy)
                return;

            sb.AppendLine();
            // Last-value storage per topic. The recording snapshot is kept
            // separately from the live-publish snapshot so a hidden MCAP
            // write can be acknowledged while a selected live Provider is
            // still pending.
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 1 || mode == 4) continue;
                sb.AppendLine($"{pad}    private bool __hasLast_{i};");
                sb.AppendLine($"{pad}    private double __lastPublishSec_{i};");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}    private {IdentifierUtils.EscapeTypeName(fields[j].TypeName)} __last_{i}_{j};");
                sb.AppendLine($"{pad}    private bool __hasRecorded_{i};");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}    private {IdentifierUtils.EscapeTypeName(fields[j].TypeName)} __lastRecorded_{i}_{j};");
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
                if (mode == 1)
                {
                    sb.AppendLine($"{pad}            case {i}: return true;");
                    continue;
                }
                if (mode == 4)
                {
                    sb.AppendLine($"{pad}            case {i}: return false;");
                    continue;
                }
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                changed = !__hasLast_{i};");
                for (int j = 0; j < fields.Count; j++)
                {
                    var f = fields[j];
                    var tolerance = f.Tolerance;
                    sb.AppendLine($"{pad}                if (!changed) changed = {TypeExprEmitter.ChangeExpr(f.MemberName, f.TypeName, "__last_" + i + "_" + j, tolerance)};");
                }
                var heartbeatInterval = fields
                    .Where(f => f.HasExplicitHz && f.Hz > 0f)
                    .Select(f => 1f / f.Hz)
                    .DefaultIfEmpty(0f)
                    .Min();
                sb.AppendLine($"{pad}                return Unity.FoxgloveSDK.Util.FoxRunUpdatePolicy.ShouldPublish(" +
                    $"{TopicMetadataEmitter.PolicyLiteral(mode)}, nowSec, __hasLast_{i}, changed, __lastPublishSec_{i}, {TypeExprEmitter.FloatLiteral(heartbeatInterval)});");
            }
            sb.AppendLine($"{pad}            default: return false;");
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
                if (mode == 1 || mode == 4) continue;
                sb.AppendLine($"{pad}            case {i}:");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}                __last_{i}_{j} = {TypeExprEmitter.MemberAccess(fields[j].MemberName)};");
                sb.AppendLine($"{pad}                __hasLast_{i} = true;");
                sb.AppendLine($"{pad}                __hasRecorded_{i} = false;");
                sb.AppendLine($"{pad}                __lastPublishSec_{i} = nowSec;");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}            default: break;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            // ShouldRecord
            sb.AppendLine();
            sb.AppendLine($"{pad}    bool IFoxglovePublishRecordingPolicySource.FoxgloveLog_ShouldRecord(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        bool changed;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 1 || mode == 4)
                {
                    sb.AppendLine($"{pad}            case {i}: return true;");
                    continue;
                }

                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                changed = !__hasRecorded_{i};");
                for (int j = 0; j < fields.Count; j++)
                {
                    var f = fields[j];
                    sb.AppendLine($"{pad}                if (!changed) changed = {TypeExprEmitter.ChangeExpr(f.MemberName, f.TypeName, "__lastRecorded_" + i + "_" + j, f.Tolerance)};");
                }
                sb.AppendLine($"{pad}                return changed;");
            }
            sb.AppendLine($"{pad}            default: return false;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            // MarkRecorded
            sb.AppendLine();
            sb.AppendLine($"{pad}    void IFoxglovePublishRecordingPolicySource.FoxgloveLog_MarkRecorded(int topicIndex)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var mode = topicModes[topics[i]];
                if (mode == 1 || mode == 4)
                    continue;

                sb.AppendLine($"{pad}            case {i}:");
                for (int j = 0; j < fields.Count; j++)
                    sb.AppendLine($"{pad}                __lastRecorded_{i}_{j} = {TypeExprEmitter.MemberAccess(fields[j].MemberName)};");
                sb.AppendLine($"{pad}                __hasRecorded_{i} = true;");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}            default: break;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }
    }
}
