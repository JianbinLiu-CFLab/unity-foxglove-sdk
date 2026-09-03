// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.SourceGenerators
// Purpose: R2FU-owned FoxRun analyzer diagnostics.

using Microsoft.CodeAnalysis;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunR2fuDiagnostics
    {
        public static readonly DiagnosticDescriptor InvalidRoute =
            Create("FOXR2F001", "R2FU route invalid");
        public static readonly DiagnosticDescriptor MessageIdentity =
            Create("FOXR2F002", "ROS message identity invalid");
        public static readonly DiagnosticDescriptor MessageConstructor =
            Create("FOXR2F003", "ROS message constructor invalid");
        public static readonly DiagnosticDescriptor MessageNamespace =
            Create("FOXR2F004", "ROS message namespace invalid");
        public static readonly DiagnosticDescriptor SchemaMismatch =
            Create("FOXR2F005", "ROS schema identity mismatch");
        public static readonly DiagnosticDescriptor MessageShape =
            Create("FOXR2F006", "ROS message shape unsupported");
        public static readonly DiagnosticDescriptor MissingNativeReference =
            Create("FOXR2F007", "R2FU native assembly reference missing");
        public static readonly DiagnosticDescriptor DuplexContract =
            Create("FOXR2F008", "R2FU duplex contract invalid");
        public static readonly DiagnosticDescriptor UnsupportedDto =
            Create("FOXR2F009", "Custom ROS DTO shape unsupported");
        public static readonly DiagnosticDescriptor NonConstructibleDto =
            Create("FOXR2F010", "Custom ROS DTO constructor missing");
        public static readonly DiagnosticDescriptor NonWritableDto =
            Create("FOXR2F011", "Custom ROS DTO member not writable");
        public static readonly DiagnosticDescriptor InvalidTargets =
            Create("FOXR2F012", "R2FU publish route invalid");
        public static readonly DiagnosticDescriptor InvalidDirectionalRoute =
            Create("FOXR2F013", "R2FU directional route invalid");
        public static readonly DiagnosticDescriptor InvalidQos =
            Create("FOXR2F014", "R2FU QoS invalid");
        public static readonly DiagnosticDescriptor QosRequiresR2fu =
            Create("FOXR2F015", "R2FU QoS requires an R2FU direction");
        public static readonly DiagnosticDescriptor MixedDirectionalQos =
            Create("FOXR2F016", "R2FU directional QoS mismatch");
        public static readonly DiagnosticDescriptor HostIdentity =
            Create(
                "FOXR2F017",
                "R2FU declaring host identity unsupported",
                "FoxRun declaring host identity cannot be represented by the R2FU partial-class contract: {0}");

        public static bool TryGet(
            string id,
            out DiagnosticDescriptor descriptor)
        {
            switch (id)
            {
                case "FOXR2F001": descriptor = InvalidRoute; return true;
                case "FOXR2F002": descriptor = MessageIdentity; return true;
                case "FOXR2F003": descriptor = MessageConstructor; return true;
                case "FOXR2F004": descriptor = MessageNamespace; return true;
                case "FOXR2F005": descriptor = SchemaMismatch; return true;
                case "FOXR2F006": descriptor = MessageShape; return true;
                case "FOXR2F007": descriptor = MissingNativeReference; return true;
                case "FOXR2F008": descriptor = DuplexContract; return true;
                case "FOXR2F009": descriptor = UnsupportedDto; return true;
                case "FOXR2F010": descriptor = NonConstructibleDto; return true;
                case "FOXR2F011": descriptor = NonWritableDto; return true;
                case "FOXR2F012": descriptor = InvalidTargets; return true;
                case "FOXR2F013": descriptor = InvalidDirectionalRoute; return true;
                case "FOXR2F014": descriptor = InvalidQos; return true;
                case "FOXR2F015": descriptor = QosRequiresR2fu; return true;
                case "FOXR2F016": descriptor = MixedDirectionalQos; return true;
                case "FOXRUN623": descriptor = HostIdentity; return true;
                default:
                    descriptor = null;
                    return false;
            }
        }

        private static DiagnosticDescriptor Create(
            string id,
            string title,
            string messageFormat = "{0}")
            => new DiagnosticDescriptor(
                id,
                title,
                messageFormat,
                "FoxRun.R2FU",
                DiagnosticSeverity.Error,
                true);
    }
}
