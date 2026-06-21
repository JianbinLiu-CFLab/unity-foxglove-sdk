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
            {
                sb.Append("{\"type\":\"array\",\"items\":");
                AppendScalarOrObjectShape(sb, field.Type);
                sb.Append('}');
                return;
            }

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
                case "System.Boolean":
                    sb.Append("{\"type\":\"boolean\"}");
                    break;
                case "string":
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
                    sb.Append("{\"type\":\"integer\"}");
                    break;
                case "float":
                case "double":
                case "System.Single":
                case "System.Double":
                    sb.Append("{\"type\":\"number\"}");
                    break;
                case "UnityEngine.Vector2":
                case "Vector2":
                    AppendNumberObject(sb, "x", "y");
                    break;
                case "UnityEngine.Vector3":
                case "Vector3":
                    AppendNumberObject(sb, "x", "y", "z");
                    break;
                case "UnityEngine.Quaternion":
                case "Quaternion":
                    AppendNumberObject(sb, "x", "y", "z", "w");
                    break;
                case "UnityEngine.Color":
                case "Color":
                    AppendNumberObject(sb, "r", "g", "b", "a");
                    break;
                default:
                    sb.Append("{\"type\":\"string\"}");
                    break;
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
                sb.Append(":{\"type\":\"number\"}");
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
                        if (c < ' ')
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
