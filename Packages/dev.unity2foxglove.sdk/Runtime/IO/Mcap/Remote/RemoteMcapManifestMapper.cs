// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Mapping from local MCAP DataLoader initialization to prototype remote manifest DTOs.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Maps local DataLoader initialization metadata into a remote-manifest shape.</summary>
    public static class RemoteMcapManifestMapper
    {
        public static RemoteMcapManifest FromInitialization(
            McapDataLoaderInitialization initialization,
            string manifestName,
            string sourceId,
            string dataRoute)
        {
            if (initialization == null)
                throw new ArgumentNullException(nameof(initialization));

            var source = new RemoteMcapSource
            {
                Id = sourceId ?? string.Empty,
                Name = manifestName ?? string.Empty,
                DataUrl = dataRoute ?? string.Empty,
                HasTimeRange = initialization.TimeRange != null && initialization.TimeRange.HasRange,
                StartTimeNs = initialization.TimeRange != null ? initialization.TimeRange.StartTimeNs : 0,
                EndTimeNs = initialization.TimeRange != null ? initialization.TimeRange.EndTimeNs : 0
            };

            AddTopics(source, initialization.Channels);
            AddSchemas(source, initialization.Schemas);
            AddProblems(source, initialization.Problems);

            var manifest = new RemoteMcapManifest { Name = manifestName ?? string.Empty };
            manifest.Sources.Add(source);
            return manifest;
        }

        private static void AddTopics(RemoteMcapSource source, List<McapDataLoaderChannel> channels)
        {
            var ordered = channels == null
                ? new List<McapDataLoaderChannel>()
                : new List<McapDataLoaderChannel>(channels);
            ordered.Sort(CompareChannels);
            for (var i = 0; i < ordered.Count; i++)
            {
                var channel = ordered[i];
                if (channel == null)
                    continue;

                source.Topics.Add(new RemoteMcapTopic
                {
                    ChannelId = channel.ChannelId,
                    Name = channel.Topic ?? string.Empty,
                    MessageEncoding = channel.MessageEncoding ?? string.Empty,
                    SchemaId = channel.SchemaId
                });
            }
        }

        private static void AddSchemas(RemoteMcapSource source, List<McapDataLoaderSchema> schemas)
        {
            var ordered = schemas == null
                ? new List<McapDataLoaderSchema>()
                : new List<McapDataLoaderSchema>(schemas);
            ordered.Sort((left, right) => left.SchemaId.CompareTo(right.SchemaId));
            for (var i = 0; i < ordered.Count; i++)
            {
                var schema = ordered[i];
                if (schema == null)
                    continue;

                var data = schema.Data ?? new byte[0];
                source.Schemas.Add(new RemoteMcapSchema
                {
                    Id = schema.SchemaId,
                    Name = schema.Name ?? string.Empty,
                    Encoding = schema.Encoding ?? string.Empty,
                    DataBase64 = Convert.ToBase64String(data),
                    DataLength = data.Length
                });
            }
        }

        private static void AddProblems(RemoteMcapSource source, List<McapDataLoaderProblem> problems)
        {
            if (problems == null)
                return;

            for (var i = 0; i < problems.Count; i++)
            {
                var problem = problems[i];
                if (problem == null)
                    continue;

                source.Problems.Add(new RemoteMcapProblem(
                    problem.Severity.ToString(),
                    problem.Code,
                    problem.Message,
                    problem.Tip));
            }
        }

        private static int CompareChannels(McapDataLoaderChannel left, McapDataLoaderChannel right)
        {
            var cmp = string.Compare(left?.Topic ?? string.Empty, right?.Topic ?? string.Empty, StringComparison.Ordinal);
            if (cmp != 0)
                return cmp;

            return (left?.ChannelId ?? 0).CompareTo(right?.ChannelId ?? 0);
        }
    }
}
