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
            FoxRunWireEncoding declaredEncoding,
            FoxRunWireEncoding effectiveEncoding,
            string schemaName)
        {
            DeclaringType = declaringType ?? string.Empty;
            Topic = topic ?? string.Empty;
            Direction = direction ?? string.Empty;
            DeclaredEncoding = declaredEncoding;
            EffectiveEncoding = effectiveEncoding;
            SchemaName = schemaName ?? string.Empty;
        }

        public string DeclaringType { get; }
        public string Topic { get; }
        public string Direction { get; }
        public FoxRunWireEncoding DeclaredEncoding { get; }
        public FoxRunWireEncoding EffectiveEncoding { get; }
        public string SchemaName { get; }
    }
}
