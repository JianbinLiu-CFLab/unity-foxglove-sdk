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
    public static class TopicMetadataEmitter
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
                var declaration = fields[0];
                var hasExplicitDelivery =
                    HasExplicit(
                        declaration,
                        FoxRunNamedArgumentPresence.Reliability)
                    || HasExplicit(
                        declaration,
                        FoxRunNamedArgumentPresence.Durability)
                    || HasExplicit(
                        declaration,
                        FoxRunNamedArgumentPresence.History)
                    || HasExplicit(
                        declaration,
                        FoxRunNamedArgumentPresence.Depth);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, {4}, {5}f, (FoxRunFlow){6}, publishTransportIds: {7}, subscribeTransportId: {8}, declaredEncoding: {9}, hasExplicitEncoding: {10}, deliveryPolicy: new FoxRunDeliveryPolicy({11}, {12}, {13}, {14}), hasExplicitDeliveryPolicy: {15}, hasExplicitHz: {16});",
                    pad,
                    i,
                    topic,
                    hz,
                    PolicyLiteral(mode),
                    tolerance,
                    declaration.Mode,
                    TransportIdsLiteral(
                        declaration.PublishTransportIds),
                    NullableStringLiteral(
                        declaration.SubscribeTransportId),
                    EncodingLiteral(declaration.Encoding),
                    InputDispatchEmitter.BoolLiteral(
                        HasExplicit(
                            declaration,
                            FoxRunNamedArgumentPresence.Encoding)),
                    ReliabilityLiteral(declaration.Reliability),
                    DurabilityLiteral(declaration.Durability),
                    HistoryLiteral(declaration.History),
                    declaration.Depth,
                    InputDispatchEmitter.BoolLiteral(
                        hasExplicitDelivery),
                    InputDispatchEmitter.BoolLiteral(hasExplicitHz)));
            }
            sb.AppendLine($"{pad}            default: return default;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
        }

        /// <summary>
        /// Emits the optional Phase153 topic contract surface. Contracts are
        /// generated as stable readonly instances so runtime registration and
        /// hot-path wire views do not need reflection, model walking,
        /// per-publication allocation, or Editor-only helpers.
        /// </summary>
        internal static void EmitGetContract(StringBuilder sb, string ns, string className, IReadOnlyList<string> topics, Dictionary<string, List<FoxgloveSourceEmitter.TopicMember>> topicMap, string pad)
        {
            for (int i = 0; i < topics.Count; i++)
            {
                var topic = topics[i];
                var fields = topicMap[topic];
                var schema = fields.FirstOrDefault(
                    field => !string.IsNullOrEmpty(field.SchemaName))
                    ?.SchemaName ?? "";
                var encoding = EffectiveEncoding(fields);
                var canonical = CanonicalTopicShape(
                    topic,
                    schema,
                    encoding,
                    fields);
                var fingerprint = Sha256Hex(canonical);
                sb.AppendLine(
                    $"{pad}    private static readonly FoxTopicContract __foxRunContract_{i} = new FoxTopicContract(\"{StringLiteralEmitter.CSharpStringLiteral(topic)}\", \"{StringLiteralEmitter.CSharpStringLiteral(schema)}\", \"{encoding}\", \"{StringLiteralEmitter.CSharpStringLiteral(canonical)}\", \"{fingerprint}\", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);");
            }
            sb.AppendLine();
            sb.AppendLine($"{pad}    string IFoxgloveTopicContractSource.FoxgloveLog_Origin => __foxRunOrigin;");
            sb.AppendLine();
            sb.AppendLine($"{pad}    FoxTopicContract IFoxgloveTopicContractSource.FoxgloveLog_GetContract(int index)");
            sb.AppendLine($"{pad}    {{");
            sb.AppendLine($"{pad}        switch (index)");
            sb.AppendLine($"{pad}        {{");
            for (int i = 0; i < topics.Count; i++)
            {
                sb.AppendLine(
                    $"{pad}            case {i}: return __foxRunContract_{i};");
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

            if (string.Equals(
                    declared,
                    FoxRunGenerationDescriptorConstants.ProtobufEncoding,
                    StringComparison.Ordinal))
            {
                return FoxRunGenerationDescriptorConstants.ProtobufEncoding;
            }
            if (string.Equals(
                    declared,
                    FoxRunGenerationDescriptorConstants.MessagePackEncoding,
                    StringComparison.Ordinal))
            {
                return FoxRunGenerationDescriptorConstants.MessagePackEncoding;
            }
            if (string.Equals(
                    declared,
                    FoxRunGenerationDescriptorConstants.JsonEncoding,
                    StringComparison.Ordinal)
                || string.Equals(
                    declared,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    StringComparison.Ordinal))
            {
                return FoxRunGenerationDescriptorConstants.JsonEncoding;
            }

            throw new InvalidOperationException(
                "FoxRun topic declares unsupported wire encoding '"
                + (declared ?? string.Empty)
                + "'.");
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
            if (string.Equals(value, FoxRunGenerationDescriptorConstants.MessagePackEncoding, StringComparison.Ordinal))
                return "FoxRunEncoding.MessagePack";
            return "(FoxRunEncoding)0";
        }

        internal static string ReliabilityLiteral(string value)
        {
            if (string.Equals(value, "reliable", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.Reliable";
            if (string.Equals(value, "best-effort", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.BestEffort";
            if (string.Equals(value, "system-default", StringComparison.Ordinal))
                return "FoxRunDeliveryReliability.SystemDefault";
            return "FoxRunDeliveryReliability.ProviderDefault";
        }

        internal static string DurabilityLiteral(string value)
        {
            if (string.Equals(value, "volatile", StringComparison.Ordinal))
                return "FoxRunDeliveryDurability.Volatile";
            if (string.Equals(value, "transient-local", StringComparison.Ordinal))
                return "FoxRunDeliveryDurability.TransientLocal";
            if (string.Equals(value, "system-default", StringComparison.Ordinal))
                return "FoxRunDeliveryDurability.SystemDefault";
            return "FoxRunDeliveryDurability.ProviderDefault";
        }

        internal static string HistoryLiteral(string value)
        {
            if (string.Equals(value, "keep-last", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepLast";
            if (string.Equals(value, "keep-all", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.KeepAll";
            if (string.Equals(value, "system-default", StringComparison.Ordinal))
                return "FoxRunDeliveryHistory.SystemDefault";
            return "FoxRunDeliveryHistory.ProviderDefault";
        }

        internal static string TransportIdsLiteral(
            IReadOnlyList<string> values)
        {
            if (values == null)
                return "null";
            return "new string[] { "
                   + string.Join(
                       ", ",
                       values.Select(
                           value =>
                               "\""
                               + StringLiteralEmitter
                                   .CSharpStringLiteral(value)
                               + "\""))
                   + " }";
        }

        internal static string NullableStringLiteral(string value)
            => value == null
                ? "null"
                : "\""
                  + StringLiteralEmitter.CSharpStringLiteral(value)
                  + "\"";

        internal static string CanonicalTopicShape(string topic, string schema, string encoding, IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var sb = new StringBuilder();
            sb.Append("v2|topic=");
            FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                sb,
                topic);
            sb.Append("|encoding=");
            FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                sb,
                encoding);
            sb.Append("|schema=");
            FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                sb,
                schema);
            var orderedFields = fields
                .OrderBy(
                    field => field.JsonFieldName,
                    StringComparer.Ordinal)
                .ThenBy(
                    field => field.MemberName,
                    StringComparer.Ordinal)
                .ToList();
            sb.Append("|fields=")
                .Append(
                    orderedFields.Count.ToString(
                        CultureInfo.InvariantCulture));
            foreach (var field in orderedFields)
            {
                sb.Append("|field|json=");
                FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                    sb,
                    field.JsonFieldName);
                sb.Append("|member=");
                FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                    sb,
                    field.MemberName);
                sb.Append("|canonical=");
                FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                    sb,
                    field.CanonicalType);
                sb.Append("|shape=");
                var identity = FoxRunTypeShapeIdentityFormatter.Build(
                    field.TypeShape
                    ?? FoxRunTypeShape.Canonical(field.CanonicalType),
                    includeUsageTraits: true);
                FoxRunTypeShapeIdentityFormatter.AppendLengthPrefixed(
                    sb,
                    identity);
            }
            sb.Append("|end");
            return sb.ToString();
        }

        public static string Sha256Hex(string value)
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
