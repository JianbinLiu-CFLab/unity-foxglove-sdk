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
                var rate = fields.Max(m => m.RateHz);
                var mode = topicModes[topics[i]];
                var eps = fields.Max(m => m.ChangeEpsilon);
                var forceInt = fields.Max(m => m.ForceIntervalSeconds);
                var topic = StringLiteralEmitter.CSharpStringLiteral(topics[i]);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}            case {1}: return new FoxgloveLogTopicInfo(\"{2}\", {3}f, {4}, {5}f, {6}f);",
                    pad, i, topic, rate, PublishModeLiteral(mode), eps, forceInt));
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
            var origin = string.IsNullOrEmpty(ns) ? className : ns + "." + className;
            sb.AppendLine($"{pad}    string IFoxgloveTopicContractSource.FoxgloveLog_Origin => \"{StringLiteralEmitter.CSharpStringLiteral(origin)}\";");
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
                var canonical = CanonicalTopicShape(topic, schema, fields);
                var fingerprint = Sha256Hex(canonical);
                sb.AppendLine(
                    $"{pad}            case {i}: return new FoxTopicContract(\"{StringLiteralEmitter.CSharpStringLiteral(topic)}\", \"{StringLiteralEmitter.CSharpStringLiteral(schema)}\", \"json\", \"{StringLiteralEmitter.CSharpStringLiteral(canonical)}\", \"{fingerprint}\", FoxTopicVisibility.Exported, FoxTopicWriterPolicy.SingleWriter);");
            }
            sb.AppendLine($"{pad}            default: return null;");
            sb.AppendLine($"{pad}        }}");
            sb.AppendLine($"{pad}    }}");
            sb.AppendLine();
        }

        /// <summary>
        /// Returns the <c>FoxRunPublishMode</c> enum literal for the given
        /// numeric mode value (0=FixedRate, 1=OnChange, 2=OnChangeOrInterval,
        /// 3=OnTrigger).
        /// </summary>
        internal static string PublishModeLiteral(int mode)
        {
            switch (mode)
            {
                case 0: return "FoxRunPublishMode.FixedRate";
                case 1: return "FoxRunPublishMode.OnChange";
                case 2: return "FoxRunPublishMode.OnChangeOrInterval";
                case 3: return "FoxRunPublishMode.OnTrigger";
                default: return FormattableString.Invariant($"(FoxRunPublishMode){mode}");
            }
        }

        private static string CanonicalTopicShape(string topic, string schema, IReadOnlyList<FoxgloveSourceEmitter.TopicMember> fields)
        {
            var sb = new StringBuilder();
            sb.Append("topic=").Append(topic ?? string.Empty).Append('\n');
            sb.Append("encoding=json\n");
            sb.Append("schema=").Append(schema ?? string.Empty).Append('\n');
            sb.Append("fields=");
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');
                var field = fields[i];
                sb.Append(field.JsonFieldName);
                sb.Append(':');
                sb.Append(string.IsNullOrWhiteSpace(field.CanonicalType)
                    ? FoxRunCanonicalTypeNormalizer.NormalizeTypeName(field.TypeName)
                    : field.CanonicalType);
            }
            return sb.ToString();
        }

        private static string Sha256Hex(string value)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
