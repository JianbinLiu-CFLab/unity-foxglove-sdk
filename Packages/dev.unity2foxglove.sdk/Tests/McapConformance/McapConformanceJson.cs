// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/McapConformance
// Purpose: JSON normalization helpers matching the official MCAP conformance SerializableMcapRecord shape.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Tests.McapConformance
{
    internal static class McapConformanceJson
    {
        public static string WriteStreamed(List<SerializableMcapRecord> records)
            => JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["records"] = records
            }, Formatting.None) + "\n";

        public static string WriteIndexed(IndexedReadResult result)
            => JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["schemas"] = result.Schemas,
                ["channels"] = result.Channels,
                ["messages"] = result.Messages,
                ["statistics"] = result.Statistics
            }, Formatting.None) + "\n";

        public static SerializableMcapRecord Record(string type, params Field[] fields)
        {
            return new SerializableMcapRecord
            {
                Type = type,
                Fields = fields
                    .OrderBy(field => field.Name, StringComparer.Ordinal)
                    .Select(field => new object[] { field.Name, field.Value })
                    .ToList()
            };
        }

        public static Field Field(string name, object value)
            => new Field(name, value);

        public static string Number(ulong value)
            => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string Number(uint value)
            => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string Number(ushort value)
            => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public static string[] ByteArray(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<string>();

            var values = new string[data.Length];
            for (var i = 0; i < data.Length; i++)
                values[i] = data[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return values;
        }

        public static SortedDictionary<string, string> StringMap(Dictionary<string, string> map)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (map == null)
                return sorted;

            foreach (var pair in map)
                sorted[pair.Key] = pair.Value;
            return sorted;
        }

        public static SortedDictionary<string, string> UshortUlongMap(Dictionary<ushort, ulong> map)
        {
            var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (map == null)
                return sorted;

            foreach (var pair in map)
                sorted[pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Number(pair.Value);
            return sorted;
        }
    }

    internal sealed class Field
    {
        public readonly string Name;
        public readonly object Value;

        public Field(string name, object value)
        {
            Name = name;
            Value = value;
        }
    }

    internal sealed class SerializableMcapRecord
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("fields")]
        public List<object[]> Fields { get; set; }
    }

    internal sealed class IndexedReadResult
    {
        public List<SerializableMcapRecord> Schemas { get; } = new List<SerializableMcapRecord>();
        public List<SerializableMcapRecord> Channels { get; } = new List<SerializableMcapRecord>();
        public List<SerializableMcapRecord> Messages { get; } = new List<SerializableMcapRecord>();
        public List<SerializableMcapRecord> Statistics { get; } = new List<SerializableMcapRecord>();
    }
}
