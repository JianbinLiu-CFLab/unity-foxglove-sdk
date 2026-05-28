// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static class PublishDispatchEmitter
    {
        internal static void EmitPublish(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveLogSource.FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var schema = StringLiteralEmitter.CSharpStringLiteral(fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "");
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine($"{pad}            case {i}: mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields)}, nowNs); break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static string PayloadExpr(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var jsonNames = fields.Select(f => JsonFieldName(f.MemberName)).ToList();
            var dict = new StringBuilder("new Dictionary<string, object> { ");
            for (int j = 0; j < fields.Count; j++)
            {
                if (j > 0) dict.Append(", ");
                dict.Append($"[\"{StringLiteralEmitter.CSharpStringLiteral(jsonNames[j])}\"] = {TypeExprEmitter.ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
            }
            dict.Append(" }");
            return dict.ToString();
        }

        private static string JsonFieldName(string memberName)
        {
            var name = memberName != null && memberName.StartsWith("@", System.StringComparison.Ordinal)
                ? memberName.Substring(1)
                : memberName ?? "";
            return name.TrimStart('_');
        }
    }
}
