// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Diagnostic problem construction for MCAP DataLoader initialization.

using System;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.IO
{
    public sealed partial class McapDataLoader
    {
        private void AddSequentialFallbackProblems(McapDataLoaderInitialization initialization)
        {
            var chunkIndexes = _reader.Summary?.ChunkIndexes;
            if (chunkIndexes != null && chunkIndexes.Count > 0)
                return;

            initialization.Problems.Add(new McapDataLoaderProblem(
                McapDataLoaderProblemSeverity.Warning,
                "MCAP file has no chunk indexes; queries will use bounded sequential fallback.",
                "UnindexedSequentialFallback",
                "Large unindexed files may require adding MCAP chunk indexes or increasing explicit fallback limits."));

            if (_sourceLengthBytes >= 0 &&
                _sequentialReadLimits.MaxPayloadBytes > 0 &&
                _sourceLengthBytes > _sequentialReadLimits.MaxPayloadBytes)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "MCAP file size exceeds the sequential fallback payload limit.",
                    "UnindexedFileExceedsSequentialPayloadLimit",
                    "Queries may fail with MaxPayloadBytes unless the file is indexed or the limit is explicitly raised."));
            }

            var messageCount = _reader.Summary?.Statistics?.MessageCount ?? 0UL;
            if (_sequentialReadLimits.MaxMessages > 0 &&
                messageCount > (ulong)_sequentialReadLimits.MaxMessages)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "MCAP message count exceeds the sequential fallback message limit.",
                    "UnindexedFileExceedsSequentialMessageLimit",
                    "Queries may fail with MaxMessages unless the file is indexed or the limit is explicitly raised."));
            }
        }

        private void AddSchemaReferenceProblems(McapDataLoaderInitialization initialization)
        {
            for (var i = 0; i < initialization.Channels.Count; i++)
            {
                var channel = initialization.Channels[i];
                if (channel.SchemaId != 0 && !_schemaMap.ContainsKey(channel.SchemaId))
                {
                    initialization.Problems.Add(new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "MCAP channel references a schema ID that is not present in the summary.",
                        "UnknownSchemaId",
                        "The raw message payload is still available; typed decoding may not be possible."));
                }
            }
        }

        private void AddFoxRunSchemaMetadataProblems(McapDataLoaderInitialization initialization)
        {
            var metadataIndex = FindMetadataIndex(FoxRunSchemaMcapMetadata.MetadataName);
            if (metadataIndex == null)
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded MCAP does not contain FoxRun schema metadata; local raw loading will continue.",
                    "FoxRunSchemaMetadataMissing"));
                return;
            }

            var metadata = _reader.ReadMetadata(metadataIndex);
            if (metadata?.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
            {
                initialization.Problems.Add(new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded FoxRun schema metadata is missing its value entry; local raw loading will continue.",
                    "FoxRunSchemaMetadataMalformed"));
                return;
            }

            var result = FoxRunSchemaMcapMetadata.EvaluateRecordedJson(value, FoxRunSchemaInfoRegistry.Current);
            initialization.Problems.Add(ToProblem(result));
        }

        private McapMetadataIndex FindMetadataIndex(string name)
        {
            var indexes = _reader.MetadataIndexes;
            if (indexes == null || string.IsNullOrEmpty(name))
                return null;

            for (var i = 0; i < indexes.Count; i++)
            {
                var index = indexes[i];
                if (index != null && string.Equals(index.Name, name, StringComparison.Ordinal))
                    return index;
            }

            return null;
        }

        private static McapDataLoaderProblem ToProblem(FoxRunReplaySchemaGuardResult result)
        {
            if (result == null)
                return new McapDataLoaderProblem(
                    McapDataLoaderProblemSeverity.Warning,
                    "Recorded MCAP does not contain usable FoxRun schema metadata; local raw loading will continue.",
                    "FoxRunSchemaMetadataMissing");

            switch (result.State)
            {
                case FoxRunReplaySchemaGuardState.Match:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Info,
                        "Recorded FoxRun schema metadata matches the current runtime manifest hash.",
                        "FoxRunSchemaMetadataMatch");
                case FoxRunReplaySchemaGuardState.MissingRecorded:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Recorded MCAP does not contain FoxRun schema metadata; local raw loading will continue.",
                        "FoxRunSchemaMetadataMissing");
                case FoxRunReplaySchemaGuardState.MissingCurrent:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Current runtime does not expose generated FoxRun schema info; local raw loading will continue.",
                        "FoxRunSchemaMetadataMissingCurrent");
                case FoxRunReplaySchemaGuardState.MalformedRecorded:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        "Recorded FoxRun schema metadata is malformed; local raw loading will continue.",
                        "FoxRunSchemaMetadataMalformed");
                case FoxRunReplaySchemaGuardState.Mismatch:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Error,
                        "Recorded FoxRun schema metadata does not match the current runtime manifest; local raw loading will continue.",
                        "FoxRunSchemaMetadataMismatch",
                        "Replay may still be blocked by Phase 114 strict schema identity policy.");
                default:
                    return new McapDataLoaderProblem(
                        McapDataLoaderProblemSeverity.Warning,
                        result.Message ?? string.Empty,
                        "FoxRunSchemaMetadataUnknown");
            }
        }
    }
}
