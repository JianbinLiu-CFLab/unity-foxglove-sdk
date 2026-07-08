// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceSchema
// Purpose: Deterministic JSON schema preview emitter for generated FoxService descriptors.

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceSchemaEmitter
    {
        public static string Emit(FoxServiceSchemaModel schema)
        {
            var sb = new StringBuilder();
            WriteSchema(sb, schema ?? FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>()));
            return sb.ToString();
        }

        private static void WriteSchema(StringBuilder sb, FoxServiceSchemaModel schema)
        {
            if (string.IsNullOrWhiteSpace(schema.JsonType))
                throw new ArgumentException("FoxServiceSchemaModel.JsonType must be non-empty.", nameof(schema));

            sb.Append('{');
            WriteJsonProperty(sb, "type", schema.JsonType);

            if (schema.Element != null)
            {
                sb.Append(',');
                WriteJsonName(sb, "items");
                sb.Append(':');
                WriteSchema(sb, schema.Element);
            }

            if (schema.AdditionalProperties != null)
            {
                sb.Append(',');
                WriteJsonName(sb, "additionalProperties");
                sb.Append(':');
                WriteSchema(sb, schema.AdditionalProperties);
            }

            if (schema.Properties.Count > 0)
            {
                sb.Append(',');
                WriteJsonName(sb, "properties");
                sb.Append(":{");
                for (var i = 0; i < schema.Properties.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    var property = schema.Properties[i];
                    WriteJsonName(sb, property.Name);
                    sb.Append(':');
                    WriteSchema(sb, property.Schema);
                }
                sb.Append('}');
            }

            sb.Append('}');
        }

        private static void WriteJsonProperty(StringBuilder sb, string name, string value)
        {
            WriteJsonName(sb, name);
            sb.Append(':');
            WriteJsonString(sb, value);
        }

        private static void WriteJsonName(StringBuilder sb, string name)
            => WriteJsonString(sb, name);

        private static void WriteJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var ch in value ?? string.Empty)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
