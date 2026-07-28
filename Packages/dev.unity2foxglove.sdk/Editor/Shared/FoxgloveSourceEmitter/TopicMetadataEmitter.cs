// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Emits the <c>FoxgloveLog_GetTopic</c> switch method and provides
    /// publish-mode literal translation for generated FoxRun partial classes.
    /// </summary>
    internal static class TopicMetadataEmitter
    {
        private static readonly object Sha256Gate = new();
        // Process-lifetime generator helper: keep one SHA256 instance to support
        // Unity profiles that do not expose the newer SHA256.HashData API.
        private static readonly SHA256 SharedSha256 = SHA256.Create();

        /// <summary>
        /// Emits the <c>IFoxgloveLogSource.FoxgloveLog_GetTopic</c> switch
        /// method that returns a <c>FoxgloveLogTopicInfo</c> for each topic
        /// index.
        /// </summary>
        internal static void EmitGetTopic(StringBuilder sb, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, Dictionary<string, int> topicModes, string pad)
        {
            sb.AppendLine($"{pad}    FoxgloveLogTopicInfo IFoxgloveLogSource.FoxgloveLog_GetTopic(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var fields = topicMap[topics[i]];
                var explicitRates = fields.Where(m => m.HasExplicitHz).ToArray();
                var hasExplicitHz = explicitRates.Length > 0;
                var hz = hasExplicitHz
                    ? explicitRates.Max(m => m.Hz)
                    : fields.Max(m => m.Hz);
                var mode = topicModes[topics[i]];
                var tolerance = fields.Max(m => m.Tolerance);
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                var endpoint = fields[0];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, {4}, {5}f, (FoxRunFlow){6}, declaredSource: {7}, hasExplicitSource: {8}, declaredTargets: {9}, hasExplicitTargets: {10}, declaredEncoding: {11}, hasExplicitEncoding: {12}, qosProfile: {13}, hasExplicitQosProfile: {14}, qosReliability: {15}, hasExplicitReliability: {16}, qosDurability: {17}, hasExplicitDurability: {18}, qosHistory: {19}, hasExplicitHistory: {20}, qosDepth: {21}, hasExplicitDepth: {22}, hasExplicitHz: {23});",
                    pad,
                    i,
                    topic,
                    hz,
                    PolicyLiteral(mode),
                    tolerance,
                    endpoint.Mode,
                    InputDispatchEmitter.SourceLiteral(endpoint.Source),
                    InputDispatchEmitter.BoolLiteral(
                        (endpoint.NamedArgumentPresence & FoxRunNamedArgumentPresence.Source)
                        == FoxRunNamedArgumentPresence.Source),
                    InputDispatchEmitter.TargetsLiteral(endpoint.Targets),
                    InputDispatchEmitter.BoolLiteral(
                        (endpoint.NamedArgumentPresence & FoxRunNamedArgumentPresence.Targets)
                        == FoxRunNamedArgumentPresence.Targets),
                    EncodingLiteral(endpoint.Encoding),
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.Encoding)),
                    QosProfileLiteral(endpoint.QosProfile),
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.QoS)),
                    QosReliabilityLiteral(endpoint.QosReliability),
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.Reliability)),
                    QosDurabilityLiteral(endpoint.QosDurability),
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.Durability)),
                    QosHistoryLiteral(endpoint.QosHistory),
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.History)),
                    endpoint.QosDepth,
                    InputDispatchEmitter.BoolLiteral(HasExplicit(endpoint, FoxRunNamedArgumentPresence.Depth)),
                    InputDispatchEmitter.BoolLiteral(hasExplicitHz)));
            }
            sb.AppendLine($"{pad}            default: return default;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
        }

        /// <summary>
        /// Emits the optional Phase153 topic contract surface. Contracts are
        /// generated constants so runtime registration does not need
        /// reflection, model walking, or Editor-only helpers.
        /// </summary>
        internal static void EmitGetContract(StringBuilder sb, string ns, string className, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            sb.AppendLine($"{pad}    string IFoxgloveTopicContractSource.FoxgloveLog_Origin => __foxRunOrigin;");
            sb.AppendLine();
            sb.AppendLine($"{pad}    FoxTopicContract IFoxgloveTopicContractSource.FoxgloveLog_GetContract(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                var topic = topics[i];
                var fields = topicMap[topic];
                var schema = fields.FirstOrDefault(f => !string.IsNullOrEmpty(f.SchemaName))?.SchemaName ?? "";
                var encoding = EffectiveEncoding(fields);
                var canonical = CanonicalTopicShape(topic, schema, encoding, fields);
                var fingerprint = Sha256Hex(canonical);
                sb.AppendLine(
                    $"{pad}            case {i}: return new FoxTopicContract(\"{StringLiteralEmitter.CSharpStringLiteral(topic)}\", \"{StringLiteralEmitter.CSharpStringLiteral(schema)}\", \"{encoding}\", \"{StringLiteralEmitter.CSharpStringLiteral(canonical)}\", \"{fingerprint}\", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);");
            }
            sb.AppendLine($"{pad}            default: return null;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
        }

        /// <summary>
        /// Returns the <c>FoxRunPolicy</c> enum literal for the given
        /// numeric policy value (1=FixedRate, 2=Change, 4=Trigger).
        /// </summary>
        internal static string PolicyLiteral(int policy)
        {
            switch (policy)
            {
                case 1: return "FoxRunPolicy.FixedRate";
                case 2: return "FoxRunPolicy.Change";
                case 4: return "FoxRunPolicy.Trigger";
                default: return FormattableString.Invariant($"(FoxRunPolicy){policy}");
            }
        }

        internal static string EffectiveEncoding(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var declared = fields.Count == 0
                ? FoxRunGenerationDescriptorConstants.JsonEncoding
                : fields[0].Encoding;
            for (var i = 1; i < fields.Count; i++)
            {
                if (!string.Equals(declared, fields[i].Encoding, StringComparison.Ordinal))
                    throw new InvalidOperationException("FoxRun topic members must share one declared wire encoding.");
            }

            return string.Equals(declared, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal)
                ? FoxRunGenerationDescriptorConstants.ProtobufEncoding
                : FoxRunGenerationDescriptorConstants.JsonEncoding;
        }

        internal static bool IsInherited(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            return fields != null
                   && fields.Count > 0
                   && string.Equals(
                       fields[0].Encoding,
                       FoxRunGenerationDescriptorConstants.InheritEncoding,
                       StringComparison.Ordinal);
        }

        internal static bool UsesProtobuf(IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
            => string.Equals(EffectiveEncoding(fields), FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal)
               || IsInherited(fields);

        private static bool HasExplicit(
            FoxgloveSourceEmitter.TopicMember member,
            FoxRunNamedArgumentPresence presence)
            => (member.NamedArgumentPresence & presence) == presence;

        private static string EncodingLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.ProtobufEncoding, StringComparison.Ordinal))
                return "FoxRunEncoding.Protobuf";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.JsonEncoding, StringComparison.Ordinal))
                return "FoxRunEncoding.JSON";
            return "(FoxRunEncoding)0";
        }

        private static string QosProfileLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.DefaultQosProfile, StringComparison.Ordinal))
                return "FoxRunQosProfile.Default";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SensorDataQosProfile, StringComparison.Ordinal))
                return "FoxRunQosProfile.SensorData";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosProfile, StringComparison.Ordinal))
                return "FoxRunQosProfile.SystemDefault";
            return "(FoxRunQosProfile)0";
        }

        private static string QosReliabilityLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.ReliableQosReliability, StringComparison.Ordinal))
                return "FoxRunQosReliability.Reliable";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.BestEffortQosReliability, StringComparison.Ordinal))
                return "FoxRunQosReliability.BestEffort";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "FoxRunQosReliability.SystemDefault";
            return "(FoxRunQosReliability)0";
        }

        private static string QosDurabilityLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.VolatileQosDurability, StringComparison.Ordinal))
                return "FoxRunQosDurability.Volatile";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.TransientLocalQosDurability, StringComparison.Ordinal))
                return "FoxRunQosDurability.TransientLocal";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "FoxRunQosDurability.SystemDefault";
            return "(FoxRunQosDurability)0";
        }

        private static string QosHistoryLiteral(string value)
        {
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.KeepLastQosHistory, StringComparison.Ordinal))
                return "FoxRunQosHistory.KeepLast";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.KeepAllQosHistory, StringComparison.Ordinal))
                return "FoxRunQosHistory.KeepAll";
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.SystemDefaultQosPolicy, StringComparison.Ordinal))
                return "FoxRunQosHistory.SystemDefault";
            return "(FoxRunQosHistory)0";
        }

        private static string CanonicalTopicShape(string topic, string schema, string encoding, IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var sb = new StringBuilder();
            sb.Append("topic=").Append(topic ?? string.Empty).Append('\n');
            sb.Append("encoding=").Append(encoding ?? string.Empty).Append('\n');
            sb.Append("schema=").Append(schema ?? string.Empty).Append('\n');
            sb.Append("fields=");
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');
                var field = fields[i];
                sb.Append(field.JsonFieldName);
                sb.Append(':');
                sb.Append(field.CanonicalType);
            }
            return sb.ToString();
        }

        internal static string Sha256Hex(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] hash;
            lock (Sha256Gate)
            {
                hash = SharedSha256.ComputeHash(bytes);
            }

            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
