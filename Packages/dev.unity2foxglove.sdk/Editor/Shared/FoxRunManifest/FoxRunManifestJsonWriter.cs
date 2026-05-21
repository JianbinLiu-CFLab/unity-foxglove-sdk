// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunManifest
// Purpose: Deterministic compact JSON writer for FoxRun canonical manifests.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunManifestJsonWriter
    {
        public static string WriteCanonical(FoxRunCanonicalManifest manifest)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "manifestVersion");
            sb.Append(manifest.ManifestVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "package");
            AppendString(sb, manifest.Package);
            sb.Append(',');
            AppendPropertyName(sb, "generator");
            WriteGenerator(sb, manifest.Generator);
            sb.Append(',');
            AppendPropertyName(sb, "sections");
            sb.Append('{');
            AppendPropertyName(sb, "foxrun");
            WriteFoxRunSection(sb, manifest.Sections.FoxRun, includeHash: true);
            sb.Append('}');
            sb.Append(',');
            AppendPropertyName(sb, "globalManifestHash");
            AppendString(sb, manifest.GlobalManifestHash);
            sb.Append('}');
            return sb.ToString();
        }

        public static string WriteReport(
            FoxRunCanonicalManifest manifest,
            string generatedAtUtc,
            IReadOnlyList<string> warnings)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "generatedAtUtc");
            AppendString(sb, generatedAtUtc ?? string.Empty);
            sb.Append(',');
            AppendPropertyName(sb, "manifestHash");
            AppendString(sb, manifest.Sections.FoxRun.ManifestHash);
            sb.Append(',');
            AppendPropertyName(sb, "globalManifestHash");
            AppendString(sb, manifest.GlobalManifestHash);
            sb.Append(',');
            AppendPropertyName(sb, "warnings");
            WriteStringArray(sb, warnings ?? Array.Empty<string>());
            sb.Append('}');
            return sb.ToString();
        }

        public static string WriteContractHashInput(
            string declaringType,
            string schemaName,
            string encoding,
            IReadOnlyList<FoxRunManifestField> fields)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "declaringType");
            AppendString(sb, declaringType);
            sb.Append(',');
            AppendPropertyName(sb, "schemaName");
            AppendString(sb, schemaName);
            sb.Append(',');
            AppendPropertyName(sb, "encoding");
            AppendString(sb, encoding);
            sb.Append(',');
            AppendPropertyName(sb, "fields");
            WriteFields(sb, fields);
            sb.Append('}');
            return sb.ToString();
        }

        public static string WriteBindingHashInput(
            string declaringType,
            string topic,
            string schemaName,
            string encoding)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "declaringType");
            AppendString(sb, declaringType);
            sb.Append(',');
            AppendPropertyName(sb, "topic");
            AppendString(sb, topic);
            sb.Append(',');
            AppendPropertyName(sb, "schemaName");
            AppendString(sb, schemaName);
            sb.Append(',');
            AppendPropertyName(sb, "encoding");
            AppendString(sb, encoding);
            sb.Append('}');
            return sb.ToString();
        }

        public static string WritePolicyHashInput(FoxRunManifestPolicy policy)
        {
            var sb = new StringBuilder();
            WritePolicy(sb, policy);
            return sb.ToString();
        }

        public static string WriteFoxRunSectionHashInput(IReadOnlyList<FoxRunManifestType> types)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "types");
            WriteTypes(sb, types);
            sb.Append('}');
            return sb.ToString();
        }

        private static void WriteGenerator(StringBuilder sb, FoxRunManifestGenerator generator)
        {
            sb.Append('{');
            AppendPropertyName(sb, "name");
            AppendString(sb, generator.Name);
            sb.Append(',');
            AppendPropertyName(sb, "majorVersion");
            sb.Append(generator.MajorVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        private static void WriteFoxRunSection(
            StringBuilder sb,
            FoxRunManifestFoxRunSection section,
            bool includeHash)
        {
            sb.Append('{');
            if (includeHash)
            {
                AppendPropertyName(sb, "manifestHash");
                AppendString(sb, section.ManifestHash);
                sb.Append(',');
            }
            AppendPropertyName(sb, "types");
            WriteTypes(sb, section.Types);
            sb.Append('}');
        }

        private static void WriteTypes(StringBuilder sb, IReadOnlyList<FoxRunManifestType> types)
        {
            sb.Append('[');
            for (var i = 0; i < types.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                WriteType(sb, types[i]);
            }
            sb.Append(']');
        }

        private static void WriteType(StringBuilder sb, FoxRunManifestType type)
        {
            sb.Append('{');
            AppendPropertyName(sb, "declaringType");
            AppendString(sb, type.DeclaringType);
            sb.Append(',');
            AppendPropertyName(sb, "contracts");
            WriteContracts(sb, type.Contracts);
            sb.Append('}');
        }

        private static void WriteContracts(StringBuilder sb, IReadOnlyList<FoxRunManifestContract> contracts)
        {
            sb.Append('[');
            for (var i = 0; i < contracts.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                WriteContract(sb, contracts[i]);
            }
            sb.Append(']');
        }

        private static void WriteContract(StringBuilder sb, FoxRunManifestContract contract)
        {
            sb.Append('{');
            AppendPropertyName(sb, "topic");
            AppendString(sb, contract.Topic);
            sb.Append(',');
            AppendPropertyName(sb, "schemaName");
            AppendString(sb, contract.SchemaName);
            sb.Append(',');
            AppendPropertyName(sb, "encoding");
            AppendString(sb, contract.Encoding);
            sb.Append(',');
            AppendPropertyName(sb, "contractHash");
            AppendString(sb, contract.ContractHash);
            sb.Append(',');
            AppendPropertyName(sb, "bindingHash");
            AppendString(sb, contract.BindingHash);
            sb.Append(',');
            AppendPropertyName(sb, "policyHash");
            AppendString(sb, contract.PolicyHash);
            sb.Append(',');
            AppendPropertyName(sb, "fields");
            WriteFields(sb, contract.Fields);
            sb.Append(',');
            AppendPropertyName(sb, "policy");
            WritePolicy(sb, contract.Policy);
            sb.Append('}');
        }

        private static void WriteFields(StringBuilder sb, IReadOnlyList<FoxRunManifestField> fields)
        {
            sb.Append('[');
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                WriteField(sb, fields[i]);
            }
            sb.Append(']');
        }

        private static void WriteField(StringBuilder sb, FoxRunManifestField field)
        {
            sb.Append('{');
            AppendPropertyName(sb, "jsonName");
            AppendString(sb, field.JsonName);
            sb.Append(',');
            AppendPropertyName(sb, "memberName");
            AppendString(sb, field.MemberName);
            sb.Append(',');
            AppendPropertyName(sb, "memberKind");
            AppendString(sb, field.MemberKind);
            sb.Append(',');
            AppendPropertyName(sb, "type");
            AppendString(sb, field.Type);
            sb.Append(',');
            AppendPropertyName(sb, "nullable");
            sb.Append(field.Nullable ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "array");
            sb.Append(field.Array ? "true" : "false");
            sb.Append('}');
        }

        private static void WritePolicy(StringBuilder sb, FoxRunManifestPolicy policy)
        {
            sb.Append('{');
            AppendPropertyName(sb, "mode");
            AppendString(sb, policy.Mode);
            sb.Append(',');
            AppendPropertyName(sb, "rateHz");
            AppendFloat(sb, policy.RateHz);
            sb.Append(',');
            AppendPropertyName(sb, "changeEpsilon");
            AppendFloat(sb, policy.ChangeEpsilon);
            sb.Append(',');
            AppendPropertyName(sb, "forceIntervalSeconds");
            AppendFloat(sb, policy.ForceIntervalSeconds);
            sb.Append('}');
        }

        private static void WriteStringArray(StringBuilder sb, IReadOnlyList<string> values)
        {
            sb.Append('[');
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                AppendString(sb, values[i]);
            }
            sb.Append(']');
        }

        private static void AppendPropertyName(StringBuilder sb, string value)
        {
            AppendString(sb, value);
            sb.Append(':');
        }

        private static void AppendString(StringBuilder sb, string value)
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
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void AppendFloat(StringBuilder sb, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }

            // Canonical identity text must stay stable across Unity/Mono/.NET runtimes.
            sb.Append(value.ToString("G9", CultureInfo.InvariantCulture));
        }
    }
}
