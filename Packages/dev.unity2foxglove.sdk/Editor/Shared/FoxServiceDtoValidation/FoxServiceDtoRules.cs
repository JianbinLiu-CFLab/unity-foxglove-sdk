// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Shared DTO validation constants for declarative FoxService analyzers.

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceDtoRules
    {
        public const int MaxDepth = 32;
        public const string WarningDiagnosticId = "FOXSERVICE007";
        public const string CycleDiagnosticId = "FOXSERVICE008";

        public const string RequestSide = "request";
        public const string ResponseSide = "response";

        public static string UnsupportedDiagnosticId(string side)
            => side == RequestSide ? "FOXSERVICE003" : "FOXSERVICE004";
    }
}
