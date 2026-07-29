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
            string flow = "Publish",
            string logicalSchemaName = "",
            bool publishAvailable = true,
            bool subscribeAvailable = true,
            string unavailableDiagnosticId = "",
            string unavailableReason = "",
            string publishUnavailableDiagnosticId = "",
            string publishUnavailableReason = "",
            string subscribeUnavailableDiagnosticId = "",
            string subscribeUnavailableReason = "")
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPropertyName(sb, "declaringType");
            AppendString(sb, declaringType);
            sb.Append(',');
            AppendPropertyName(sb, "schemaName");
            AppendString(sb, schemaName);
            sb.Append(',');
            AppendPropertyName(sb, "logicalSchemaName");
            AppendString(sb, logicalSchemaName);
            sb.Append(',');
            AppendPropertyName(sb, "encoding");
            AppendString(sb, encoding);
            sb.Append(',');
            WriteAvailability(
                sb,
                publishAvailable,
                subscribeAvailable,
                unavailableDiagnosticId,
                unavailableReason,
                publishUnavailableDiagnosticId,
                publishUnavailableReason,
                subscribeUnavailableDiagnosticId,
                subscribeUnavailableReason);
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
            string encoding,
            string flow = "")
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
            if (!string.IsNullOrWhiteSpace(flow))
            {
                sb.Append(',');
                AppendPropertyName(sb, "flow");
                AppendString(sb, flow);
            }
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
            AppendPropertyName(sb, "wireSchemaName");
            AppendString(sb, contract.WireSchemaName);
            sb.Append(',');
            AppendPropertyName(sb, "logicalSchemaName");
            AppendString(sb, contract.LogicalSchemaName);
            sb.Append(',');
            AppendPropertyName(sb, "encoding");
            AppendString(sb, contract.Encoding);
            sb.Append(',');
            AppendPropertyName(sb, "availability");
            WriteAvailability(
                sb,
                contract.PublishAvailable,
                contract.SubscribeAvailable,
                contract.UnavailableDiagnosticId,
                contract.UnavailableReason,
                contract.PublishUnavailableDiagnosticId,
                contract.PublishUnavailableReason,
                contract.SubscribeUnavailableDiagnosticId,
                contract.SubscribeUnavailableReason);
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
            if (field.ProtobufMetadata != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "protobuf");
                WriteProtobufMetadata(sb, field.ProtobufMetadata);
            }
            if (field.TypeShape != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "typeShape");
                WriteTypeShape(sb, field.TypeShape);
            }
            if (field.NormalizedSchedule != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "normalizedSchedule");
                WriteNormalizedSchedule(sb, field.NormalizedSchedule);
            }
            sb.Append('}');
        }

        private static void WriteProtobufMetadata(
            StringBuilder sb,
            FoxRunProtobufMetadata metadata)
        {
            sb.Append('{');
            AppendPropertyName(sb, "fieldNumber");
            sb.Append(metadata.FieldNumber.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "type");
            WriteProtobufTypeMetadata(sb, metadata.TypeMetadata);
            sb.Append('}');
        }

        private static void WriteProtobufTypeMetadata(
            StringBuilder sb,
            FoxRunProtobufTypeMetadata metadata)
        {
            if (metadata == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('{');
            AppendPropertyName(sb, "typeName");
            AppendString(sb, metadata.TypeName);
            sb.Append(',');
            AppendPropertyName(sb, "fields");
            sb.Append('[');
            for (var index = 0; index < metadata.Fields.Count; index++)
            {
                if (index > 0)
                    sb.Append(',');
                var field = metadata.Fields[index];
                sb.Append('{');
                AppendPropertyName(sb, "memberName");
                AppendString(sb, field.MemberName);
                sb.Append(',');
                AppendPropertyName(sb, "jsonName");
                AppendString(sb, field.JsonName);
                sb.Append(',');
                AppendPropertyName(sb, "fieldNumber");
                sb.Append(field.FieldNumber.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                AppendPropertyName(sb, "presenceOnly");
                sb.Append(field.PresenceOnly ? "true" : "false");
                sb.Append(',');
                AppendPropertyName(sb, "presenceUsesHasValue");
                sb.Append(field.PresenceUsesHasValue ? "true" : "false");
                sb.Append(',');
                AppendPropertyName(sb, "type");
                WriteProtobufTypeMetadata(sb, field.TypeMetadata);
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');
        }

        private static void WriteTypeShape(StringBuilder sb, FoxRunTypeShape shape)
        {
            sb.Append('{');
            AppendPropertyName(sb, "kind");
            AppendString(sb, shape.Kind.ToString());
            sb.Append(',');
            AppendPropertyName(sb, "typeName");
            AppendString(sb, shape.TypeName);
            sb.Append(',');
            AppendPropertyName(sb, "nullable");
            sb.Append(shape.Nullable ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "canConstruct");
            sb.Append(shape.CanConstruct ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "collectionKind");
            AppendString(sb, shape.CollectionKind.ToString());
            sb.Append(',');
            AppendPropertyName(sb, "binary");
            sb.Append(shape.IsBinary ? "true" : "false");
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
                WriteProtobufTypeFields(sb, shape.Fields);
            }
            if (shape.EnumValues.Count > 0)
            {
                sb.Append(',');
                AppendPropertyName(sb, "enumValues");
                WriteProtobufEnumValues(sb, shape.EnumValues);
            }
            if (shape.ElementShape != null)
            {
                sb.Append(',');
                AppendPropertyName(sb, "elementShape");
                WriteTypeShape(sb, shape.ElementShape);
            }
            sb.Append('}');
        }

        private static void WriteProtobufTypeFields(StringBuilder sb, IReadOnlyList<FoxRunTypeField> fields)
        {
            var ordered = new List<FoxRunTypeField>(fields ?? Array.Empty<FoxRunTypeField>());
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
                sb.Append(',');
                AppendPropertyName(sb, "collectionKind");
                AppendString(sb, field.RepeatedCollectionKind.ToString());
                sb.Append(',');
                AppendPropertyName(sb, "canAssign");
                sb.Append(field.CanAssign ? "true" : "false");
                sb.Append(',');
                AppendPropertyName(sb, "nullable");
                sb.Append(field.IsNullable ? "true" : "false");
                sb.Append(',');
                AppendPropertyName(sb, "shape");
                WriteTypeShape(sb, field.TypeShape);
                sb.Append('}');
            }
            sb.Append(']');
        }

        private static void WriteProtobufEnumValues(StringBuilder sb, IReadOnlyList<FoxRunEnumValue> values)
        {
            var ordered = new List<FoxRunEnumValue>(values ?? Array.Empty<FoxRunEnumValue>());
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

        private static void WriteAvailability(
            StringBuilder sb,
            bool publishAvailable,
            bool subscribeAvailable,
            string unavailableDiagnosticId,
            string unavailableReason,
            string publishUnavailableDiagnosticId,
            string publishUnavailableReason,
            string subscribeUnavailableDiagnosticId,
            string subscribeUnavailableReason)
        {
            sb.Append('{');
            AppendPropertyName(sb, "publishAvailable");
            sb.Append(publishAvailable ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "subscribeAvailable");
            sb.Append(subscribeAvailable ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "unavailableDiagnosticId");
            AppendString(sb, unavailableDiagnosticId);
            sb.Append(',');
            AppendPropertyName(sb, "unavailableReason");
            AppendString(sb, unavailableReason);
            if (!string.Equals(
                    publishUnavailableDiagnosticId,
                    unavailableDiagnosticId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    publishUnavailableReason,
                    unavailableReason,
                    StringComparison.Ordinal)
                || !string.Equals(
                    subscribeUnavailableDiagnosticId,
                    unavailableDiagnosticId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    subscribeUnavailableReason,
                    unavailableReason,
                    StringComparison.Ordinal))
            {
                sb.Append(',');
                AppendPropertyName(sb, "publishUnavailableDiagnosticId");
                AppendString(sb, publishUnavailableDiagnosticId);
                sb.Append(',');
                AppendPropertyName(sb, "publishUnavailableReason");
                AppendString(sb, publishUnavailableReason);
                sb.Append(',');
                AppendPropertyName(sb, "subscribeUnavailableDiagnosticId");
                AppendString(sb, subscribeUnavailableDiagnosticId);
                sb.Append(',');
                AppendPropertyName(sb, "subscribeUnavailableReason");
                AppendString(sb, subscribeUnavailableReason);
            }
            sb.Append('}');
        }

        private static void WriteNormalizedSchedule(
            StringBuilder sb,
            FoxRunNormalizedScheduleTuple schedule)
        {
            sb.Append('{');
            AppendPropertyName(sb, "policy");
            sb.Append(schedule.Policy.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendPropertyName(sb, "hasExplicitHz");
            sb.Append(schedule.HasExplicitHz ? "true" : "false");
            sb.Append(',');
            AppendPropertyName(sb, "hz");
            AppendFloat(sb, schedule.Hz);
            sb.Append(',');
            AppendPropertyName(sb, "tolerance");
            AppendFloat(sb, schedule.Tolerance);
            sb.Append(',');
            AppendPropertyName(sb, "onlyIf");
            AppendString(sb, schedule.OnlyIf);
            sb.Append(',');
            AppendPropertyName(sb, "conditionMemberKind");
            AppendString(sb, schedule.ConditionMemberKind.ToString());
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
