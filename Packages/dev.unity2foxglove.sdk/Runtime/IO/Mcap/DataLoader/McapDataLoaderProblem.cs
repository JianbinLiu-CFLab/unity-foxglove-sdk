// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/DataLoader
// Purpose: Problem diagnostics surfaced by the local MCAP DataLoader facade.

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Severity for local MCAP DataLoader diagnostics.</summary>
    public enum McapDataLoaderProblemSeverity
    {
        /// <summary>Informational diagnostic.</summary>
        Info,

        /// <summary>Recoverable condition that callers may want to surface.</summary>
        Warning,

        /// <summary>High-severity diagnostic that does not stop raw local loading.</summary>
        Error
    }

    /// <summary>Diagnostic emitted while initializing a local MCAP data source.</summary>
    public sealed class McapDataLoaderProblem
    {
        /// <summary>Severity assigned by the local DataLoader facade.</summary>
        public McapDataLoaderProblemSeverity Severity;

        /// <summary>Human-readable diagnostic message.</summary>
        public string Message;

        /// <summary>Optional caller-facing remediation hint.</summary>
        public string Tip;

        /// <summary>Stable diagnostic code for tests and UI grouping.</summary>
        public string Code;

        /// <summary>Creates an empty informational diagnostic.</summary>
        public McapDataLoaderProblem()
        {
            Severity = McapDataLoaderProblemSeverity.Info;
            Message = string.Empty;
            Tip = string.Empty;
            Code = string.Empty;
        }

        /// <summary>Creates a diagnostic with a stable code and optional remediation tip.</summary>
        public McapDataLoaderProblem(
            McapDataLoaderProblemSeverity severity,
            string message,
            string code,
            string tip = "")
        {
            Severity = severity;
            Message = message ?? string.Empty;
            Code = code ?? string.Empty;
            Tip = tip ?? string.Empty;
        }
    }
}
