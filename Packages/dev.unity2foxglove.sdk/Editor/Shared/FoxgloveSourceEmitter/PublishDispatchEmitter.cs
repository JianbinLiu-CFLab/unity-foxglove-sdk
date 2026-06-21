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
    /// Emits the <c>FoxgloveLog_Publish</c> dispatch method that builds a
    /// JSON payload dictionary from member values and calls
    /// <c>FoxgloveManager.PublishJson</c> for each topic index.
    /// </summary>
    internal static class PublishDispatchEmitter
    {
        /// <summary>
        /// Emits the <c>IFoxgloveLogSource.FoxgloveLog_Publish</c> implementation
        /// that switches on topic index and emits a
        /// <c>FoxgloveManager.PublishJson</c> call for each topic.
        /// </summary>
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
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}            case {i}: mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __BuildFoxRunJson_{i}(), nowNs); break;");
                }
                else
                {
                    sb.AppendLine($"{pad}            case {i}: mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields)}, nowNs); break;");
                }
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            EmitAggregateJsonWriters(sb, topics, topicMap, pad);
        }

        /// <summary>
        /// Emits the optional local-bus publish side-channel. The generated
        /// method checks for subscribers before building the payload, so the
        /// existing live path does not allocate extra dictionaries when no
        /// local consumers are attached.
        /// </summary>
        internal static void EmitPublishToBus(StringBuilder sb, string ns, string className, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            var origin = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveTopicBusSource.FoxgloveLog_PublishToBus(int topicIndex, FoxTopicBus bus, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (bus == null)");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine($"{pad}            case {i}:");
                sb.AppendLine($"{pad}                if (!bus.HasSubscribers(\"{topic}\")) break;");
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __payload = __BuildFoxRunJson_{i}();");
                    sb.AppendLine($"{pad}                bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __payload, \"{StringLiteralEmitter.CSharpStringLiteral(origin)}\");");
                }
                else
                {
                    sb.AppendLine($"{pad}                bus.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, {PayloadExpr(fields)}, \"{StringLiteralEmitter.CSharpStringLiteral(origin)}\");");
                }
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static string PayloadExpr(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var jsonNames = fields.Select(f => f.JsonFieldName).ToList();
            var dict = new StringBuilder("new Dictionary<string, object> { ");
            for (int j = 0; j < fields.Count; j++)
            {
                if (j > 0) dict.Append(", ");
                dict.Append($"[\"{StringLiteralEmitter.CSharpStringLiteral(jsonNames[j])}\"] = {TypeExprEmitter.ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
            }
            dict.Append(" }");
            return dict.ToString();
        }

        private static void EmitAggregateJsonWriters(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            var emittedAny = false;
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                if (!IsAggregateTopic(fields))
                    continue;

                EnsurePureAggregateTopic(fields, topics[i]);
                emittedAny = true;
                sb.AppendLine();
                sb.AppendLine($"{pad}    private byte[] __BuildFoxRunJson_{i}()");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        var __json = new global::System.Text.StringBuilder(128);");
                sb.AppendLine($"{pad}        __WriteFoxRunJson_{i}(__json);");
                sb.AppendLine($"{pad}        return global::System.Text.Encoding.UTF8.GetBytes(__json.ToString());");
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine();
                sb.AppendLine($"{pad}    private void __WriteFoxRunJson_{i}(global::System.Text.StringBuilder __json)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        __json.Append('{{');");
                for (int j = 0; j < fields.Count; j++)
                {
                    var separator = j == 0 ? string.Empty : ",";
                    sb.AppendLine($"{pad}        __json.Append(\"{separator}\\\"{StringLiteralEmitter.CSharpStringLiteral(fields[j].JsonFieldName)}\\\":\");");
                    EmitJsonValueAppend(sb, fields[j], pad + "        ");
                }
                sb.AppendLine($"{pad}        __json.Append('}}');");
                sb.AppendLine($"{pad}    }}");
            }

            if (!emittedAny)
                return;

            sb.AppendLine();
            sb.AppendLine($"{pad}    private static void __AppendFoxRunJsonString(global::System.Text.StringBuilder __json, string value)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (value == null)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            __json.Append(\"null\");");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        __json.Append('\\\"');");
            sb.AppendLine($"{pad}        for (int __i = 0; __i < value.Length; __i++)");
            sb.AppendLine($"{pad}        {{");
            sb.AppendLine($"{pad}            var __c = value[__i];");
            sb.AppendLine($"{pad}            switch (__c)");
            sb.AppendLine($"{pad}            {{");
            sb.AppendLine($"{pad}                case '\\\"': __json.Append(\"\\\\\\\"\"); break;");
            sb.AppendLine($"{pad}                case '\\\\': __json.Append(\"\\\\\\\\\"); break;");
            sb.AppendLine($"{pad}                case '\\b': __json.Append(\"\\\\b\"); break;");
            sb.AppendLine($"{pad}                case '\\f': __json.Append(\"\\\\f\"); break;");
            sb.AppendLine($"{pad}                case '\\n': __json.Append(\"\\\\n\"); break;");
            sb.AppendLine($"{pad}                case '\\r': __json.Append(\"\\\\r\"); break;");
            sb.AppendLine($"{pad}                case '\\t': __json.Append(\"\\\\t\"); break;");
            sb.AppendLine($"{pad}                default:");
            sb.AppendLine($"{pad}                    if (__c < ' ')");
            sb.AppendLine($"{pad}                        __json.Append(\"\\\\u\").Append(((int)__c).ToString(\"x4\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}                    else");
            sb.AppendLine($"{pad}                        __json.Append(__c);");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        __json.Append('\\\"');");
            sb.AppendLine($"{pad}    }}");
        }

        private static void EmitJsonValueAppend(StringBuilder sb, FoxgloveSourceEmitter.TopicMember field, string pad)
        {
            var type = NormalizeType(field.TypeName);
            var access = TypeExprEmitter.MemberAccess(field.MemberName);
            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
                    sb.AppendLine($"{pad}__json.Append({access} ? \"true\" : \"false\");");
                    break;
                case "string":
                case "String":
                case "System.String":
                    sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access});");
                    break;
                case "float":
                case "Single":
                case "System.Single":
                    sb.AppendLine($"{pad}if (float.IsNaN({access}) || float.IsInfinity({access})) __json.Append(\"null\"); else __json.Append({access}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "double":
                case "Double":
                case "System.Double":
                    sb.AppendLine($"{pad}if (double.IsNaN({access}) || double.IsInfinity({access})) __json.Append(\"null\"); else __json.Append({access}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "Vector2":
                    AppendVector(sb, pad, access, "x", "y");
                    break;
                case "Vector3":
                    AppendVector(sb, pad, access, "x", "y", "z");
                    break;
                case "Quaternion":
                    AppendVector(sb, pad, access, "x", "y", "z", "w");
                    break;
                case "Color":
                    AppendVector(sb, pad, access, "r", "g", "b", "a");
                    break;
                default:
                    if (IsIntegralType(type))
                        sb.AppendLine($"{pad}__json.Append({access}.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    else
                        sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access} == null ? null : {access}.ToString());");
                    break;
            }
        }

        private static void AppendVector(StringBuilder sb, string pad, string access, params string[] fields)
        {
            sb.AppendLine($"{pad}__json.Append('{{');");
            for (int i = 0; i < fields.Length; i++)
            {
                var separator = i == 0 ? string.Empty : ",";
                var field = fields[i];
                sb.AppendLine($"{pad}__json.Append(\"{separator}\\\"{field}\\\":\");");
                sb.AppendLine($"{pad}if (float.IsNaN({access}.{field}) || float.IsInfinity({access}.{field})) __json.Append(\"null\"); else __json.Append({access}.{field}.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            }
            sb.AppendLine($"{pad}__json.Append('}}');");
        }

        private static bool IsAggregateTopic(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields.Any(field => field.IsAggregateMember);

        private static void EnsurePureAggregateTopic(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields, string topic)
        {
            if (fields.Any(field => field.IsAggregateMember) && fields.Any(field => !field.IsAggregateMember))
            {
                throw new System.InvalidOperationException(
                    "FoxRun aggregate topic cannot mix aggregate and field-level members: " + topic);
            }
        }

        private static string NormalizeType(string typeName)
        {
            var type = typeName ?? string.Empty;
            return type.StartsWith("UnityEngine.", System.StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
        }

        private static bool IsIntegralType(string type)
        {
            switch (type)
            {
                case "byte":
                case "Byte":
                case "System.Byte":
                case "sbyte":
                case "SByte":
                case "System.SByte":
                case "short":
                case "Int16":
                case "System.Int16":
                case "ushort":
                case "UInt16":
                case "System.UInt16":
                case "int":
                case "Int32":
                case "System.Int32":
                case "uint":
                case "UInt32":
                case "System.UInt32":
                case "long":
                case "Int64":
                case "System.Int64":
                case "ulong":
                case "UInt64":
                case "System.UInt64":
                    return true;
                default:
                    return false;
            }
        }
    }
}
