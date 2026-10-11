// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/FoxRun/Decomposed/Ros2CustomCdrEmitter

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.FoxgloveSDK.Editor;

namespace Unity2Foxglove.Ros2Bridge.Editor
{
    internal static partial class FoxRunBridgeSourceEmitter
    {
        private static string RosType(
            FoxRunTypeShape shape,
            BridgeDtoShape nested)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object)
                return nested?.PayloadIdentity ?? string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Enum)
                return string.IsNullOrEmpty(shape.CanonicalType)
                    ? "int32"
                    : shape.CanonicalType;
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            switch (shape.CanonicalType)
            {
                case "bool":
                case "uint8":
                case "int8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "float32":
                case "float64":
                case "string":
                    return shape.CanonicalType;
                default:
                    return string.Empty;
            }
        }

        private static string ClrTypeName(FoxRunTypeShape shape)
        {
            if (shape == null)
                return string.Empty;
            if (shape.Kind == FoxRunTypeShapeKind.Object
                || shape.Kind == FoxRunTypeShapeKind.Enum)
            {
                return NullableTypeName(
                    shape.TypeName,
                    shape.Nullable
                    && shape.Kind == FoxRunTypeShapeKind.Enum);
            }
            if (shape.Kind != FoxRunTypeShapeKind.Canonical)
                return string.Empty;

            string typeName;
            switch (shape.CanonicalType)
            {
                case "bool": typeName = "System.Boolean"; break;
                case "uint8": typeName = "System.Byte"; break;
                case "int8": typeName = "System.SByte"; break;
                case "int16": typeName = "System.Int16"; break;
                case "uint16": typeName = "System.UInt16"; break;
                case "int32": typeName = "System.Int32"; break;
                case "uint32": typeName = "System.UInt32"; break;
                case "int64": typeName = "System.Int64"; break;
                case "uint64": typeName = "System.UInt64"; break;
                case "float32": typeName = "System.Single"; break;
                case "float64": typeName = "System.Double"; break;
                case "string": return "System.String";
                default: return string.Empty;
            }
            return NullableTypeName(typeName, shape.Nullable);
        }

        private static string NullableTypeName(
            string typeName,
            bool nullable)
            => nullable
                ? "System.Nullable<" + typeName + ">"
                : typeName ?? string.Empty;

        private static string BuildCanonicalIdentity(
            string typeName,
            IEnumerable<BridgeDtoMemberShape> members)
        {
            var builder = new StringBuilder();
            AppendLengthFramed(builder, typeName);
            foreach (var member in members)
            {
                AppendLengthFramed(builder, member.Name);
                AppendLengthFramed(builder, member.RosFieldName);
                AppendLengthFramed(builder, member.Kind.ToString());
                AppendLengthFramed(builder, member.FullyQualifiedTypeName);
                AppendLengthFramed(builder, member.RosType);
                AppendLengthFramed(builder, member.SequenceElementTypeName);
                AppendLengthFramed(builder, member.NestedShapeIdentity);
                AppendLengthFramed(
                    builder,
                    member.HasPresence ? "1" : "0");
                AppendLengthFramed(
                    builder,
                    member.SequenceRepresentation.ToString());
            }
            return builder.ToString();
        }

        private static string BuildPayloadIdentity(
            string typeName,
            string canonicalIdentity)
        {
            var simpleName = typeName ?? string.Empty;
            var dot = simpleName.LastIndexOf('.');
            if (dot >= 0)
                simpleName = simpleName.Substring(dot + 1);
            var pascal = ToPascalIdentifier(simpleName);
            if (string.IsNullOrEmpty(pascal))
                pascal = "FoxRunPayload";
            return pascal + Fnv1a64Hex(
                (typeName ?? string.Empty).ToUpperInvariant()
                + "|"
                + (canonicalIdentity ?? string.Empty));
        }

        private static string Fnv1a64Hex(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= prime;
            }
            return hash
                .ToString("X16", CultureInfo.InvariantCulture)
                .Substring(0, 12);
        }

        private static void AppendLengthFramed(
            StringBuilder builder,
            string value)
        {
            value ??= string.Empty;
            builder.Append(
                value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':').Append(value).Append('|');
        }

        private static string ToRosFieldName(string value)
        {
            value ??= string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (char.IsLetterOrDigit(current))
                {
                    var previous = index > 0 ? value[index - 1] : '\0';
                    var next = index + 1 < value.Length
                        ? value[index + 1]
                        : '\0';
                    var needsSeparator = index > 0
                        && char.IsUpper(current)
                        && (char.IsLower(previous)
                            || char.IsDigit(previous)
                            || (char.IsUpper(previous)
                                && char.IsLower(next)));
                    if (needsSeparator
                        && builder.Length > 0
                        && builder[builder.Length - 1] != '_')
                    {
                        builder.Append('_');
                    }
                    builder.Append(char.ToLowerInvariant(current));
                }
                else if (builder.Length > 0
                         && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }
            return builder.ToString().Trim('_');
        }

        private static bool TryValidateRosFieldNames(
            FoxRunTypeShape shape,
            string path,
            ISet<FoxRunTypeShape> visited,
            out string reason)
        {
            reason = string.Empty;
            if (shape == null)
                return true;
            if (shape.Kind == FoxRunTypeShapeKind.Collection)
            {
                return TryValidateRosFieldNames(
                    shape.ElementShape,
                    path + "[]",
                    visited,
                    out reason);
            }
            if (shape.Kind != FoxRunTypeShapeKind.Object
                || !visited.Add(shape))
            {
                return true;
            }

            var rosNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in shape.Fields
                         .OrderBy(
                             value => value?.MemberName,
                             StringComparer.Ordinal))
            {
                if (field == null)
                    continue;

                var rosName = ToRosFieldName(field.MemberName);
                var fieldPath = (path ?? string.Empty)
                                + "."
                                + field.MemberName;
                if (!IsValidRosFieldName(rosName))
                {
                    reason = "Bridge custom DTO member '"
                             + fieldPath
                             + "' lowers to invalid ROS 2 field identifier '"
                             + rosName
                             + "'.";
                    return false;
                }
                if (rosName.StartsWith(
                        "foxrun_",
                        StringComparison.Ordinal))
                {
                    reason = "Bridge custom DTO member '"
                             + fieldPath
                             + "' uses the reserved ROS 2 field prefix 'foxrun_'.";
                    return false;
                }
                if (!rosNames.Add(rosName))
                {
                    reason = "Bridge custom DTO member '"
                             + fieldPath
                             + "' collides after lowering to ROS 2 field identifier '"
                             + rosName
                             + "'.";
                    return false;
                }
                if (!TryValidateRosFieldNames(
                        field.TypeShape,
                        fieldPath,
                        visited,
                        out reason))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsValidRosFieldName(string value)
        {
            if (string.IsNullOrEmpty(value)
                || value[0] < 'a'
                || value[0] > 'z')
            {
                return false;
            }

            var previousUnderscore = false;
            for (var index = 1; index < value.Length; index++)
            {
                var current = value[index];
                if ((current >= 'a' && current <= 'z')
                    || (current >= '0' && current <= '9'))
                {
                    previousUnderscore = false;
                    continue;
                }
                if (current != '_'
                    || previousUnderscore
                    || index == value.Length - 1)
                {
                    return false;
                }
                previousUnderscore = true;
            }
            return true;
        }

        private static string ToPascalIdentifier(string value)
        {
            value ??= string.Empty;
            var builder = new StringBuilder(value.Length);
            var capitalize = true;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character))
                {
                    capitalize = true;
                    continue;
                }
                if (builder.Length == 0 && char.IsDigit(character))
                    builder.Append('N');
                builder.Append(
                    capitalize
                        ? char.ToUpperInvariant(character)
                        : character);
                capitalize = false;
            }
            return builder.ToString();
        }

        private static bool IsNullable(string typeName)
        {
            var value = (typeName ?? string.Empty).Trim();
            return value.EndsWith("?", StringComparison.Ordinal)
                   || value.StartsWith("System.Nullable<", StringComparison.Ordinal)
                   || value.StartsWith("Nullable<", StringComparison.Ordinal);
        }

        private static bool TryUnwrapNullable(string typeName, out string elementType)
        {
            var value = (typeName ?? string.Empty).Trim();
            if (value.EndsWith("?", StringComparison.Ordinal))
            {
                elementType = value.Substring(0, value.Length - 1);
                return elementType.Length > 0;
            }

            const string systemPrefix = "System.Nullable<";
            const string prefix = "Nullable<";
            var matchedPrefix = value.StartsWith(systemPrefix, StringComparison.Ordinal)
                ? systemPrefix
                : value.StartsWith(prefix, StringComparison.Ordinal)
                    ? prefix
                    : null;
            if (matchedPrefix != null && value.EndsWith(">", StringComparison.Ordinal))
            {
                elementType = value.Substring(
                    matchedPrefix.Length,
                    value.Length - matchedPrefix.Length - 1);
                return elementType.Length > 0;
            }

            elementType = string.Empty;
            return false;
        }

        private static string StripArray(string rosType)
        {
            var value = rosType ?? string.Empty;
            var bracket = value.IndexOf('[');
            return bracket < 0 ? value : value.Substring(0, bracket);
        }

        private static string GlobalTypeName(string typeName)
        {
            var escaped = IdentifierUtils.EscapeTypeName(typeName);
            return string.IsNullOrWhiteSpace(escaped)
                   || escaped.StartsWith("global::", StringComparison.Ordinal)
                ? escaped
                : "global::" + escaped;
        }

        private static string EscapeIdentifier(string value)
            => IdentifierUtils.EscapeIdentifier(value);

        private static string CSharpStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < ' ')
                            escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        else
                            escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }
    }
}
