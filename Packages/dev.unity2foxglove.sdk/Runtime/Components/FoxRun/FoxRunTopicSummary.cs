// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Read-only generated FoxRun topic policy summary for Inspector use.

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>One generated FoxRun topic and the effective session wire contract.</summary>
    public readonly struct FoxRunTopicSummary
    {
        public FoxRunTopicSummary(
            string declaringType,
            string topic,
            string direction,
            FoxRunEncoding declaredEncoding,
            FoxRunEncoding effectiveEncoding,
            string schemaName,
            string logicalSchemaName = "",
            bool available = true,
            string unavailableDiagnosticId = "",
            string unavailableReason = "")
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            Direction = direction ?? string.Empty;
            DeclaredEncoding = declaredEncoding;
            EffectiveEncoding = effectiveEncoding;
            SchemaName = schemaName ?? string.Empty;
            LogicalSchemaName = logicalSchemaName ?? string.Empty;
            Available = available;
            UnavailableDiagnosticId = unavailableDiagnosticId ?? string.Empty;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        public string DeclaringType { get; }
        public string Topic { get; }
        public string Direction { get; }
        public FoxRunEncoding DeclaredEncoding { get; }
        public FoxRunEncoding EffectiveEncoding { get; }
        public string SchemaName { get; }
        public string WireSchemaName => SchemaName;
        public string LogicalSchemaName { get; }
        public bool Available { get; }
        public string UnavailableDiagnosticId { get; }
        public string UnavailableReason { get; }
    }
}
