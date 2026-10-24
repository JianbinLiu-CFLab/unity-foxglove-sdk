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
        private static void EmitJsonValueAppend(
            StringBuilder sb,
            FoxgloveSourceEmitter.TopicMember field,
            int topicIndex,
            int fieldIndex,
            string pad)
        {
            var type = NormalizeType(field.TypeName);
            var access = $"__foxRunCapture_{topicIndex}_{fieldIndex}";
            var valueShape = field.TypeShape?.Kind == FoxRunTypeShapeKind.Collection
                ? field.TypeShape.ElementShape
                : field.TypeShape;
            if (valueShape != null
                && (valueShape.Kind == FoxRunTypeShapeKind.Object
                    || valueShape.Kind == FoxRunTypeShapeKind.Enum)
                && !UsesBuiltInJsonCodec(valueShape))
            {
                sb.AppendLine(
                    $"{pad}global::Unity.FoxgloveSDK.Components.FoxRunInboundJson.AppendObject(__json, {access});");
                return;
            }
            if (TryGetCollectionElementType(type, out var elementType, out var countProperty))
            {
                EmitCollectionJsonValueAppend(sb, elementType, countProperty, access, fieldIndex, pad);
                return;
            }

            EmitScalarOrObjectJsonValueAppend(sb, type, access, pad);
        }

        private static bool UsesBuiltInJsonCodec(FoxRunTypeShape shape)
        {
            var typeName = shape?.TypeName ?? string.Empty;
            return string.Equals(typeName, "UnityEngine.Vector2", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Vector3", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Quaternion", System.StringComparison.Ordinal)
                   || string.Equals(typeName, "UnityEngine.Color", System.StringComparison.Ordinal);
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
