// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Evaluates FoxRun schema identity metadata embedded in an MCAP file
// against the current runtime's schema identity to detect potential mismatch.

using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;

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
        {
            var metadata = replayEngine?.FindMetadata(FoxRunSchemaMcapMetadata.MetadataName);
            if (metadata == null)
                return FoxRunSchemaMcapMetadata.CreateMissingRecordedResult();

            if (metadata.Metadata == null || !metadata.Metadata.TryGetValue("value", out var value))
                return FoxRunSchemaMcapMetadata.CreateMalformedRecordedResult(
                    "Metadata record is missing the value entry.");

            return FoxRunSchemaMcapMetadata.EvaluateRecordedJson(
                value, FoxRunSchemaInfoRegistry.Current);
        }
    }
}
