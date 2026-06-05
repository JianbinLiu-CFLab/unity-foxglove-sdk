// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Serializes local remote MCAP DTOs to Foxglove's Remote Data Loader manifest contract.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Serializes Remote Data Loader manifests using Foxglove's official JSON field names.</summary>
    public static class RemoteMcapOfficialManifestSerializer
    {
        private const ulong NanosecondsPerSecond = 1_000_000_000UL;
        private const string NanosecondFractionFormat = "D9";

        /// <summary>Serializes a remote MCAP manifest to the official Foxglove Remote Data Loader JSON shape.</summary>
        public static string Serialize(RemoteMcapManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            return ToJson(manifest).ToString(Formatting.None);
        }

        private static JObject ToJson(RemoteMcapManifest manifest)
        {
            var root = new JObject
            {
                ["sources"] = SourcesToJson(manifest)
            };
            if (!string.IsNullOrEmpty(manifest.Name))
                root["name"] = manifest.Name;
            return root;
        }

        private static JArray SourcesToJson(RemoteMcapManifest manifest)
        {
            var sources = new JArray();
            if (manifest.Sources == null)
                return sources;

            for (var i = 0; i < manifest.Sources.Count; i++)
            {
                var source = manifest.Sources[i];
                if (source == null)
                    continue;

                var json = new JObject
                {
                    ["url"] = source.DataUrl ?? string.Empty,
                    ["topics"] = TopicsToJson(source),
                    ["schemas"] = SchemasToJson(source),
                    ["startTime"] = FormatUnixNanoseconds(source.StartTimeNs),
                    ["endTime"] = FormatUnixNanoseconds(source.EndTimeNs)
                };
                if (!string.IsNullOrEmpty(source.Id))
                    json["id"] = source.Id;

                sources.Add(json);
            }

            return sources;
        }

        private static JArray TopicsToJson(RemoteMcapSource source)
        {
            var topics = new JArray();
            if (source.Topics == null)
                return topics;

            for (var i = 0; i < source.Topics.Count; i++)
            {
                var topic = source.Topics[i];
                if (topic == null)
                    continue;

                var json = new JObject
                {
                    ["name"] = topic.Name ?? string.Empty,
                    ["messageEncoding"] = topic.MessageEncoding ?? string.Empty
                };
                if (topic.SchemaId != 0)
                    json["schemaId"] = topic.SchemaId;

                topics.Add(json);
            }

            return topics;
        }

        private static JArray SchemasToJson(RemoteMcapSource source)
        {
            var schemas = new JArray();
            if (source.Schemas == null)
                return schemas;

            for (var i = 0; i < source.Schemas.Count; i++)
            {
                var schema = source.Schemas[i];
                if (schema == null)
                    continue;

                schemas.Add(new JObject
                {
                    ["id"] = schema.Id,
                    ["name"] = schema.Name ?? string.Empty,
                    ["encoding"] = schema.Encoding ?? string.Empty,
                    ["data"] = schema.DataBase64 ?? string.Empty
                });
            }

            return schemas;
        }

        private static string FormatUnixNanoseconds(ulong unixNanoseconds)
        {
            var seconds = unixNanoseconds / NanosecondsPerSecond;
            var nanoseconds = unixNanoseconds % NanosecondsPerSecond;
            if (seconds > long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(unixNanoseconds), "Timestamp seconds exceed Int64 range.");

            var value = DateTimeOffset.FromUnixTimeSeconds((long)seconds)
                .UtcDateTime
                .ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            if (nanoseconds == 0)
                return value + "Z";

            var fraction = nanoseconds.ToString(NanosecondFractionFormat, System.Globalization.CultureInfo.InvariantCulture)
                .TrimEnd('0');
            return value + "." + fraction + "Z";
        }
    }
}
