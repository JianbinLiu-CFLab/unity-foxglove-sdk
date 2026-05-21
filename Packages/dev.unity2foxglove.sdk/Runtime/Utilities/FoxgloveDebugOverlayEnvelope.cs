// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Utilities
// Purpose: JSON envelope for explicit FoxRun debug overlay topics.
// Kept UnityEngine-free so runtime tests can validate input boundaries.

namespace Unity.FoxgloveSDK.Util
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>
    /// Schemaless JSON envelope for explicit <c>/debug/...</c> diagnostics.
    /// </summary>
    public sealed class FoxgloveDebugOverlayEnvelope
    {
        public const int CurrentVersion = 1;
        public const string KindName = "debugOverlay";

        private const int MaxValueDepth = 8;
        private const string DebugTopicPrefix = "/debug/";

        private FoxgloveDebugOverlayEnvelope(
            string source,
            string label,
            IReadOnlyDictionary<string, object> values)
        {
            Version = CurrentVersion;
            Kind = KindName;
            Source = source;
            Label = string.IsNullOrWhiteSpace(label) ? null : label;
            Values = CopyValues(values);
        }

        [JsonProperty("version", Order = 0)]
        public int Version { get; }

        [JsonProperty("kind", Order = 1)]
        public string Kind { get; }

        [JsonProperty("source", Order = 2)]
        public string Source { get; }

        [JsonProperty("label", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string Label { get; }

        [JsonProperty("values", Order = 4)]
        public IReadOnlyDictionary<string, object> Values { get; }

        public static bool IsValidTopic(string topic)
        {
            return !string.IsNullOrWhiteSpace(topic)
                   && topic.StartsWith(DebugTopicPrefix, StringComparison.Ordinal)
                   && topic.Length > DebugTopicPrefix.Length;
        }

        public static bool TryCreate(
            string topic,
            string source,
            IReadOnlyDictionary<string, object> values,
            string label,
            out FoxgloveDebugOverlayEnvelope envelope)
        {
            envelope = null;

            if (!IsValidTopic(topic) || string.IsNullOrWhiteSpace(source))
                return false;

            if (values == null || values.Count == 0)
                return false;

            try
            {
                foreach (var pair in values)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || !IsSupportedJsonValue(pair.Value, 0))
                        return false;
                }

                envelope = new FoxgloveDebugOverlayEnvelope(source, label, values);
                return true;
            }
            catch
            {
                envelope = null;
                return false;
            }
        }

        public static bool TryCreateValue(
            string topic,
            string source,
            string key,
            object value,
            string label,
            out FoxgloveDebugOverlayEnvelope envelope)
        {
            envelope = null;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            var values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [key] = value
            };

            return TryCreate(topic, source, values, label, out envelope);
        }

        private static IReadOnlyDictionary<string, object> CopyValues(IReadOnlyDictionary<string, object> values)
        {
            var copy = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in values)
            {
                copy[pair.Key] = pair.Value;
            }

            return new ReadOnlyDictionary<string, object>(copy);
        }

        private static bool IsSupportedJsonValue(object value, int depth)
        {
            if (depth > MaxValueDepth)
                return false;

            if (value == null)
                return true;

            if (IsBinaryLikeValue(value))
                return false;

            var type = value.GetType();
            if (type.IsEnum)
                return true;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                case TypeCode.Char:
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.DateTime:
                case TypeCode.String:
                    return true;
            }

            if (value is DateTimeOffset || value is Guid)
                return true;

            if (value is IReadOnlyDictionary<string, object> readOnlyDictionary)
            {
                foreach (var entry in readOnlyDictionary)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                        return false;
                    if (!IsSupportedJsonValue(entry.Value, depth + 1))
                        return false;
                }

                return true;
            }

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!(entry.Key is string key) || string.IsNullOrWhiteSpace(key))
                        return false;
                    if (!IsSupportedJsonValue(entry.Value, depth + 1))
                        return false;
                }

                return true;
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    if (!IsSupportedJsonValue(item, depth + 1))
                        return false;
                }

                return true;
            }

            return false;
        }

        private static bool IsBinaryLikeValue(object value)
        {
            return value is byte[]
                   || value is ArraySegment<byte>
                   || value is Memory<byte>
                   || value is ReadOnlyMemory<byte>
                   || value is Stream
                   || value is IEnumerable<byte>;
        }
    }
}
