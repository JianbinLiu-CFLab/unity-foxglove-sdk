// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Host-independent FoxRun generation-model diagnostics.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunGenerationModelValidator
    {
        public static IReadOnlyList<FoxRunGenerationDiagnostic> Validate(FoxRunGenerationModel model)
        {
            var diagnostics = new List<FoxRunGenerationDiagnostic>();
            foreach (var type in (model == null ? Array.Empty<FoxRunGenerationType>() : model.Types))
            {
                if (type.DeclaringType.IndexOf('<') >= 0 || type.DeclaringType.IndexOf('`') >= 0)
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", type.DeclaringType, "", "Generic FoxRun declaring types may be unsafe for IL2CPP contract governance."));

                foreach (var member in type.Members)
                    ValidateMember(member, diagnostics);
            }
            return diagnostics;
        }

        private static void ValidateMember(FoxRunGenerationMember member, List<FoxRunGenerationDiagnostic> diagnostics)
        {
            var target = member.DeclaringType + "." + member.MemberName;

            if (string.IsNullOrWhiteSpace(member.ClassName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN011", target, member.MemberName, "FoxRun declaring class name is required."));

            if (string.IsNullOrWhiteSpace(member.MemberName))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN012", target, member.MemberName, "FoxRun member name is required."));

            if (member.PublishMode < 0 || member.PublishMode > 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN013", target, member.MemberName, "FoxRun publish mode must be between 0 and 3."));

            if (!FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN006", target, member.MemberName, "FoxRun member type '" + member.RawObservedTypeName + "' is not a canonical built-in contract type."));

            if (IsUnsupportedGenericMember(member))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", target, member.MemberName, "Generic FoxRun member type may be unsafe for IL2CPP contract governance."));

            if (string.IsNullOrEmpty(member.Topic) || !member.Topic.StartsWith("/", StringComparison.Ordinal))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN008", target, member.MemberName, "FoxRun topic must be absolute and start with '/'."));

            if (member.RateHz <= 0f && member.PublishMode != 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "RateHz <= 0 disables scheduled publishing; use OnTrigger or a positive rate for periodic output."));

            if (IsBinaryLike(member.RawObservedTypeName) || IsBinaryLike(member.EmissionTypeName) || IsBinaryLike(member.CanonicalType)
                || (member.IsArray && member.CanonicalType == "uint8"))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN010", target, member.MemberName, "Binary/blob values are not supported in the FoxRun contract path."));
        }

        private static bool IsUnsupportedGenericMember(FoxRunGenerationMember member)
        {
            var looksGeneric = member.EmissionTypeName.IndexOf('<') >= 0
                               || member.RawObservedTypeName.IndexOf('`') >= 0;
            if (!looksGeneric)
                return false;

            return !member.IsArray || !FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType);
        }

        private static bool IsBinaryLike(string typeName)
        {
            var name = typeName ?? string.Empty;
            return name == "byte[]"
                   || name == "System.Byte[]"
                   || name == "uint8[]"
                   || name.IndexOf("System.IO.Stream", StringComparison.Ordinal) >= 0
                   || name.IndexOf("Memory<System.Byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("ReadOnlyMemory<System.Byte>", StringComparison.Ordinal) >= 0;
        }
    }

    public sealed class FoxRunGenerationDiagnostic
    {
        public readonly string Id;
        public readonly string Severity;
        public readonly string Target;
        public readonly string MemberName;
        public readonly string Message;

        private FoxRunGenerationDiagnostic(string id, string severity, string target, string memberName, string message)
        {
            Id = id ?? string.Empty;
            Severity = severity ?? string.Empty;
            Target = target ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public static FoxRunGenerationDiagnostic Warning(string id, string target, string memberName, string message)
        {
            return new FoxRunGenerationDiagnostic(id, "Warning", target, memberName, message);
        }

        public static FoxRunGenerationDiagnostic Error(string id, string target, string memberName, string message)
        {
            return new FoxRunGenerationDiagnostic(id, "Error", target, memberName, message);
        }
    }
}
