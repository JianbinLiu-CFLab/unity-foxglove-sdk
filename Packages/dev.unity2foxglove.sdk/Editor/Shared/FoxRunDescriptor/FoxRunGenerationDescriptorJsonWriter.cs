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
            WriteStringField(sb, "source", member.Source);
            sb.Append(',');
            WriteStringField(sb, "targets", member.Targets);
            sb.Append(',');
            WriteStringField(sb, "qosProfile", member.QosProfile);
            sb.Append(',');
            WriteStringField(sb, "qosReliability", member.QosReliability);
            sb.Append(',');
            WriteStringField(sb, "qosDurability", member.QosDurability);
            sb.Append(',');
            WriteStringField(sb, "qosHistory", member.QosHistory);
            sb.Append(',');
            WriteIntField(sb, "qosDepth", member.QosDepth);
            sb.Append(',');
            WriteName(sb, "generatesWebSocketCodec");
            sb.Append(member.GeneratesWebSocketCodec ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "generatesRos2NativeRegistration");
            sb.Append(member.GeneratesRos2NativeRegistration ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "ros2MessageShape");
            WriteRos2MessageShape(sb, member.Ros2MessageShape);
            sb.Append(',');
            WriteStringField(sb, "ros2ContractKind", member.Ros2ContractKind.ToString());
            sb.Append(',');
            WriteName(sb, "ros2CustomDtoShape");
            WriteRos2CustomDtoShape(sb, member.Ros2CustomDtoShape);
            sb.Append(',');
            WriteStringField(
                sb,
                "ros2CustomEnvelopeMessageName",
                member.Ros2ContractKind == FoxRunRos2ContractKind.CustomDto
                && !string.IsNullOrWhiteSpace(member.Ros2CustomDtoShape?.PayloadIdentity)
                    ? member.Ros2CustomDtoShape.PayloadIdentity + "Envelope"
                    : string.Empty);
            if ((member.ProtobufMetadata?.FieldNumber ?? 0) > 0)
            {
                sb.Append(',');
                WriteName(sb, "protobufFieldNumber");
                sb.Append(member.ProtobufMetadata.FieldNumber.ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(',');
            WriteName(sb, "hz");
            WriteFloat(sb, member.Hz);
            sb.Append(',');
            WriteStringField(sb, "policy", member.PolicyName);
            sb.Append(',');
            WriteStringField(sb, "mode", member.FlowName);
            sb.Append(',');
            WriteName(sb, "tolerance");
            WriteFloat(sb, member.Tolerance);
            sb.Append(',');
            WriteStringField(sb, "onlyIf", member.OnlyIf);
            sb.Append(',');
            WriteStringField(
                sb,
                "onlyIfMemberKind",
                FoxRunGenerationMember.ConditionMemberKindToName(member.ConditionMemberKind));
            sb.Append(',');
            WriteStringField(
                sb,
                "explicitArguments",
                FoxRunGenerationMember.ExplicitArgumentsToText(member.NamedArgumentPresence));
            sb.Append(',');
            WriteName(sb, "isAggregateMember");
            sb.Append(member.IsAggregateMember ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "isStream");
            sb.Append(member.IsStream ? "true" : "false");
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

        private static void WriteRos2MessageShape(StringBuilder sb, FoxRunRos2MessageShape shape)
        {
            if (shape == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('{');
            WriteStringField(sb, "fullyQualifiedTypeName", shape.FullyQualifiedTypeName);
            sb.Append(',');
            WriteStringField(sb, "canonicalRosType", shape.CanonicalRosType);
            sb.Append(',');
            WriteName(sb, "hasPublicParameterlessConstructor");
            sb.Append(shape.HasPublicParameterlessConstructor ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "implementsRos2Message");
            sb.Append(shape.ImplementsRos2Message ? "true" : "false");
            sb.Append(',');
            WriteStringField(sb, "copyShapeIdentity", shape.CopyShapeIdentity);
            sb.Append(',');
            WriteName(sb, "members");
            sb.Append('[');
            for (var i = 0; i < shape.Members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var member = shape.Members[i];
                sb.Append('{');
                WriteStringField(sb, "name", member.Name);
                sb.Append(',');
                WriteStringField(sb, "kind", member.Kind.ToString());
                sb.Append(',');
                WriteStringField(sb, "fullyQualifiedTypeName", member.FullyQualifiedTypeName);
                sb.Append(',');
                WriteStringField(sb, "sequenceElementTypeName", member.SequenceElementTypeName);
                sb.Append(',');
                WriteStringField(sb, "nestedShapeIdentity", member.NestedShapeIdentity);
                sb.Append(',');
                WriteName(sb, "nestedShape");
                WriteRos2MessageShape(sb, member.NestedShape);
                sb.Append(',');
                WriteName(sb, "canRead");
                sb.Append(member.CanRead ? "true" : "false");
                sb.Append(',');
                WriteName(sb, "canWrite");
                sb.Append(member.CanWrite ? "true" : "false");
                sb.Append(',');
                WriteStringField(sb, "sequenceRepresentation", member.SequenceRepresentation.ToString());
                sb.Append(',');
                WriteName(sb, "fixedSize");
                sb.Append(member.FixedSize.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append(',');
            WriteName(sb, "diagnostics");
            sb.Append('[');
            for (var i = 0; i < shape.Diagnostics.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, shape.Diagnostics[i]);
            }
            sb.Append(']');
            sb.Append('}');
        }

        private static void WriteRos2CustomDtoShape(StringBuilder sb, FoxRunRos2CustomDtoShape shape)
        {
            if (shape == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append('{');
            WriteStringField(sb, "fullyQualifiedTypeName", shape.FullyQualifiedTypeName);
            sb.Append(',');
            WriteStringField(sb, "canonicalIdentity", shape.CanonicalIdentity);
            sb.Append(',');
            WriteStringField(sb, "payloadIdentity", shape.PayloadIdentity);
            sb.Append(',');
            WriteName(sb, "hasPublicParameterlessConstructor");
            sb.Append(shape.HasPublicParameterlessConstructor ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "isSupported");
            sb.Append(shape.IsSupported ? "true" : "false");
            sb.Append(',');
            WriteName(sb, "members");
            sb.Append('[');
            for (var i = 0; i < shape.Members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var member = shape.Members[i];
                sb.Append('{');
                WriteStringField(sb, "name", member.Name);
                sb.Append(',');
                WriteStringField(sb, "rosFieldName", member.RosFieldName);
                sb.Append(',');
                WriteStringField(sb, "presenceFieldName", member.PresenceFieldName);
                sb.Append(',');
                WriteStringField(sb, "kind", member.Kind.ToString());
                sb.Append(',');
                WriteStringField(sb, "fullyQualifiedTypeName", member.FullyQualifiedTypeName);
                sb.Append(',');
                WriteStringField(sb, "rosType", member.RosType);
                sb.Append(',');
                WriteStringField(sb, "sequenceElementTypeName", member.SequenceElementTypeName);
                sb.Append(',');
                WriteStringField(sb, "nestedShapeIdentity", member.NestedShapeIdentity);
                sb.Append(',');
                WriteName(sb, "nestedShape");
                WriteRos2CustomDtoShape(sb, member.NestedShape);
                sb.Append(',');
                WriteName(sb, "hasPresence");
                sb.Append(member.HasPresence ? "true" : "false");
                sb.Append(',');
                WriteName(sb, "canRead");
                sb.Append(member.CanRead ? "true" : "false");
                sb.Append(',');
                WriteName(sb, "canWrite");
                sb.Append(member.CanWrite ? "true" : "false");
                sb.Append(',');
                WriteStringField(sb, "sequenceRepresentation", member.SequenceRepresentation.ToString());
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append(',');
            WriteName(sb, "diagnostics");
            sb.Append('[');
            for (var i = 0; i < shape.Diagnostics.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, shape.Diagnostics[i]);
            }
            sb.Append(']');
            sb.Append('}');
        }

        private static void WriteStringField(StringBuilder sb, string name, string value)
        {
            WriteName(sb, name);
            WriteString(sb, value);
        }

        private static void WriteIntField(StringBuilder sb, string name, int value)
        {
            WriteName(sb, name);
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
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
                    "FoxRun descriptor model contains NaN or Infinity. Hz and tolerance values must be finite. " +
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
