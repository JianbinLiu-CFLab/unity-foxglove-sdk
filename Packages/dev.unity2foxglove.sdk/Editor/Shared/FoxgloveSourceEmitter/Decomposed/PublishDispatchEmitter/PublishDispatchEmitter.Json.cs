// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter
// Purpose: Phase192 partial source section for PublishDispatchEmitter.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    internal static partial class PublishDispatchEmitter
    {
        private static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            return string.IsNullOrWhiteSpace(escaped)
                       || escaped.StartsWith("global::", System.StringComparison.Ordinal)
                ? escaped
                : "global::" + escaped;
        }

        private static string CaptureTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            if (string.IsNullOrWhiteSpace(escaped) || escaped.StartsWith("global::", System.StringComparison.Ordinal))
                return escaped;
            if (escaped.EndsWith("[]", System.StringComparison.Ordinal))
                return CaptureTypeName(escaped.Substring(0, escaped.Length - 2)) + "[]";
            if (escaped.EndsWith("?", System.StringComparison.Ordinal))
                return CaptureTypeName(escaped.Substring(0, escaped.Length - 1)) + "?";
            switch (escaped)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "char":
                case "object":
                case "void":
                case "dynamic":
                case "nint":
                case "nuint":
                    return escaped;
                default:
                    return "global::" + escaped;
            }
        }

        private static string CapturedValueExpr(int topicIndex, int fieldIndex, string type)
        {
            var access = $"__foxRunCapture_{topicIndex}_{fieldIndex}";
            var normalized = type != null && type.StartsWith("UnityEngine.", System.StringComparison.Ordinal)
                ? type.Substring("UnityEngine.".Length)
                : type;
            switch (normalized)
            {
                case "Vector3": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z }}";
                case "Vector2": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y }}";
                case "Quaternion": return $"new Dictionary<string, object> {{ [\"x\"] = {access}.x, [\"y\"] = {access}.y, [\"z\"] = {access}.z, [\"w\"] = {access}.w }}";
                case "Color": return $"new Dictionary<string, object> {{ [\"r\"] = {access}.r, [\"g\"] = {access}.g, [\"b\"] = {access}.b, [\"a\"] = {access}.a }}";
                default: return access;
            }
        }

        internal static bool NeedsStructuralOriginSnapshot(
            IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => fields.Any(field => !SupportsDirectOriginSnapshot(field));

        private static bool SupportsDirectOriginSnapshot(
            FoxgloveSourceEmitter.TopicMember field)
        {
            if (field?.TypeShape?.Kind == FoxRunTypeShapeKind.Enum)
                return true;
            var type = NormalizeType((field?.TypeName ?? string.Empty).Trim());
            if (type.EndsWith("?", System.StringComparison.Ordinal))
                type = type.Substring(0, type.Length - 1);
            if (type.StartsWith("System.Nullable<", System.StringComparison.Ordinal)
                || type.StartsWith("Nullable<", System.StringComparison.Ordinal))
                return true;
            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
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
                case "float":
                case "Single":
                case "System.Single":
                case "double":
                case "Double":
                case "System.Double":
                case "decimal":
                case "Decimal":
                case "System.Decimal":
                case "char":
                case "Char":
                case "System.Char":
                case "string":
                case "String":
                case "System.String":
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                case "Color":
                case "Color32":
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitAggregateJsonWriters(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                if (MessagePackPublishDispatchEmitter.UsesMessagePack(fields))
                    continue;
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine();
                    sb.AppendLine($"{pad}    private byte[] __foxRunLastJson_{i};");
                }

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
                    EmitJsonValueAppend(sb, fields[j], i, j, pad + "        ");
                }
                sb.AppendLine($"{pad}        __json.Append('}}');");
                sb.AppendLine($"{pad}    }}");
            }

            if (topics.Count == 0)
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
            sb.AppendLine($"{pad}                    if (__c < ' ' || global::System.Char.IsSurrogate(__c))");
            sb.AppendLine($"{pad}                        __json.Append(\"\\\\u\").Append(((int)__c).ToString(\"x4\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}                    else");
            sb.AppendLine($"{pad}                        __json.Append(__c);");
            sb.AppendLine($"{pad}                    break;");
            sb.AppendLine($"{pad}            }}");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}        __json.Append('\\\"');");
            sb.AppendLine($"{pad}    }}");
        }
    }
}
