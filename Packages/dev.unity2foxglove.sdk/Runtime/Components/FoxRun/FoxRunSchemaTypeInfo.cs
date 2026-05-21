// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated FoxRun declaring-type metadata.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Generated metadata for one declaring type that owns FoxRun topics.</summary>
    public sealed class FoxRunSchemaTypeInfo
    {
        public string DeclaringType { get; }
        public IReadOnlyList<FoxRunSchemaContractInfo> Contracts { get; }

        public FoxRunSchemaTypeInfo(
            string declaringType,
            IReadOnlyList<FoxRunSchemaContractInfo> contracts)
        {
            DeclaringType = declaringType ?? string.Empty;
            Contracts = new List<FoxRunSchemaContractInfo>(contracts ?? Array.Empty<FoxRunSchemaContractInfo>()).AsReadOnly();
        }
    }
}
