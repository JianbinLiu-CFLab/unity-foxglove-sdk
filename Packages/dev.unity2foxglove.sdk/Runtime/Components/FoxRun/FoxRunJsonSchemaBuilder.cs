// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Builds JSON Schema text for generated FoxRun aggregate contracts.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Builds deterministic JSON Schema text for generated FoxRun contracts.</summary>
    internal static class FoxRunJsonSchemaBuilder
    {
        public static string Build(FoxRunSchemaContractInfo contract)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            var sb = new StringBuilder(256);
            sb.Append("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{");
            for (int i = 0; i < contract.Fields.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                var field = contract.Fields[i];
                AppendString(sb, field.JsonName);
                sb.Append(':');
                AppendShape(sb, field);
            }
            sb.Append("},\"required\":[");
            for (int i = 0; i < contract.Fields.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                AppendString(sb, contract.Fields[i].JsonName);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendShape(StringBuilder sb, FoxRunSchemaFieldInfo field)
        {
            if (field.Array)
                throw new InvalidOperationException("FoxRun aggregate array fields are not supported by generated JSON schema inference: " + field.JsonName);

            if (field.Nullable)
            {
                sb.Append("{\"anyOf\":[");
                AppendScalarOrObjectShape(sb, field.Type);
                sb.Append(",{\"type\":\"null\"}]}");
                return;
            }

            AppendScalarOrObjectShape(sb, field.Type);
        }

        private static void AppendScalarOrObjectShape(StringBuilder sb, string type)
        {
            switch (NormalizeType(type))
            {
                case "bool":
                case "boolean":
                case "Boolean":
                case "System.Boolean":
                    sb.Append("{\"type\":\"boolean\"}");
                    break;
                case "string":
                case "String":
                case "System.String":
                    sb.Append("{\"type\":\"string\"}");
                    break;
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "System.Byte":
                case "System.SByte":
                case "System.Int16":
                case "System.UInt16":
                case "System.Int32":
                case "System.UInt32":
                case "System.Int64":
                case "System.UInt64":
                case "uint8":
                case "int8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                    sb.Append("{\"type\":\"integer\"}");
                    break;
                case "float":
                case "double":
                case "decimal":
                case "System.Single":
                case "System.Double":
                case "System.Decimal":
                case "float32":
                case "float64":
                    AppendNullableNumber(sb);
                    break;
                case "UnityEngine.Vector2":
                case "Vector2":
                case "unity.vector2.float32":
                    AppendNumberObject(sb, "x", "y");
                    break;
                case "UnityEngine.Vector3":
                case "Vector3":
                case "unity.vector3.float32":
                    AppendNumberObject(sb, "x", "y", "z");
                    break;
                case "UnityEngine.Quaternion":
                case "Quaternion":
                case "unity.quaternion.float32":
                    AppendNumberObject(sb, "x", "y", "z", "w");
                    break;
                case "UnityEngine.Color":
                case "Color":
                case "unity.color.float32":
                    AppendNumberObject(sb, "r", "g", "b", "a");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported FoxRun aggregate schema field type: " + type);
            }
        }

        private static void AppendNumberObject(StringBuilder sb, params string[] names)
        {
            sb.Append("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                AppendString(sb, names[i]);
                sb.Append(':');
                AppendNullableNumber(sb);
            }
            sb.Append("},\"required\":[");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                AppendString(sb, names[i]);
            }
            sb.Append("]}");
        }

        private static void AppendNullableNumber(StringBuilder sb)
        {
            // JSON has no NaN/Infinity literal. FoxRun aggregate floating-point
            // fields allow null as the sentinel for non-finite runtime values,
            // even when the source field itself is not nullable.
            sb.Append("{\"anyOf\":[{\"type\":\"number\"},{\"type\":\"null\"}]}");
        }

        private static string NormalizeType(string type)
            => type ?? string.Empty;

        private static void AppendString(StringBuilder sb, string value)
        {
            sb.Append('"');
            value = value ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || char.IsSurrogate(c))
                            sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
