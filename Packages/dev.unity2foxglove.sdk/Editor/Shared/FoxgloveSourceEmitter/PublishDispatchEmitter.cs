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
        internal static void EmitPublish(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            string pad)
        {
            var declaringType = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveLogSource.FoxgloveLog_Publish(int topicIndex, FoxgloveManager mgr, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var rawSchema = fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "";
                var schema = StringLiteralEmitter.CSharpStringLiteral(rawSchema);
                var protobufSchema = StringLiteralEmitter.CSharpStringLiteral(
                    FoxRunProtobufContractBuilder.ResolveMessageFullName(rawSchema, declaringType, topics[i]));
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                var suppressRemoteEcho = fields.Any(field => field.Mode == 2);
                var protobuf = string.Equals(
                    TopicMetadataEmitter.EffectiveEncoding(fields),
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    System.StringComparison.Ordinal);
                var inherited = TopicMetadataEmitter.IsInherited(fields);
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}            case {i}:");
                    if (suppressRemoteEcho)
                    {
                        sb.AppendLine($"{pad}                if (__foxRunSuppressNextPublish_{i})");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    __foxRunSuppressNextPublish_{i} = false;");
                        sb.AppendLine($"{pad}                    break;");
                        sb.AppendLine($"{pad}                }}");
                    }
                    if (inherited)
                    {
                        sb.AppendLine($"{pad}                if (mgr.ResolveFoxRunWireEncoding(FoxRunWireEncoding.Inherit, FoxRunMode.PublishOnly) == FoxRunWireEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    var __payload_{i} = __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                    __foxRunLastJson_{i} = __payload_{i};");
                        sb.AppendLine($"{pad}                    mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __payload_{i}, nowNs);");
                        sb.AppendLine($"{pad}                }}");
                    }
                    else if (protobuf)
                    {
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    }
                    else
                    {
                        sb.AppendLine($"{pad}                var __payload_{i} = __BuildFoxRunJson_{i}();");
                        sb.AppendLine($"{pad}                __foxRunLastJson_{i} = __payload_{i};");
                        sb.AppendLine($"{pad}                mgr.PublishFoxRunJsonBytes(\"{topic}\", \"{schema}\", __payload_{i}, nowNs);");
                    }
                    sb.AppendLine($"{pad}                break;");
                }
                else
                {
                    sb.AppendLine($"{pad}            case {i}:");
                    if (suppressRemoteEcho)
                    {
                        sb.AppendLine($"{pad}                if (__foxRunSuppressNextPublish_{i})");
                        sb.AppendLine($"{pad}                {{");
                        sb.AppendLine($"{pad}                    __foxRunSuppressNextPublish_{i} = false;");
                        sb.AppendLine($"{pad}                    break;");
                        sb.AppendLine($"{pad}                }}");
                    }
                    if (inherited)
                    {
                        sb.AppendLine($"{pad}                if (mgr.ResolveFoxRunWireEncoding(FoxRunWireEncoding.Inherit, FoxRunMode.PublishOnly) == FoxRunWireEncoding.Protobuf)");
                        sb.AppendLine($"{pad}                    mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                        sb.AppendLine($"{pad}                else");
                        sb.AppendLine($"{pad}                    mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields)}, nowNs);");
                    }
                    else if (protobuf)
                        sb.AppendLine($"{pad}                mgr.PublishProto(\"{topic}\", \"{protobufSchema}\", __BuildFoxRunProtobuf_{i}(), nowNs);");
                    else
                        sb.AppendLine($"{pad}                mgr.PublishJson(\"{topic}\", \"{schema}\", {PayloadExpr(fields)}, nowNs);");
                    sb.AppendLine($"{pad}                break;");
                }
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");

            EmitAggregateJsonWriters(sb, topics, topicMap, pad);
            ProtobufPublishDispatchEmitter.EmitBuilders(sb, declaringType, topics, topicMap, pad);
        }

        /// <summary>
        /// Emits the optional local-bus publish side-channel. The generated
        /// method checks for subscribers before building the payload, so the
        /// existing live path does not allocate extra dictionaries when no
        /// local consumers are attached.
        /// </summary>
        internal static void EmitPublishToBus(
            StringBuilder sb,
            string ns,
            string className,
            IReadOnlyList<string> topics,
            Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap,
            IReadOnlyDictionary<string, FoxgloveSourceEmitter.TopicMember> nativeBusMembers,
            string pad)
        {
            var origin = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            if (nativeBusMembers != null && nativeBusMembers.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{pad}    [Preserve]");
                sb.AppendLine($"{pad}    bool IFoxgloveTopicBusDemandSource.FoxgloveLog_HasBusSubscribers(int topicIndex, FoxTopicBus bus)");
                sb.AppendLine($"{pad}    {{");
                sb.AppendLine($"{pad}        if (bus == null)");
                sb.AppendLine($"{pad}            return false;");
                sb.AppendLine($"{pad}        switch (topicIndex)");
                sb.AppendLine($"{pad}        {{");
                for (int i = 0; i < topics.Count; i++)
                {
                    if (!nativeBusMembers.ContainsKey(topics[i]))
                        continue;
                    var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                    sb.AppendLine($"{pad}            case {i}: return bus.HasSubscribers(\"{topic}\");");
                }
                sb.AppendLine($"{pad}            default: return false;");
                sb.AppendLine($"{pad}        }}");
                sb.AppendLine($"{pad}    }}");
            }
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
                if (nativeBusMembers != null && nativeBusMembers.TryGetValue(topics[i], out var customMember))
                {
                    var dtoType = GlobalTypeName(customMember.TypeName);
                    var access = TypeExprEmitter.MemberAccess(customMember.MemberName);
                    sb.AppendLine($"{pad}                var __foxRunNativePayload_{i} = {access};");
                    sb.AppendLine($"{pad}                bus.Publish<{dtoType}>(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, in __foxRunNativePayload_{i}, \"{StringLiteralEmitter.CSharpStringLiteral(origin)}\");");
                }
                else if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __payload = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
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

        /// <summary>
        /// Emits the optional additive sink fanout side-channel. Aggregate topics
        /// reuse the JSON bytes built for the primary live/MCAP publish path.
        /// Legacy field-level topics still keep their primary <c>PublishJson</c>
        /// path, while the side-channel builds equivalent JSON bytes only when a
        /// sink is attached.
        /// </summary>
        internal static void EmitPublishToSinks(StringBuilder sb, string ns, string className, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            var origin = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine();
            sb.AppendLine($"{pad}    [Preserve]");
            sb.AppendLine($"{pad}    void IFoxgloveTopicSinkSource.FoxgloveLog_PublishToSinks(int topicIndex, FoxTopicSinkRouter router, ulong nowNs)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if (router == null || !router.HasSinks)");
            sb.AppendLine($"{pad}            return;");
            sb.AppendLine($"{pad}        switch (topicIndex)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                sb.AppendLine($"{pad}            case {i}:");
                if (IsAggregateTopic(fields))
                {
                    EnsurePureAggregateTopic(fields, topics[i]);
                    sb.AppendLine($"{pad}                var __sink_{i} = __foxRunLastJson_{i} ?? __BuildFoxRunJson_{i}();");
                    sb.AppendLine($"{pad}                __foxRunLastJson_{i} = null;");
                }
                else
                {
                    sb.AppendLine($"{pad}                var __sink_{i} = __BuildFoxRunJson_{i}();");
                }
                sb.AppendLine($"{pad}                router.Publish(((IFoxgloveTopicContractSource)this).FoxgloveLog_GetContract({i}), nowNs, __sink_{i}, \"{StringLiteralEmitter.CSharpStringLiteral(origin)}\");");
                sb.AppendLine($"{pad}                break;");
            }
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
        }

        private static string PayloadExpr(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var dict = new StringBuilder("new Dictionary<string, object> { ");
            for (int j = 0; j < fields.Count; j++)
            {
                if (j > 0) dict.Append(", ");
                dict.Append($"[\"{StringLiteralEmitter.CSharpStringLiteral(fields[j].JsonFieldName)}\"] = {TypeExprEmitter.ValueExpr(fields[j].MemberName, fields[j].TypeName)}");
            }
            dict.Append(" }");
            return dict.ToString();
        }

        private static string GlobalTypeName(string typeName)
            => string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("global::", System.StringComparison.Ordinal)
                ? typeName
                : "global::" + typeName;

        private static void EmitAggregateJsonWriters(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                if (fields.Any(field => field.Mode == 2))
                {
                    sb.AppendLine();
                    sb.AppendLine($"{pad}    private bool __foxRunSuppressNextPublish_{i};");
                }
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
                    EmitJsonValueAppend(sb, fields[j], j, pad + "        ");
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

        private static void EmitJsonValueAppend(StringBuilder sb, FoxgloveSourceEmitter.TopicMember field, int fieldIndex, string pad)
        {
            var type = NormalizeType(field.TypeName);
            var access = TypeExprEmitter.MemberAccess(field.MemberName);
            if (TryGetCollectionElementType(type, out var elementType, out var countProperty))
            {
                EmitCollectionJsonValueAppend(sb, elementType, countProperty, access, fieldIndex, pad);
                return;
            }

            EmitScalarOrObjectJsonValueAppend(sb, type, access, pad);
        }

        private static void EmitCollectionJsonValueAppend(
            StringBuilder sb,
            string elementType,
            string countProperty,
            string access,
            int fieldIndex,
            string pad)
        {
            var indexName = "__foxRunIndex_" + fieldIndex;
            sb.AppendLine($"{pad}if ({access} == null)");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    __json.Append(\"null\");");
            sb.AppendLine($"{pad}}}");
            sb.AppendLine($"{pad}else");
            sb.AppendLine($"{pad}{{");
            sb.AppendLine($"{pad}    __json.Append('[');");
            sb.AppendLine($"{pad}    for (int {indexName} = 0; {indexName} < {access}.{countProperty}; {indexName}++)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        if ({indexName} > 0) __json.Append(',');");
            EmitScalarOrObjectJsonValueAppend(sb, elementType, access + "[" + indexName + "]", pad + "        ");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine($"{pad}    __json.Append(']');");
            sb.AppendLine($"{pad}}}");
        }

        private static void EmitScalarOrObjectJsonValueAppend(StringBuilder sb, string type, string access, string pad)
        {
            if (TryUnwrapNullableType(type, out var nullableType))
            {
                EmitNullableJsonValueAppend(sb, nullableType, access, pad);
                return;
            }

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
                case "Vector4":
                    AppendVector(sb, pad, access, "x", "y", "z", "w");
                    break;
                case "Color32":
                    AppendColor32(sb, pad, access);
                    break;
                default:
                    if (IsIntegralType(type))
                        sb.AppendLine($"{pad}__json.Append({access}.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    else
                        sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access} == null ? null : {access}.ToString());");
                    break;
            }
        }

        private static void EmitNullableJsonValueAppend(StringBuilder sb, string type, string access, string pad)
        {
            switch (type)
            {
                case "bool":
                case "Boolean":
                case "System.Boolean":
                    sb.AppendLine($"{pad}if ({access} == null) __json.Append(\"null\"); else __json.Append({access}.Value ? \"true\" : \"false\");");
                    break;
                case "float":
                case "Single":
                case "System.Single":
                    sb.AppendLine($"{pad}if ({access} == null || float.IsNaN({access}.Value) || float.IsInfinity({access}.Value)) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                case "double":
                case "Double":
                case "System.Double":
                    sb.AppendLine($"{pad}if ({access} == null || double.IsNaN({access}.Value) || double.IsInfinity({access}.Value)) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
                    break;
                default:
                    if (IsIntegralType(type))
                        sb.AppendLine($"{pad}if ({access} == null) __json.Append(\"null\"); else __json.Append({access}.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture));");
                    else
                        sb.AppendLine($"{pad}__AppendFoxRunJsonString(__json, {access} == null ? null : {access}.Value.ToString());");
                    break;
            }
        }

        private static bool TryGetCollectionElementType(string type, out string elementType, out string countProperty)
        {
            elementType = string.Empty;
            countProperty = string.Empty;

            if (type.EndsWith("[]", System.StringComparison.Ordinal))
            {
                elementType = NormalizeType(type.Substring(0, type.Length - 2));
                countProperty = "Length";
                return true;
            }

            const string listPrefix = "List<";
            const string genericListPrefix = "System.Collections.Generic.List<";
            const string iListPrefix = "IList<";
            const string genericIListPrefix = "System.Collections.Generic.IList<";
            const string readOnlyListPrefix = "IReadOnlyList<";
            const string genericReadOnlyListPrefix = "System.Collections.Generic.IReadOnlyList<";

            if (TryGetSingleGenericArgument(type, listPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, iListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericIListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, readOnlyListPrefix, out elementType)
                || TryGetSingleGenericArgument(type, genericReadOnlyListPrefix, out elementType))
            {
                countProperty = "Count";
                return true;
            }

            return false;
        }

        private static bool TryGetSingleGenericArgument(string type, string prefix, out string argument)
        {
            argument = string.Empty;
            if (!type.StartsWith(prefix, System.StringComparison.Ordinal) || !type.EndsWith(">", System.StringComparison.Ordinal))
                return false;

            argument = NormalizeType(type.Substring(prefix.Length, type.Length - prefix.Length - 1).Trim());
            return argument.IndexOf(',') < 0;
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

        private static void AppendColor32(StringBuilder sb, string pad, string access)
        {
            sb.AppendLine($"{pad}__json.Append('{{');");
            sb.AppendLine($"{pad}__json.Append(\"\\\"r\\\":\").Append(((float){access}.r / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"g\\\":\").Append(((float){access}.g / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"b\\\":\").Append(((float){access}.b / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
            sb.AppendLine($"{pad}__json.Append(\",\\\"a\\\":\").Append(((float){access}.a / 255f).ToString(\"R\", global::System.Globalization.CultureInfo.InvariantCulture));");
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

        private static bool TryUnwrapNullableType(string type, out string innerType)
        {
            innerType = string.Empty;
            type = (type ?? string.Empty).Trim();
            if (type.EndsWith("?", System.StringComparison.Ordinal))
            {
                innerType = NormalizeType(type.Substring(0, type.Length - 1).Trim());
                return innerType.Length > 0;
            }

            return TryGetSingleGenericArgument(type, "Nullable<", out innerType)
                   || TryGetSingleGenericArgument(type, "System.Nullable<", out innerType);
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
