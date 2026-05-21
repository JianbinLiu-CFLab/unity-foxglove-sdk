// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTO for generated FoxRun schema field metadata.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Generated metadata for one field in a FoxRun topic contract.</summary>
    public sealed class FoxRunSchemaFieldInfo
    {
        public string JsonName { get; }
        public string MemberName { get; }
        public string MemberKind { get; }
        public string Type { get; }
        public bool Nullable { get; }
        public bool Array { get; }

        public FoxRunSchemaFieldInfo(
            string jsonName,
            string memberName,
            string memberKind,
            string type,
            bool nullable,
            bool array)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            MemberKind = memberKind ?? string.Empty;
            Type = type ?? string.Empty;
            Nullable = nullable;
            Array = array;
        }
    }
}
