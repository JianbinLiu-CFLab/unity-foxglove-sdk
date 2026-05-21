// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: MCAP metadata envelope and replay guard helpers for generated FoxRun schema info.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Replay guard result states for FoxRun schema metadata stored in MCAP files.</summary>
    public enum FoxRunReplaySchemaGuardState
    {
        Match,
        MissingRecorded,
        MissingCurrent,
        MalformedRecorded,
        Mismatch
    }

    /// <summary>Comparison result for recorded and current FoxRun schema manifest hashes.</summary>
    public sealed class FoxRunReplaySchemaGuardResult
    {
        public FoxRunReplaySchemaGuardState State { get; }
        public bool IsBlocking { get; }
        public string Message { get; }
        public string RecordedGlobalManifestHash { get; }
        public string CurrentGlobalManifestHash { get; }

        public FoxRunReplaySchemaGuardResult(
            FoxRunReplaySchemaGuardState state,
            bool isBlocking,
            string message,
            string recordedGlobalManifestHash,
            string currentGlobalManifestHash)
        {
            State = state;
            IsBlocking = isBlocking;
            Message = message ?? string.Empty;
            RecordedGlobalManifestHash = recordedGlobalManifestHash ?? string.Empty;
            CurrentGlobalManifestHash = currentGlobalManifestHash ?? string.Empty;
        }
    }

    /// <summary>One FoxRun topic contract entry in the MCAP schema metadata envelope.</summary>
    public sealed class FoxRunSchemaMcapContractMetadata
    {
        [JsonProperty("topic", Order = 0)]
        public string Topic { get; set; }

        [JsonProperty("schemaName", Order = 1)]
        public string SchemaName { get; set; }

        [JsonProperty("encoding", Order = 2)]
        public string Encoding { get; set; }

        [JsonProperty("contractHash", Order = 3)]
        public string ContractHash { get; set; }

        [JsonProperty("bindingHash", Order = 4)]
        public string BindingHash { get; set; }

        [JsonProperty("policyHash", Order = 5)]
        public string PolicyHash { get; set; }
    }

    /// <summary>Deterministic JSON payload stored in the FoxRun MCAP metadata record.</summary>
    public sealed class FoxRunSchemaMcapMetadataRecord
    {
        [JsonProperty("schemaMetadataVersion", Order = 0)]
        public int SchemaMetadataVersion { get; set; }

        [JsonProperty("manifestVersion", Order = 1)]
        public int ManifestVersion { get; set; }

        [JsonProperty("generatorVersion", Order = 2)]
        public string GeneratorVersion { get; set; }

        [JsonProperty("generatorMajorVersion", Order = 3)]
        public int GeneratorMajorVersion { get; set; }

        [JsonProperty("globalManifestHash", Order = 4)]
        public string GlobalManifestHash { get; set; }

        [JsonProperty("manifestHash", Order = 5)]
        public string ManifestHash { get; set; }

        [JsonProperty("typeCount", Order = 6)]
        public int TypeCount { get; set; }

        [JsonProperty("contractCount", Order = 7)]
        public int ContractCount { get; set; }

        [JsonProperty("fieldCount", Order = 8)]
        public int FieldCount { get; set; }

        [JsonProperty("contracts", Order = 9)]
        public List<FoxRunSchemaMcapContractMetadata> Contracts { get; set; }
    }

    /// <summary>Builds and evaluates the FoxRun schema metadata record carried by MCAP files.</summary>
    public static class FoxRunSchemaMcapMetadata
    {
        public const string MetadataName = "unity2foxglove.foxrun.schema";
        public const int SchemaMetadataVersion = 1;

        public static bool TryCreateJson(FoxRunSchemaManifestInfo manifest, out string json)
        {
            json = null;
            if (!HasUsableHash(manifest))
                return false;

            var record = CreateRecord(manifest);
            json = JsonConvert.SerializeObject(record, Formatting.None);
            return true;
        }

        public static FoxRunSchemaMcapMetadataRecord CreateRecord(FoxRunSchemaManifestInfo manifest)
        {
            if (!HasUsableHash(manifest))
                throw new ArgumentException("FoxRun schema manifest info is missing a global manifest hash.", nameof(manifest));

            var contracts = new List<FoxRunSchemaMcapContractMetadata>();
            foreach (var type in manifest.Types ?? Array.Empty<FoxRunSchemaTypeInfo>())
            {
                if (type?.Contracts == null)
                    continue;

                foreach (var contract in type.Contracts)
                {
                    if (contract == null)
                        continue;

                    contracts.Add(new FoxRunSchemaMcapContractMetadata
                    {
                        Topic = contract.Topic ?? string.Empty,
                        SchemaName = contract.SchemaName ?? string.Empty,
                        Encoding = contract.Encoding ?? string.Empty,
                        ContractHash = contract.ContractHash ?? string.Empty,
                        BindingHash = contract.BindingHash ?? string.Empty,
                        PolicyHash = contract.PolicyHash ?? string.Empty
                    });
                }
            }

            contracts.Sort(CompareContracts);

            return new FoxRunSchemaMcapMetadataRecord
            {
                SchemaMetadataVersion = SchemaMetadataVersion,
                ManifestVersion = manifest.ManifestVersion,
                GeneratorVersion = FormatGeneratorVersion(manifest.GeneratorMajorVersion),
                GeneratorMajorVersion = manifest.GeneratorMajorVersion,
                GlobalManifestHash = manifest.GlobalManifestHash,
                ManifestHash = manifest.FoxRunManifestHash,
                TypeCount = manifest.TypeCount,
                ContractCount = manifest.ContractCount,
                FieldCount = manifest.FieldCount,
                Contracts = contracts
            };
        }

        public static bool TryParseJson(
            string json,
            out FoxRunSchemaMcapMetadataRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "metadata value is empty";
                return false;
            }

            try
            {
                record = JsonConvert.DeserializeObject<FoxRunSchemaMcapMetadataRecord>(json);
            }
            catch (Exception ex) when (ex is JsonException || ex is ArgumentException)
            {
                error = ex.Message;
                return false;
            }

            if (record == null)
            {
                error = "metadata JSON did not deserialize to a record";
                return false;
            }

            if (record.SchemaMetadataVersion != SchemaMetadataVersion)
            {
                error = "unsupported schema metadata version: " + record.SchemaMetadataVersion.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.GlobalManifestHash))
            {
                error = "globalManifestHash is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.ManifestHash))
            {
                error = "manifestHash is missing";
                return false;
            }

            if (record.Contracts == null)
            {
                error = "contracts array is missing";
                return false;
            }

            return true;
        }

        public static FoxRunReplaySchemaGuardResult CreateMissingRecordedResult()
            => new FoxRunReplaySchemaGuardResult(
                FoxRunReplaySchemaGuardState.MissingRecorded,
                false,
                "Recorded MCAP does not contain FoxRun schema metadata; replay will continue without schema hash enforcement.",
                string.Empty,
                string.Empty);

        public static FoxRunReplaySchemaGuardResult CreateMalformedRecordedResult(string detail)
            => new FoxRunReplaySchemaGuardResult(
                FoxRunReplaySchemaGuardState.MalformedRecorded,
                false,
                "Recorded FoxRun schema metadata is malformed; replay will continue without schema hash enforcement. " +
                (string.IsNullOrWhiteSpace(detail) ? string.Empty : detail),
                string.Empty,
                string.Empty);

        public static FoxRunReplaySchemaGuardResult EvaluateRecordedJson(
            string recordedJson,
            FoxRunSchemaManifestInfo current)
        {
            if (!TryParseJson(recordedJson, out var recorded, out var error))
                return CreateMalformedRecordedResult(error);

            return Evaluate(recorded, current);
        }

        public static FoxRunReplaySchemaGuardResult Evaluate(
            FoxRunSchemaMcapMetadataRecord recorded,
            FoxRunSchemaManifestInfo current)
        {
            if (recorded == null)
                return CreateMissingRecordedResult();

            var recordedHash = recorded.GlobalManifestHash ?? string.Empty;
            if (string.IsNullOrWhiteSpace(recordedHash))
                return CreateMalformedRecordedResult("globalManifestHash is missing");

            if (!HasUsableHash(current))
            {
                return new FoxRunReplaySchemaGuardResult(
                    FoxRunReplaySchemaGuardState.MissingCurrent,
                    false,
                    "Current runtime does not expose generated FoxRun schema info; replay will continue without schema hash enforcement.",
                    recordedHash,
                    string.Empty);
            }

            var currentHash = current.GlobalManifestHash;
            if (string.Equals(recordedHash, currentHash, StringComparison.Ordinal))
            {
                return new FoxRunReplaySchemaGuardResult(
                    FoxRunReplaySchemaGuardState.Match,
                    false,
                    "Recorded FoxRun schema metadata matches the current runtime manifest hash.",
                    recordedHash,
                    currentHash);
            }

            return new FoxRunReplaySchemaGuardResult(
                FoxRunReplaySchemaGuardState.Mismatch,
                true,
                "FoxRun replay schema mismatch.\n" +
                "Recorded: " + ShortHash(recordedHash) + "\n" +
                "Current:  " + ShortHash(currentHash) + "\n" +
                "Replay blocked.",
                recordedHash,
                currentHash);
        }

        private static bool HasUsableHash(FoxRunSchemaManifestInfo manifest)
            => manifest != null && !string.IsNullOrWhiteSpace(manifest.GlobalManifestHash);

        private static string FormatGeneratorVersion(int majorVersion)
            => majorVersion.ToString(CultureInfo.InvariantCulture) + ".0.0";

        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "<missing>";

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static int CompareContracts(
            FoxRunSchemaMcapContractMetadata left,
            FoxRunSchemaMcapContractMetadata right)
        {
            var compare = string.Compare(left?.Topic, right?.Topic, StringComparison.Ordinal);
            if (compare != 0)
                return compare;

            compare = string.Compare(left?.SchemaName, right?.SchemaName, StringComparison.Ordinal);
            if (compare != 0)
                return compare;

            return string.Compare(left?.Encoding, right?.Encoding, StringComparison.Ordinal);
        }
    }
}
