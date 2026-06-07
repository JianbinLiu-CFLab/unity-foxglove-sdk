// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Re-emits local MCAP query results as self-contained MCAP streams for
// Remote Data Loader data responses.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Writes a time-filtered MCAP response that ordinary MCAP readers can open.</summary>
    public static class RemoteMcapRangeWriter
    {
        /// <summary>Builds a self-contained MCAP stream for the requested inclusive log-time range.</summary>
        public static MemoryStream CreateSlice(string mcapPath, RemoteMcapRequest request, long maxInMemoryDataBytes)
        {
            if (mcapPath == null)
                throw new ArgumentNullException(nameof(mcapPath));

            request = request ?? new RemoteMcapRequest();
            using var loader = new McapDataLoader(mcapPath);
            var initialization = loader.Initialize();
            var messages = loader.CreateIterator(new McapDataLoaderQuery
            {
                StartTimeNs = request.StartTimeNs,
                EndTimeNs = request.EndTimeNs,
                MaxMessages = 0
            });

            var output = new MemoryStream();
            try
            {
                using (var recorder = new McapRecorder(
                    output,
                    null,
                    new McapWriterOptions { UseChunking = false },
                    leaveOpen: true))
                {
                    RegisterChannels(recorder, initialization);
                    ThrowIfOverCap(output, maxInMemoryDataBytes);
                    foreach (var message in messages)
                    {
                        recorder.WriteMessage(message.ChannelId, message.LogTime, message.Data);
                        ThrowIfOverCap(output, maxInMemoryDataBytes);
                    }

                    recorder.Close();
                    ThrowIfOverCap(output, maxInMemoryDataBytes);
                }
            }
            catch
            {
                output.Dispose();
                throw;
            }

            output.Position = 0;
            return output;
        }

        private static void RegisterChannels(McapRecorder recorder, McapDataLoaderInitialization initialization)
        {
            var schemas = BuildSchemaMap(initialization?.Schemas);
            var channels = initialization?.Channels == null
                ? new List<McapDataLoaderChannel>()
                : new List<McapDataLoaderChannel>(initialization.Channels);
            channels.Sort((left, right) => left.ChannelId.CompareTo(right.ChannelId));

            for (var i = 0; i < channels.Count; i++)
            {
                var channel = channels[i];
                if (channel == null)
                    continue;

                schemas.TryGetValue(channel.SchemaId, out var schema);
                recorder.AddChannel(
                    channel.ChannelId,
                    channel.Topic,
                    channel.MessageEncoding,
                    schema?.Name ?? string.Empty,
                    schema?.Encoding ?? string.Empty,
                    SchemaContentForRecorder(schema));
            }
        }

        private static Dictionary<ushort, McapDataLoaderSchema> BuildSchemaMap(List<McapDataLoaderSchema> schemas)
        {
            var result = new Dictionary<ushort, McapDataLoaderSchema>();
            if (schemas == null)
                return result;

            for (var i = 0; i < schemas.Count; i++)
            {
                var schema = schemas[i];
                if (schema != null)
                    result[schema.SchemaId] = schema;
            }

            return result;
        }

        private static string SchemaContentForRecorder(McapDataLoaderSchema schema)
        {
            if (schema == null || schema.Data == null || schema.Data.Length == 0)
                return string.Empty;

            return string.Equals(schema.Encoding, "protobuf", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToBase64String(schema.Data)
                : Encoding.UTF8.GetString(schema.Data);
        }

        private static void ThrowIfOverCap(MemoryStream output, long maxInMemoryDataBytes)
        {
            if (maxInMemoryDataBytes >= 0 && output.Length > maxInMemoryDataBytes)
                throw new RemoteMcapRangeTooLargeException(
                    "Requested MCAP range exceeds the configured in-memory byte response cap.");
        }
    }

    internal sealed class RemoteMcapRangeTooLargeException : InvalidOperationException
    {
        public RemoteMcapRangeTooLargeException(string message)
            : base(message)
        {
        }
    }
}
