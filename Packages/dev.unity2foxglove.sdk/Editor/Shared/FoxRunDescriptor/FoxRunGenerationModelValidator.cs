// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent validation for the Provider-neutral FoxRun model.


namespace Unity.FoxgloveSDK.Editor
{
    /// <remarks>Phase192 source sections: ValidationRules.cs, ValidationTopology.cs</remarks>
    public static partial class FoxRunGenerationModelValidator
    {
    }

    public sealed class FoxRunGenerationDiagnostic
    {
        public readonly string Id;
        public readonly string Severity;
        public readonly string Target;
        public readonly string MemberName;
        public readonly string Message;

        private FoxRunGenerationDiagnostic(
            string id,
            string severity,
            string target,
            string memberName,
            string message)
        {
            Id = id ?? string.Empty;
            Severity = severity ?? string.Empty;
            Target = target ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public static FoxRunGenerationDiagnostic Warning(
            string id,
            string target,
            string memberName,
            string message)
            => new FoxRunGenerationDiagnostic(
                id,
                "Warning",
                target,
                memberName,
                message);

        public static FoxRunGenerationDiagnostic Error(
            string id,
            string target,
            string memberName,
            string message)
            => new FoxRunGenerationDiagnostic(
                id,
                "Error",
                target,
                memberName,
                message);
    }
}
