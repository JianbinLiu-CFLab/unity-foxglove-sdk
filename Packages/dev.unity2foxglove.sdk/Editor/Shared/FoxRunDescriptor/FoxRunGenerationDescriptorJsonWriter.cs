// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Deterministic JSON writer for FoxRun generation-model descriptors.

using System;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunGenerationDescriptorJsonWriter
    {
        public static string Write(FoxRunGenerationModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var sb = new StringBuilder();
            sb.Append('{');
            WriteName(sb, "descriptorVersion");
            sb.Append(model.DescriptorVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteName(sb, "generatorVersion");
            WriteString(sb, model.GeneratorVersion);
            sb.Append(',');
            WriteName(sb, "types");
            sb.Append('[');
            for (var i = 0; i < model.Types.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteType(sb, model.Types[i]);
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteType(StringBuilder sb, FoxRunGenerationType type)
        {
            sb.Append('{');
            WriteName(sb, "namespace");
            WriteString(sb, type.Namespace);
            sb.Append(',');
            WriteName(sb, "className");
            WriteString(sb, type.ClassName);
            sb.Append(',');
            WriteName(sb, "declaringType");
            WriteString(sb, type.DeclaringType);
            sb.Append(',');
            WriteName(sb, "members");
            sb.Append('[');
            for (var i = 0; i < type.Members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteMember(sb, type.Members[i]);
            }
            sb.Append(']');
            sb.Append('}');
        }

        private static void WriteMember(StringBuilder sb, FoxRunGenerationMember member)
        {
            sb.Append('{');
            WriteStringField(sb, "memberName", member.MemberName);
            sb.Append(',');
            WriteStringField(sb, "memberKind", member.MemberKind);
            sb.Append(',');
            WriteStringField(sb, "rawTypeName", member.RawTypeName);
            sb.Append(',');
            WriteStringField(sb, "emissionTypeName", member.EmissionTypeName);
            sb.Append(',');
            WriteStringField(sb, "canonicalType", member.CanonicalType);
            sb.Append(',');
            WriteStringField(sb, "topic", member.Topic);
            sb.Append(',');
            WriteStringField(sb, "schemaName", member.SchemaName);
            sb.Append(',');
            WriteStringField(sb, "encoding", member.Encoding);
            sb.Append(',');
            WriteName(sb, "rateHz");
            WriteFloat(sb, member.RateHz);
            sb.Append(',');
            WriteStringField(sb, "publishMode", member.PublishModeName);
            sb.Append(',');
            WriteName(sb, "changeEpsilon");
            WriteFloat(sb, member.ChangeEpsilon);
            sb.Append(',');
            WriteName(sb, "forceIntervalSeconds");
            WriteFloat(sb, member.ForceIntervalSeconds);
            sb.Append(',');
            WriteStringField(sb, "hostKind", member.HostKind);
            sb.Append(',');
            WriteName(sb, "rawMemberOrder");
            sb.Append(member.RawMemberOrder.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            WriteStringField(sb, "conditionalSymbols", member.ConditionalSymbols);
            sb.Append('}');
        }

        private static void WriteStringField(StringBuilder sb, string name, string value)
        {
            WriteName(sb, name);
            WriteString(sb, value);
        }

        private static void WriteName(StringBuilder sb, string name)
        {
            WriteString(sb, name);
            sb.Append(':');
        }

        private static void WriteFloat(StringBuilder sb, float value)
        {
            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
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
                            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
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
