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
            if (manifest.ManifestVersion >= 2)
            {
                sb.Append(',');
                AppendPropertyName(sb, "subscriptions");
                WriteSubscriptionSection(sb, manifest.Sections.Subscriptions, includeHash: true);
            }
            sb.Append('}');
            sb.Append(',');
            AppendPropertyName(sb, "globalManifestHash");
            AppendString(sb, manifest.GlobalManifestHash);
            if (manifest.ManifestVersion >= 2)
            {
                sb.Append(',');
                AppendPropertyName(sb, "subscriptionManifestHash");
                AppendString(sb, manifest.Sections.Subscriptions.ManifestHash);
            }
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

        /// <summary>
        /// Write the canonical contract hash input. Callers should pass
        /// pre-normalized policy/contract data; non-finite policy floats are
        /// canonicalized to 0 by the policy writer for runtime-stable hashes.
        /// </summary>
        public static string WriteContractHashInput(
            string declaringType,
            string schemaName,
            string encoding,
            IReadOnlyList<FoxRunManifestField> fields,
            string flow = "Publish")
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
            if (!IsDefaultFlow(flow))
            {
                AppendPropertyName(sb, "flow");
                AppendString(sb, flow);
                sb.Append(',');
            }
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

        /// <summary>
        /// Write the canonical policy hash input. NaN and infinity policy
        /// floats are written as 0 so hash identity stays stable across
        /// Unity/Mono/.NET runtimes.
        /// </summary>
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

        public static string WriteSubscriptionSectionHashInput(
            IReadOnlyList<FoxRunManifestSubscriptionBinding> bindings)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "bindings");
            WriteSubscriptionBindings(sb, bindings ?? Array.Empty<FoxRunManifestSubscriptionBinding>());
            sb.Append('}');
            return sb.ToString();
        }

        public static string WriteGlobalHashInput(
            int manifestVersion,
            string packageName,
            FoxRunManifestGenerator generator,
            string foxRunSectionHash,
            string subscriptionSectionHash = null)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "manifestVersion");
            sb.Append(manifestVersion.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "package");
            AppendString(sb, packageName);
            sb.Append(',');
            AppendPropertyName(sb, "generator");
            WriteGenerator(sb, generator);
            sb.Append(',');
            AppendPropertyName(sb, "sections");
            sb.Append('{');
            AppendPropertyName(sb, "foxrun");
            AppendString(sb, foxRunSectionHash);
            if (manifestVersion >= 2 && subscriptionSectionHash != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "subscriptions");
                AppendString(sb, subscriptionSectionHash);
            }
            sb.Append('}');
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

        private static void WriteSubscriptionSection(
            StringBuilder sb,
            FoxRunManifestSubscriptionSection section,
            bool includeHash)
        {
            sb.Append('{');
            if (includeHash)
            {
                AppendPropertyName(sb, "manifestHash");
                AppendString(sb, section.ManifestHash);
                sb.Append(',');
            }
            AppendPropertyName(sb, "bindings");
            WriteSubscriptionBindings(sb, section.Bindings);
            sb.Append('}');
        }

        private static void WriteSubscriptionBindings(
            StringBuilder sb,
            IReadOnlyList<FoxRunManifestSubscriptionBinding> bindings)
        {
            sb.Append('[');
            for (var index = 0; index < bindings.Count; index++)
            {
                if (index > 0)
                    sb.Append(',');
                WriteSubscriptionBinding(sb, bindings[index]);
            }
            sb.Append(']');
        }

        private static void WriteSubscriptionBinding(
            StringBuilder sb,
            FoxRunManifestSubscriptionBinding binding)
        {
            sb.Append('{');
            AppendPropertyName(sb, "declaringType");
            AppendString(sb, binding.DeclaringType);
            sb.Append(',');
            AppendPropertyName(sb, "memberName");
            AppendString(sb, binding.MemberName);
            sb.Append(',');
            AppendPropertyName(sb, "topic");
            AppendString(sb, binding.Topic);
            sb.Append(',');
            AppendPropertyName(sb, "flow");
            AppendString(sb, binding.Flow);
            sb.Append(',');
            AppendPropertyName(sb, "declaredSource");
            AppendString(sb, binding.DeclaredSource);
            sb.Append(',');
            AppendPropertyName(sb, "declaredTargets");
            AppendString(sb, binding.DeclaredTargets);
            sb.Append(',');
            AppendPropertyName(sb, "qosProfile");
            AppendString(sb, binding.QosProfile);
            sb.Append(',');
            AppendPropertyName(sb, "qosReliability");
            AppendString(sb, binding.QosReliability);
            sb.Append(',');
            AppendPropertyName(sb, "qosDurability");
            AppendString(sb, binding.QosDurability);
            sb.Append(',');
            AppendPropertyName(sb, "qosHistory");
            AppendString(sb, binding.QosHistory);
            sb.Append(',');
            AppendPropertyName(sb, "qosDepth");
            sb.Append(binding.QosDepth.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "supportsWebSocket");
            sb.Append(binding.SupportsWebSocket ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "supportsRos2Native");
            sb.Append(binding.SupportsRos2Native ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "isStream");
            sb.Append(binding.IsStream ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "nativeType");
            AppendString(sb, binding.NativeType);
            sb.Append(',');
            AppendPropertyName(sb, "canonicalRosType");
            AppendString(sb, binding.CanonicalRosType);
            sb.Append(',');
            AppendPropertyName(sb, "copyShapeIdentity");
            AppendString(sb, binding.CopyShapeIdentity);
            sb.Append(',');
            AppendPropertyName(sb, "ros2ContractKind");
            AppendString(sb, binding.Ros2ContractKind.ToString());
            sb.Append(',');
            AppendPropertyName(sb, "customDtoIdentity");
            AppendString(sb, binding.CustomDtoIdentity);
            sb.Append(',');
            AppendPropertyName(sb, "customPayloadIdentity");
            AppendString(sb, binding.CustomPayloadIdentity);
            sb.Append(',');
            AppendPropertyName(sb, "customEnvelopeIdentity");
            AppendString(sb, binding.CustomEnvelopeIdentity);
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
            if (!IsDefaultFlow(contract.Flow))
            {
                sb.Append(',');
                AppendPropertyName(sb, "flow");
                AppendString(sb, contract.Flow);
            }
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
            if ((field.ProtobufMetadata?.FieldNumber ?? 0) > 0)
            {
                sb.Append(',');
                AppendPropertyName(sb, "protobufFieldNumber");
                sb.Append(field.ProtobufMetadata.FieldNumber.ToString(CultureInfo.InvariantCulture));
            }
            if (field.TypeShape != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "protobufShape");
                WriteProtobufTypeShape(
                    sb,
                    field.TypeShape,
                    field.ProtobufMetadata?.TypeMetadata);
            }
            sb.Append('}');
        }

        private static void WriteProtobufTypeShape(
            StringBuilder sb,
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata protobufMetadata = null)
        {
            shape = LegacyProtobufValueShape(shape);
            sb.Append('{');
            AppendPropertyName(sb, "kind");
            AppendString(sb, shape.Kind.ToString());
            sb.Append(',');
            AppendPropertyName(sb, "typeName");
            AppendString(sb, shape.TypeName);
            if (!string.IsNullOrEmpty(shape.CanonicalType))
            {
                sb.Append(',');
                AppendPropertyName(sb, "canonicalType");
                AppendString(sb, shape.CanonicalType);
            }
            if (shape.Fields.Count > 0)
            {
                sb.Append(',');
                AppendPropertyName(sb, "fields");
                WriteProtobufTypeFields(sb, shape.Fields, protobufMetadata);
            }
            if (shape.EnumValues.Count > 0)
            {
                sb.Append(',');
                AppendPropertyName(sb, "enumValues");
                WriteProtobufEnumValues(sb, shape.EnumValues);
            }
            sb.Append('}');
        }

        private static void WriteProtobufTypeFields(
            StringBuilder sb,
            IReadOnlyList<FoxRunTypeField> fields,
            FoxRunProtobufTypeMetadata protobufMetadata)
        {
            var ordered = new List<FoxRunTypeField>(fields ?? Array.Empty<FoxRunTypeField>());
            ordered.Sort((left, right) => string.Compare(left.MemberName, right.MemberName, StringComparison.Ordinal));
            sb.Append('[');
            for (var index = 0; index < ordered.Count; index++)
            {
                if (index > 0)
                    sb.Append(',');
                var field = ordered[index];
                sb.Append('{');
                AppendPropertyName(sb, "jsonName");
                AppendString(sb, field.JsonName);
                sb.Append(',');
                AppendPropertyName(sb, "memberName");
                AppendString(sb, field.MemberName);
                sb.Append(',');
                AppendPropertyName(sb, "repeated");
                sb.Append(field.Repeated ? "true" : "false");
                var fieldMetadata = protobufMetadata?.Find(
                    field.MemberName,
                    field.JsonName);
                if ((fieldMetadata?.FieldNumber ?? 0) > 0)
                {
                    sb.Append(',');
                    AppendPropertyName(sb, "protobufFieldNumber");
                    sb.Append(fieldMetadata.FieldNumber.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append(',');
                AppendPropertyName(sb, "shape");
                WriteProtobufTypeShape(
                    sb,
                    field.TypeShape,
                    fieldMetadata?.TypeMetadata);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static FoxRunTypeShape LegacyProtobufValueShape(FoxRunTypeShape shape)
        {
            while (shape != null && shape.Kind == FoxRunTypeShapeKind.Collection)
                shape = shape.ElementShape;
            return FoxRunProtobufTypeShapeProjection.ProjectValue(shape);
        }

        private static void WriteProtobufEnumValues(StringBuilder sb, IReadOnlyList<FoxRunEnumValue> values)
        {
            var ordered = new List<FoxRunEnumValue>(values ?? Array.Empty<FoxRunEnumValue>());
            if (!ordered.Exists(value => value.Number == 0))
                ordered.Add(new FoxRunEnumValue("UNSPECIFIED", 0));
            ordered.Sort((left, right) =>
            {
                var byNumber = left.Number.CompareTo(right.Number);
                return byNumber != 0 ? byNumber : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            });
            sb.Append('[');
            for (var index = 0; index < ordered.Count; index++)
            {
                if (index > 0)
                    sb.Append(',');
                sb.Append('{');
                AppendPropertyName(sb, "name");
                AppendString(sb, ordered[index].Name);
                sb.Append(',');
                AppendPropertyName(sb, "number");
                sb.Append(ordered[index].Number.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void WritePolicy(StringBuilder sb, FoxRunManifestPolicy policy)
        {
            sb.Append('{');
            AppendPropertyName(sb, "mode");
            AppendString(sb, policy.Mode);
            sb.Append(',');
            AppendPropertyName(sb, "hz");
            AppendFloat(sb, policy.Hz);
            sb.Append(',');
            AppendPropertyName(sb, "tolerance");
            AppendFloat(sb, policy.Tolerance);
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

        /// <summary>
        /// Append a canonical manifest float. Non-finite values are written as
        /// 0 because JSON has no NaN/Infinity literal and manifest hash input
        /// must stay deterministic across runtimes.
        /// </summary>
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

        private static bool IsDefaultFlow(string flow)
            => string.IsNullOrWhiteSpace(flow)
               || string.Equals(flow, "Publish", StringComparison.Ordinal);
    }
}
