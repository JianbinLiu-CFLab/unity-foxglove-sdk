// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Evaluates FoxRun schema identity metadata embedded in an MCAP file
// against the current runtime's schema identity to detect potential mismatch.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Evaluates FoxRun schema identity metadata embedded in an MCAP file
    /// against the current runtime's schema identity to detect potential
    /// mismatch.
    /// </summary>
    internal static class ReplaySchemaGuard
    {
        /// <summary>
        /// Reads the schema-identity metadata record from the given replay engine
        /// and returns a <see cref="FoxRunReplaySchemaGuardResult"/> describing
        /// whether the recorded schema matches the current runtime.
        /// </summary>
        internal static FoxRunReplaySchemaGuardResult Evaluate(McapReplayEngine replayEngine)
            => EvaluateWithMode(replayEngine, SchemaIdentityMode.Strict);

        internal static FoxRunReplaySchemaGuardResult EvaluateWithMode(
            McapReplayEngine replayEngine,
            SchemaIdentityMode identityMode)
            => EvaluateWithMode(replayEngine, identityMode, null);

        internal static FoxRunReplaySchemaGuardResult EvaluateWithMode(
            McapReplayEngine replayEngine,
            SchemaIdentityMode identityMode,
            ISchemaRegistry schemaRegistry)
        {
            var foxRunResult = EvaluateFoxRun(replayEngine, identityMode);
            var sdkResult = EvaluateSdkWireSchema(replayEngine, identityMode, schemaRegistry);
            if (sdkResult == null)
                return foxRunResult;
            if (foxRunResult == null
                || foxRunResult.State == FoxRunReplaySchemaGuardState.MissingRecorded)
                return sdkResult;
            if (sdkResult.State == FoxRunReplaySchemaGuardState.Match)
                return foxRunResult;
            if (foxRunResult.State == FoxRunReplaySchemaGuardState.Match)
                return sdkResult;

            var isBlocking = foxRunResult.IsBlocking || sdkResult.IsBlocking;
            return new FoxRunReplaySchemaGuardResult(
                FoxRunReplaySchemaGuardState.Mismatch,
                isBlocking,
                foxRunResult.Message + "\n" + sdkResult.Message,
                foxRunResult.RecordedGlobalManifestHash,
                foxRunResult.CurrentGlobalManifestHash);
        }

        private static FoxRunReplaySchemaGuardResult EvaluateFoxRun(
            McapReplayEngine replayEngine,
            SchemaIdentityMode identityMode)
        {
            var metadata = replayEngine?.FindMetadata(FoxRunSchemaMcapMetadata.MetadataName);
            if (metadata == null)
                return FoxRunSchemaMcapMetadata.CreateMissingRecordedResult();

            if (metadata.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
                return FoxRunSchemaMcapMetadata.CreateMalformedRecordedResult(
                    "Metadata record is missing the value entry.",
                    identityMode);

            return FoxRunSchemaMcapMetadata.EvaluateRecordedJson(
                value, FoxRunSchemaInfoRegistry.Current, identityMode);
        }

        private static FoxRunReplaySchemaGuardResult EvaluateSdkWireSchema(
            McapReplayEngine replayEngine,
            SchemaIdentityMode identityMode,
            ISchemaRegistry schemaRegistry)
        {
            var metadata = replayEngine?.FindMetadata(SdkWireSchemaMcapMetadata.MetadataName);
            if (metadata == null)
            {
                var foxRunMetadata = replayEngine?.FindMetadata(FoxRunSchemaMcapMetadata.MetadataName);
                if (foxRunMetadata?.Metadata == null
                    || !foxRunMetadata.Metadata.TryGetValue("value", out var foxRunValue)
                    || !FoxRunSchemaMcapMetadata.TryParseJson(foxRunValue, out var foxRunRecord, out _)
                    || string.IsNullOrWhiteSpace(foxRunRecord.SdkWireSchemaHash))
                    return null;
                if (!SdkWireSchemaIdentity.TryCompute(schemaRegistry, out var fallbackCurrentHash))
                    return CreateSdkResult(
                        FoxRunReplaySchemaGuardState.MissingCurrent,
                        identityMode,
                        "Current runtime does not expose an SDK-wide wire-schema identity for the FoxRun metadata hash.",
                        foxRunRecord.SdkWireSchemaHash,
                        string.Empty);
                if (string.Equals(foxRunRecord.SdkWireSchemaHash, fallbackCurrentHash, StringComparison.Ordinal))
                    return new FoxRunReplaySchemaGuardResult(
                        FoxRunReplaySchemaGuardState.Match,
                        false,
                        "Recorded FoxRun SDK wire-schema hash matches the current runtime.",
                        foxRunRecord.SdkWireSchemaHash,
                        fallbackCurrentHash);
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.Mismatch,
                    identityMode,
                    "FoxRun SDK wire-schema hash mismatch. Recorded: "
                    + ShortHash(foxRunRecord.SdkWireSchemaHash)
                    + "; Current: "
                    + ShortHash(fallbackCurrentHash),
                    foxRunRecord.SdkWireSchemaHash,
                    fallbackCurrentHash);
            }

            if (metadata.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MalformedRecorded,
                    identityMode,
                    "SDK-wide wire-schema metadata is missing the value entry.",
                    string.Empty,
                    string.Empty);

            if (!SdkWireSchemaMcapMetadata.TryParseJson(
                    value,
                    out var recordedHash,
                    out var metadataVersion,
                    out var recordedComponents,
                    out var error))
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MalformedRecorded,
                    identityMode,
                    "SDK-wide wire-schema metadata is malformed. " + error,
                    string.Empty,
                    string.Empty);

            var recordedSchemas = SelectSdkSchemaRecords(replayEngine?.Summary);
            string identityFailure;
            string currentHash;
            bool currentIdentityAvailable;
            if (metadataVersion >= 3)
            {
                currentIdentityAvailable = SdkWireSchemaIdentity.TryCompute(
                    schemaRegistry,
                    recordedSchemas,
                    recordedComponents,
                    out currentHash,
                    out identityFailure);
            }
            else if (metadataVersion >= 2)
            {
                currentIdentityAvailable = SdkWireSchemaIdentity.TryCompute(
                    schemaRegistry,
                    recordedSchemas,
                    out currentHash,
                    out identityFailure);
            }
            else
            {
                currentIdentityAvailable = SdkWireSchemaIdentity.TryCompute(
                    schemaRegistry,
                    out currentHash);
                identityFailure = currentIdentityAvailable
                    ? string.Empty
                    : "Current runtime does not expose an SDK-wide wire-schema identity.";
            }
            if (!currentIdentityAvailable)
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MissingCurrent,
                    identityMode,
                    "Current runtime does not expose the recorded output contracts. " + identityFailure,
                    recordedHash,
                    string.Empty);

            if (string.Equals(recordedHash, currentHash, StringComparison.Ordinal))
                return new FoxRunReplaySchemaGuardResult(
                    FoxRunReplaySchemaGuardState.Match,
                    false,
                    "Recorded SDK-wide wire-schema identity matches the current runtime.",
                    recordedHash,
                    currentHash);

            return CreateSdkResult(
                FoxRunReplaySchemaGuardState.Mismatch,
                identityMode,
                "SDK-wide wire-schema identity mismatch. Recorded: "
                + ShortHash(recordedHash)
                + "; Current: "
                + ShortHash(currentHash),
                recordedHash,
                currentHash);
        }

        private static IReadOnlyList<McapSchema> SelectSdkSchemaRecords(McapFileSummary summary)
        {
            if (summary?.Schemas == null)
                return Array.Empty<McapSchema>();

            var channels = summary.Channels ?? new List<McapChannel>();
            var hasDirection = channels.Any(channel =>
                channel?.Metadata != null
                && channel.Metadata.ContainsKey(McapRecorder.DataDirectionMetadataKey));
            if (!hasDirection)
                return summary.Schemas;

            var outputSchemaIds = new HashSet<ushort>(
                channels.Where(channel =>
                    channel?.Metadata != null
                    && channel.Metadata.TryGetValue(McapRecorder.DataDirectionMetadataKey, out var direction)
                    && string.Equals(direction, "output", StringComparison.OrdinalIgnoreCase))
                    .Select(channel => channel.SchemaId));
            return summary.Schemas.Where(schema => outputSchemaIds.Contains(schema.Id)).ToList();
        }
        private static FoxRunReplaySchemaGuardResult CreateSdkResult(
            FoxRunReplaySchemaGuardState state,
            SchemaIdentityMode identityMode,
            string message,
            string recordedHash,
            string currentHash)
        {
            var isBlocking = identityMode == SchemaIdentityMode.Strict;
            return new FoxRunReplaySchemaGuardResult(
                state,
                isBlocking,
                message + (isBlocking ? " Replay blocked." : " Replay will continue."),
                recordedHash,
                currentHash);
        }

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "<missing>";
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }
    }

    internal static class SdkWireSchemaIdentity
    {
        internal static bool TryCompute(ISchemaRegistry registry, out string hash)
        {
            hash = string.Empty;
            if (!(registry is ISchemaRegistrySnapshot snapshot))
                return false;

            return TryComputeCore(
                snapshot.GetSchemaSnapshot(),
                Array.Empty<SdkWireSchemaComponentIdentity>(),
                out hash,
                includeLegacyComponentSnapshot: true);
        }

        internal static bool TryCompute(
            ISchemaRegistry registry,
            IReadOnlyList<McapSchema> recordedSchemas,
            out string hash)
            => TryCompute(registry, recordedSchemas, Array.Empty<SdkWireSchemaComponentIdentity>(), out hash, out _);

        internal static bool TryCompute(
            ISchemaRegistry registry,
            IReadOnlyList<McapSchema> recordedSchemas,
            out string hash,
            out string failureReason)
            => TryCompute(registry, recordedSchemas, Array.Empty<SdkWireSchemaComponentIdentity>(), out hash, out failureReason);

        internal static bool TryCompute(
            ISchemaRegistry registry,
            IReadOnlyList<McapSchema> recordedSchemas,
            IReadOnlyList<SdkWireSchemaComponentIdentity> recordedComponents,
            out string hash)
            => TryCompute(registry, recordedSchemas, recordedComponents, out hash, out _);

        internal static bool TryCompute(
            ISchemaRegistry registry,
            IReadOnlyList<McapSchema> recordedSchemas,
            IReadOnlyList<SdkWireSchemaComponentIdentity> recordedComponents,
            out string hash,
            out string failureReason)
        {
            hash = string.Empty;
            failureReason = string.Empty;
            if (!(registry is ISchemaRegistrySnapshot snapshot)
                || recordedSchemas == null
                || recordedComponents == null)
            {
                failureReason = "The current schema registry cannot provide a snapshot.";
                return false;
            }

            var current = snapshot.GetSchemaSnapshot() ?? Array.Empty<SchemaEntry>();
            var selected = new List<SchemaEntry>(recordedSchemas.Count);
            foreach (var recorded in recordedSchemas)
            {
                if (recorded == null)
                    continue;

                var match = current.FirstOrDefault(entry =>
                    string.Equals(entry.Name, recorded.Name, StringComparison.Ordinal)
                    && string.Equals(entry.Encoding, recorded.Encoding, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(match.Name))
                {
                    failureReason = "Missing schema '" + recorded.Name + "' with encoding '" + recorded.Encoding + "'.";
                    return false;
                }
                selected.Add(match);
            }

            var componentSnapshot = ComponentMessagePackCodecRegistry.CaptureSnapshot();
            var selectedComponents = new List<SdkWireSchemaComponentIdentity>(recordedComponents.Count);
            foreach (var recorded in recordedComponents)
            {
                if (recorded == null)
                {
                    failureReason = "The recording contains a null component contract.";
                    return false;
                }
                if (!string.Equals(recorded.Encoding, "msgpack", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(recorded.LogicalSchema)
                    || string.IsNullOrEmpty(recorded.ShapeIdentity))
                {
                    failureReason = "Invalid recorded component contract for logical schema '"
                        + (recorded.LogicalSchema ?? string.Empty)
                        + "' and shape '" + (recorded.ShapeIdentity ?? string.Empty) + "'.";
                    return false;
                }

                var currentComponent = componentSnapshot.Entries.FirstOrDefault(entry =>
                    entry.IsAvailable
                    && entry.ClaimsLogicalSchemaKey
                    && string.Equals(entry.LogicalSchemaName, recorded.LogicalSchema, StringComparison.Ordinal)
                    && string.Equals(entry.ShapeIdentity, recorded.ShapeIdentity, StringComparison.Ordinal));
                if (currentComponent == null)
                {
                    failureReason = "Missing component '" + recorded.LogicalSchema
                        + "' with recorded shape '" + recorded.ShapeIdentity + "'.";
                    return false;
                }

                selectedComponents.Add(new SdkWireSchemaComponentIdentity
                {
                    Topic = recorded.Topic ?? string.Empty,
                    Encoding = recorded.Encoding,
                    LogicalSchema = currentComponent.LogicalSchemaName,
                    ShapeIdentity = currentComponent.ShapeIdentity
                });
            }

            return TryComputeCore(selected, selectedComponents, out hash);
        }

        internal static bool TryCompute(
            IEnumerable<SchemaEntry> schemaEntries,
            out string hash)
            => TryCompute(schemaEntries, Array.Empty<SdkWireSchemaComponentIdentity>(), out hash);

        internal static bool TryCompute(
            IEnumerable<SchemaEntry> schemaEntries,
            IReadOnlyList<SdkWireSchemaComponentIdentity> componentEntries,
            out string hash)
            => TryComputeCore(schemaEntries, componentEntries, out hash);

        private static bool TryComputeCore(
            IEnumerable<SchemaEntry> schemaEntries,
            IReadOnlyList<SdkWireSchemaComponentIdentity> componentEntries,
            out string hash,
            bool includeLegacyComponentSnapshot = false)
        {
            hash = string.Empty;
            if (schemaEntries == null || componentEntries == null)
                return false;

            var builder = new StringBuilder();
            foreach (var schema in schemaEntries
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Encoding, StringComparer.Ordinal))
            {
                builder.Append("schema|");
                Append(builder, schema.Name);
                Append(builder, schema.Encoding);
                Append(builder, schema.Content);
                Append(builder, schema.RawContent == null ? string.Empty : Convert.ToBase64String(schema.RawContent));
            }

            if (includeLegacyComponentSnapshot)
            {
                foreach (var entry in ComponentMessagePackCodecRegistry.CaptureSnapshot().Entries
                    .OrderBy(value => value.LogicalSchemaName, StringComparer.Ordinal)
                    .ThenBy(value => value.ClrType == null ? string.Empty : value.ClrType.FullName, StringComparer.Ordinal))
                {
                    builder.Append("component|");
                    Append(builder, entry.ClrType == null ? string.Empty : entry.ClrType.FullName);
                    Append(builder, entry.LogicalSchemaName);
                    Append(builder, entry.ShapeIdentity);
                    builder.Append(entry.IsAvailable ? '1' : '0').Append(entry.ClaimsLogicalSchemaKey ? '1' : '0');
                }
            }

            foreach (var entry in componentEntries
                .OrderBy(value => value.Topic, StringComparer.Ordinal)
                .ThenBy(value => value.Encoding, StringComparer.Ordinal)
                .ThenBy(value => value.LogicalSchema, StringComparer.Ordinal)
                .ThenBy(value => value.ShapeIdentity, StringComparer.Ordinal))
            {
                builder.Append("component|");
                Append(builder, entry.Topic);
                Append(builder, entry.Encoding);
                Append(builder, entry.LogicalSchema);
                Append(builder, entry.ShapeIdentity);
            }

            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                hash = BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant();
            }

            return true;
        }

        private static void Append(StringBuilder builder, string value)
        {
            value = value ?? string.Empty;
            builder.Append(value.Length).Append(':').Append(value).Append('|');
        }
    }
}
