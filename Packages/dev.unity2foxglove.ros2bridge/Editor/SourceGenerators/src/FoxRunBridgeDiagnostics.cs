// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ROS2 Bridge FoxRun source generator
// Purpose: Bridge-owned diagnostics for physical CDR contract projection.

using Microsoft.CodeAnalysis;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunBridgeDiagnostics
    {
        internal static readonly DiagnosticDescriptor UnsupportedDto =
            Create(
                "FOXBRG001",
                "Bridge custom DTO shape unsupported");

        internal static readonly DiagnosticDescriptor InvalidRosField =
            Create(
                "FOXBRG002",
                "Bridge ROS 2 field identifier invalid");

        internal static readonly DiagnosticDescriptor HostIdentity =
            Create(
                "FOXBRG003",
                "Bridge declaring host identity unsupported",
                "FoxRun declaring host identity cannot be represented by the Bridge partial-class contract: {0}");

        internal static DiagnosticDescriptor For(string id)
            => string.Equals(
                   id,
                   InvalidRosField.Id,
                   System.StringComparison.Ordinal)
                ? InvalidRosField
                : string.Equals(
                      id,
                      "FOXRUN623",
                      System.StringComparison.Ordinal)
                    ? HostIdentity
                : UnsupportedDto;

        private static DiagnosticDescriptor Create(
            string id,
            string title,
            string messageFormat = "{0}")
            => new DiagnosticDescriptor(
                id,
                title,
                messageFormat,
                "FoxRun.Bridge",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true);
    }
}
