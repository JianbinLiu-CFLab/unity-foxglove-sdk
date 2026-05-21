// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated FoxRun manifest metadata.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Generated runtime snapshot of the canonical FoxRun manifest.</summary>
    public sealed class FoxRunSchemaManifestInfo
    {
        public int ManifestVersion { get; }
        public string PackageName { get; }
        public string GeneratorName { get; }
        public int GeneratorMajorVersion { get; }
        public string GlobalManifestHash { get; }
        public string FoxRunManifestHash { get; }
        public IReadOnlyList<FoxRunSchemaTypeInfo> Types { get; }
        public int TypeCount { get; }
        public int ContractCount { get; }
        public int FieldCount { get; }

        public FoxRunSchemaManifestInfo(
            int manifestVersion,
            string packageName,
            string generatorName,
            int generatorMajorVersion,
            string globalManifestHash,
            string foxRunManifestHash,
            IReadOnlyList<FoxRunSchemaTypeInfo> types)
        {
            ManifestVersion = manifestVersion;
            PackageName = packageName ?? string.Empty;
            GeneratorName = generatorName ?? string.Empty;
            GeneratorMajorVersion = generatorMajorVersion;
            GlobalManifestHash = globalManifestHash ?? string.Empty;
            FoxRunManifestHash = foxRunManifestHash ?? string.Empty;
            Types = new List<FoxRunSchemaTypeInfo>(types ?? Array.Empty<FoxRunSchemaTypeInfo>()).AsReadOnly();
            TypeCount = Types.Count;

            var contractCount = 0;
            var fieldCount = 0;
            foreach (var type in Types)
            {
                if (type == null)
                    continue;

                contractCount += type.Contracts.Count;
                foreach (var contract in type.Contracts)
                {
                    if (contract != null)
                        fieldCount += contract.Fields.Count;
                }
            }

            ContractCount = contractCount;
            FieldCount = fieldCount;
        }
    }
}
