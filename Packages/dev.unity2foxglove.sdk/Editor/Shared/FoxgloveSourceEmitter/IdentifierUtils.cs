// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Sanitizes and escapes identifiers for use in generated C# source code.
    /// </summary>
    internal static class IdentifierUtils
    {
        /// <summary>
        /// Sanitizes a value into a valid C# identifier: replaces disallowed
        /// characters with underscores and prepends an underscore when the
        /// value starts with a digit.
        /// </summary>
        internal static string SanitizeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Member";

            var sb = new StringBuilder(value.Length + 1);
            if (!IsIdentifierStart(value[0]))
                sb.Append('_');

            foreach (var ch in value)
                sb.Append(IsIdentifierPart(ch) ? ch : '_');

            return sb.ToString();
        }

        /// <summary>
        /// Sanitizes a value into a safe file-name stem: replaces any
        /// character that is not a valid C# identifier part with an underscore.
        /// </summary>
        internal static string SanitizeFileStem(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "FoxRunSource";

            var sb = new StringBuilder(value.Length + 1);
            foreach (var ch in value)
                sb.Append(IsIdentifierPart(ch) ? ch : '_');

            return sb.Length == 0 ? "FoxRunSource" : sb.ToString();
        }

        /// <summary>
        /// Escapes an identifier with the <c>@</c> prefix when it collides with
        /// a C# keyword. Leaves already-escaped identifiers unchanged.
        /// </summary>
        internal static string EscapeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var bare = value.StartsWith("@", StringComparison.Ordinal) ? value.Substring(1) : value;
            return IsCSharpKeyword(bare) ? "@" + bare : value;
        }

        /// <summary>
        /// Returns true when the given value is a C# reserved keyword.
        /// </summary>
        internal static bool IsCSharpKeyword(string value)
        {
            switch (value)
            {
                case "abstract":
                case "as":
                case "base":
                case "bool":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "checked":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "goto":
                case "if":
                case "implicit":
                case "in":
                case "int":
                case "interface":
                case "internal":
                case "is":
                case "lock":
                case "long":
                case "namespace":
                case "new":
                case "null":
                case "object":
                case "operator":
                case "out":
                case "override":
                case "params":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "ref":
                case "return":
                case "sbyte":
                case "sealed":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unsafe":
                case "ushort":
                case "using":
                case "virtual":
                case "void":
                case "volatile":
                case "while":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the character is valid as the first character of
        /// a C# identifier (letter or underscore).
        /// </summary>
        internal static bool IsIdentifierStart(char ch)
        {
            return ch == '_' || char.IsLetter(ch);
        }

        /// <summary>
        /// Returns true when the character is valid inside a C# identifier
        /// (letter, digit, or underscore).
        /// </summary>
        internal static bool IsIdentifierPart(char ch)
        {
            return ch == '_' || char.IsLetterOrDigit(ch);
        }
    }
}
