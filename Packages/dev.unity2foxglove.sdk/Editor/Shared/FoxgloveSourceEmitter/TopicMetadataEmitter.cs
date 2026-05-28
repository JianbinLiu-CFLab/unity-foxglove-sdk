// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class TopicMetadataEmitter
    {
        internal static void EmitGetTopic(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, Dictionary<string, int> topicModes, string pad)
        {
            sb.AppendLine($"{pad}    FoxgloveLogTopicInfo IFoxgloveLogSource.FoxgloveLog_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var rate = fields.Max(m => m.RateHz);
                var mode = topicModes[topics[i]];
                var eps = fields.Max(m => m.ChangeEpsilon);
                var forceInt = fields.Max(m => m.ForceIntervalSeconds);
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, {4}, {5}f, {6}f);",
                    pad, i, topic, rate, PublishModeLiteral(mode), eps, forceInt));
            }
            sb.AppendLine($"{pad}            default: return default;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
        }

        internal static string PublishModeLiteral(int mode)
        {
            switch (mode)
            {
                case 0: return "FoxRunPublishMode.FixedRate";
                case 1: return "FoxRunPublishMode.OnChange";
                case 2: return "FoxRunPublishMode.OnChangeOrInterval";
                case 3: return "FoxRunPublishMode.OnTrigger";
                default: return FormattableString.Invariant($"(FoxRunPublishMode){mode}");
            }
        }
    }
}
