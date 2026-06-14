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
        private static readonly string[] UnityNativeContainerPrefixes =
        {
            "NativeArray<",
            "NativeList<",
            "NativeHashMap<",
            "NativeMultiHashMap<",
            "NativeParallelHashMap<",
            "NativeParallelMultiHashMap<",
            "NativeSlice<",
            "NativeQueue<",
            "NativeReference<",
            "NativeText<",
            "Unity.Collections.NativeArray<",
            "Unity.Collections.NativeList<",
            "Unity.Collections.NativeHashMap<",
            "Unity.Collections.NativeMultiHashMap<",
            "Unity.Collections.NativeParallelHashMap<",
            "Unity.Collections.NativeParallelMultiHashMap<",
            "Unity.Collections.NativeSlice<",
            "Unity.Collections.NativeQueue<",
            "Unity.Collections.NativeReference<",
            "Unity.Collections.NativeText<"
        };

        public static IReadOnlyList<FoxRunGenerationDiagnostic> Validate(FoxRunGenerationModel model)
        {
            var diagnostics = new List<FoxRunGenerationDiagnostic>();
            foreach (var type in (model == null ? Array.Empty<FoxRunGenerationType>() : model.Types))
            {
                if (type.DeclaringType.IndexOf('<') >= 0 || type.DeclaringType.IndexOf('`') >= 0)
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", type.DeclaringType, "", "Generic FoxRun declaring types may be unsafe for IL2CPP contract governance."));

                foreach (var member in type.Members)
                    ValidateMember(member, diagnostics);

                ValidateTopicGroups(type, diagnostics);
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

            if (!IsKnownMemberKind(member.MemberKind))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN014", target, member.MemberName, "FoxRun member kind must be 'field' or 'property'."));

            if (IsInvalidConditionName(member.When))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN015", target, member.MemberName, "FoxRun When condition member name is invalid or missing."));

            if (IsInvalidConditionName(member.Unless))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN016", target, member.MemberName, "FoxRun Unless condition member name is invalid or missing."));

            if (!FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType))
            {
                var raw = member.RawObservedTypeName ?? string.Empty;
                var message = string.IsNullOrWhiteSpace(raw)
                    ? "FoxRun member has an empty type; the generator host produced no observed type name."
                    : IsUnityNativeContainerTypeName(raw)
                    ? "FoxRun member type '" + raw + "' is a Unity native container and is not supported "
                      + "as a FoxRun field; use a managed type instead."
                    : "FoxRun member type '" + raw + "' is not a canonical built-in contract type.";
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN006", target, member.MemberName, message));
            }

            if (IsUnsupportedGenericMember(member))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN007", target, member.MemberName, "Generic FoxRun member type may be unsafe for IL2CPP contract governance."));

            if (string.IsNullOrEmpty(member.Topic) || !member.Topic.StartsWith("/", StringComparison.Ordinal))
                diagnostics.Add(FoxRunGenerationDiagnostic.Error("FOXRUN008", target, member.MemberName, "FoxRun topic must be absolute and start with '/'."));

            if (member.HasNonFiniteRateHz)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "RateHz must be finite; use OnTrigger or a positive finite rate for periodic output."));
            else if (member.RateHz <= 0f && member.PublishMode != 3)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "RateHz <= 0 disables scheduled publishing; use OnTrigger or a positive rate for periodic output."));

            if (member.HasNonFiniteChangeEpsilon)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "ChangeEpsilon must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));

            if (member.HasNonFiniteForceIntervalSeconds)
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN009", target, member.MemberName, "ForceIntervalSeconds must be finite; non-finite policy values are not emitted into FoxRun descriptor evidence."));

            if (IsBinaryLike(member.RawObservedTypeName) || IsBinaryLike(member.EmissionTypeName) || IsBinaryLike(member.CanonicalType)
                || (member.IsArray && member.CanonicalType == "uint8"))
                diagnostics.Add(FoxRunGenerationDiagnostic.Warning("FOXRUN010", target, member.MemberName, "Binary/blob values are not supported in the FoxRun contract path."));
        }

        private static void ValidateTopicGroups(FoxRunGenerationType type, List<FoxRunGenerationDiagnostic> diagnostics)
        {
            var byTopic = type.Members
                .Where(member => !string.IsNullOrEmpty(member.Topic))
                .GroupBy(member => member.Topic, StringComparer.Ordinal);

            foreach (var group in byTopic)
            {
                var schemas = group
                    .Select(member => member.SchemaName)
                    .Where(schema => !string.IsNullOrEmpty(schema))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (schemas.Count > 1)
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN002",
                        group.Key,
                        "",
                        "Topic has conflicting SchemaName values across FoxRun members."));

                var collision = group
                    .GroupBy(member => member.MemberName.TrimStart('_'), StringComparer.Ordinal)
                    .FirstOrDefault(names => names.Count() > 1);
                if (collision != null)
                {
                    var first = collision.First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN003",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "FoxRun member names collide after stripping leading underscores for topic '" + group.Key + "'."));
                }

                var mixedPolicy = group.Select(member => member.PublishMode).Distinct().Count() > 1
                    || group.Select(member => member.ChangeEpsilon).Distinct().Count() > 1
                    || group.Select(member => member.ForceIntervalSeconds).Distinct().Count() > 1;
                if (mixedPolicy)
                {
                    var first = group.First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Warning(
                        "FOXRUN005",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed PublishMode, ChangeEpsilon, or ForceIntervalSeconds values."));
                }

                var mixedConditions = group.Select(member => member.When).Distinct(StringComparer.Ordinal).Count() > 1
                    || group.Select(member => member.Unless).Distinct(StringComparer.Ordinal).Count() > 1;
                if (mixedConditions)
                {
                    var first = group.First();
                    diagnostics.Add(FoxRunGenerationDiagnostic.Error(
                        "FOXRUN017",
                        first.DeclaringType + "." + first.MemberName,
                        first.MemberName,
                        "Topic '" + group.Key + "' has mixed When or Unless values."));
                }
            }
        }

        private static bool IsInvalidConditionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var value = name.Trim();
            if (value.EndsWith("()", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 2);
            if (value.Length == 0)
                return true;

            if (!IsIdentifierStart(value[0]))
                return true;
            for (var i = 1; i < value.Length; i++)
            {
                if (!IsIdentifierPart(value[i]))
                    return true;
            }

            return false;
        }

        private static bool IsIdentifierStart(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        private static bool IsIdentifierPart(char ch)
        {
            return ch == '_' || char.IsLetterOrDigit(ch);
        }

        private static bool IsUnsupportedGenericMember(FoxRunGenerationMember member)
        {
            if (IsSupportedNullableMember(member))
                return false;

            var looksGeneric = member.EmissionTypeName.IndexOf('<') >= 0
                               || member.RawObservedTypeName.IndexOf('`') >= 0;
            if (!looksGeneric)
                return false;

            return !member.IsArray || !FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType);
        }

        private static bool IsSupportedNullableMember(FoxRunGenerationMember member)
        {
            if (!FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(member.CanonicalType))
                return false;

            return FoxRunCanonicalTypeNormalizer.IsNullableType(member.EmissionTypeName)
                   || FoxRunCanonicalTypeNormalizer.IsNullableType(member.RawObservedTypeName);
        }

        private static bool IsBinaryLike(string typeName)
        {
            var name = FoxRunEmissionTypeNameFormatter.NormalizeCSharpTypeName(typeName);
            return name == "byte[]"
                   || name == "System.Byte[]"
                   || name == "uint8[]"
                   || name.IndexOf("System.IO.Stream", StringComparison.Ordinal) >= 0
                   || name.IndexOf("Memory<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("ReadOnlyMemory<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("Span<byte>", StringComparison.Ordinal) >= 0
                   || name.IndexOf("ReadOnlySpan<byte>", StringComparison.Ordinal) >= 0;
        }

        private static bool IsKnownMemberKind(string memberKind)
        {
            return string.Equals(memberKind, "field", StringComparison.Ordinal)
                   || string.Equals(memberKind, "property", StringComparison.Ordinal);
        }

        private static bool IsUnityNativeContainerTypeName(string rawTypeName)
        {
            if (string.IsNullOrEmpty(rawTypeName))
                return false;

            foreach (var prefix in UnityNativeContainerPrefixes)
            {
                if (rawTypeName.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
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
