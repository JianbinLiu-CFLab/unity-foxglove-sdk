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

            var sb = new StringBuilder(EstimateCapacity(model));
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

        private static int EstimateCapacity(FoxRunGenerationModel model)
        {
            var typeCount = model.Types.Count;
            var memberCount = 0;
            foreach (var type in model.Types)
                memberCount += type.Members.Count;
            return 64 + typeCount * 96 + memberCount * 256;
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
            WriteName(sb, "isArray");
            sb.Append(member.IsArray ? "true" : "false");
            sb.Append(',');
            WriteStringField(sb, "elementTypeName", member.ElementTypeName);
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
            WriteStringField(sb, "mode", member.ModeName);
            sb.Append(',');
            WriteName(sb, "changeEpsilon");
            WriteFloat(sb, member.ChangeEpsilon);
            sb.Append(',');
            WriteName(sb, "forceIntervalSeconds");
            WriteFloat(sb, member.ForceIntervalSeconds);
            sb.Append(',');
            WriteStringField(sb, "when", member.When);
            sb.Append(',');
            WriteStringField(sb, "unless", member.Unless);
            sb.Append(',');
            WriteName(sb, "isAggregateMember");
            sb.Append(member.IsAggregateMember ? "true" : "false");
            sb.Append(',');
            WriteStringField(sb, "jsonFieldName", member.JsonFieldName);
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
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException(
                    "FoxRun descriptor model contains NaN or Infinity. RateHz and epsilon values must be finite. " +
                    "Check the published FoxRun members for misconfigured values.");
            }

            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            var text = value ?? string.Empty;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
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
                            WriteEscapedCodeUnit(sb, ch);
                        }
                        else if (char.IsSurrogate(ch))
                        {
                            if (!char.IsHighSurrogate(ch) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                                throw new InvalidOperationException("FoxRun descriptor strings must not contain lone surrogate code units.");

                            WriteEscapedCodeUnit(sb, ch);
                            i++;
                            WriteEscapedCodeUnit(sb, text[i]);
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

        private static void WriteEscapedCodeUnit(StringBuilder sb, char ch)
        {
            sb.Append("\\u");
            sb.Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
        }
    }
}
