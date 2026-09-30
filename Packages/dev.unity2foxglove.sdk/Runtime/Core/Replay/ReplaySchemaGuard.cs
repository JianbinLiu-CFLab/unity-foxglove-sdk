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
                return null;

            if (metadata.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MalformedRecorded,
                    identityMode,
                    "SDK-wide wire-schema metadata is missing the value entry.",
                    string.Empty,
                    string.Empty);

            if (!SdkWireSchemaMcapMetadata.TryParseJson(value, out var recordedHash, out var error))
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MalformedRecorded,
                    identityMode,
                    "SDK-wide wire-schema metadata is malformed. " + error,
                    string.Empty,
                    string.Empty);

            if (!SdkWireSchemaIdentity.TryCompute(schemaRegistry, out var currentHash))
                return CreateSdkResult(
                    FoxRunReplaySchemaGuardState.MissingCurrent,
                    identityMode,
                    "Current runtime does not expose an SDK-wide wire-schema identity.",
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

        private static FoxRunReplaySchemaGuardResult CreateSdkResult(
            FoxRunReplaySchemaGuardState state,
            SchemaIdentityMode identityMode,
            string message,
            string recordedHash,
            string currentHash)
        {
            var isBlocking = identityMode == SchemaIdentityMode.Strict
                || identityMode == SchemaIdentityMode.Compatible;
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

            var builder = new StringBuilder();
            var schemas = snapshot.GetSchemaSnapshot() ?? Array.Empty<SchemaEntry>();
            foreach (var schema in schemas
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Encoding, StringComparer.Ordinal))
            {
                builder.Append("schema|");
                Append(builder, schema.Name);
                Append(builder, schema.Encoding);
                Append(builder, schema.Content);
                Append(builder, schema.RawContent == null ? string.Empty : Convert.ToBase64String(schema.RawContent));
            }

            var componentSnapshot = ComponentMessagePackCodecRegistry.CaptureSnapshot();
            foreach (var entry in componentSnapshot.Entries
                .OrderBy(value => value.LogicalSchemaName, StringComparer.Ordinal)
                .ThenBy(value => value.ClrType == null ? string.Empty : value.ClrType.FullName, StringComparer.Ordinal))
            {
                builder.Append("component|");
                Append(builder, entry.ClrType == null ? string.Empty : entry.ClrType.FullName);
                Append(builder, entry.LogicalSchemaName);
                Append(builder, entry.ShapeIdentity);
                builder.Append(entry.IsAvailable ? '1' : '0').Append(entry.ClaimsLogicalSchemaKey ? '1' : '0');
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
