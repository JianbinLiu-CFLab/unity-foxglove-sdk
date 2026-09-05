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
    public static class IdentifierUtils
    {
        /// <summary>
        /// Sanitizes a value into a valid C# identifier: replaces disallowed
        /// characters with underscores and prepends an underscore when the
        /// value starts with a digit.
        /// </summary>
        public static string SanitizeIdentifier(string value)
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
        public static string SanitizeFileStem(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "FoxRunSource";

            var sb = new StringBuilder(value.Length + 1);
            // File stems are not C# identifiers, so leading digits remain valid
            // and intentionally differ from SanitizeIdentifier's underscore prefix.
            foreach (var ch in value)
                sb.Append(IsIdentifierPart(ch) ? ch : '_');

            return sb.ToString();
        }

        /// <summary>
        /// Escapes an identifier with the <c>@</c> prefix when it collides with
        /// a C# keyword. Leaves already-escaped identifiers unchanged.
        /// </summary>
        public static string EscapeIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var bare = value.StartsWith("@", StringComparison.Ordinal) ? value.Substring(1) : value;
            return IsCSharpKeyword(bare) ? "@" + bare : value;
        }

        /// <summary>
        /// Escapes each component of a dotted namespace or type name without
        /// changing its semantic (unescaped) spelling.
        /// </summary>
        public static string EscapeQualifiedName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var parts = value.Split('.');
            for (var i = 0; i < parts.Length; i++)
                parts[i] = EscapeIdentifier(parts[i]);
            return string.Join(".", parts);
        }

        /// <summary>
        /// Escapes identifier tokens in a C# type expression, including
        /// identifiers nested inside generic arguments and array/nullable
        /// suffixes.
        /// </summary>
        public static string EscapeTypeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new StringBuilder(value.Length + 8);
            var index = 0;
            while (index < value.Length)
            {
                var ch = value[index];
                if (ch == '@'
                    && index + 1 < value.Length
                    && IsIdentifierStart(value[index + 1]))
                {
                    var start = index++;
                    while (index < value.Length && IsIdentifierPart(value[index]))
                        index++;
                    sb.Append(value.Substring(start, index - start));
                    continue;
                }

                if (IsIdentifierStart(ch))
                {
                    var start = index++;
                    while (index < value.Length && IsIdentifierPart(value[index]))
                        index++;
                    var token = value.Substring(start, index - start);
                    if (string.Equals(token, "global", StringComparison.Ordinal)
                        && index + 1 < value.Length
                        && value[index] == ':'
                        && value[index + 1] == ':')
                        sb.Append(token);
                    else if (IsBuiltInTypeAlias(token))
                    {
                        // A built-in alias is only special in an unqualified type position.
                        // Once a dot/alias qualifier precedes it, it names a user type and
                        // reserved aliases (for example N.int) must be escaped.
                        if (IsQualifiedTypeToken(value, start) && IsReservedTypeAlias(token))
                            sb.Append(EscapeIdentifier(token));
                        else
                            sb.Append(token);
                    }
                    else
                        sb.Append(EscapeIdentifier(token));
                    continue;
                }

                sb.Append(ch);
                index++;
            }

            return sb.ToString();
        }

        private static bool IsQualifiedTypeToken(string value, int tokenStart)
        {
            var index = tokenStart - 1;
            while (index >= 0 && char.IsWhiteSpace(value[index]))
                index--;

            return index >= 0
                && (value[index] == '.' || value[index] == ':');
        }

        private static bool IsReservedTypeAlias(string value)
        {
            switch (value)
            {
                case "bool": case "byte": case "sbyte": case "short":
                case "ushort": case "int": case "uint": case "long":
                case "ulong": case "float": case "double": case "decimal":
                case "string": case "char": case "object": case "void":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBuiltInTypeAlias(string value)
        {
            switch (value)
            {
                case "bool": case "byte": case "sbyte": case "short":
                case "ushort": case "int": case "uint": case "long":
                case "ulong": case "float": case "double": case "decimal":
                case "string": case "char": case "object": case "void":
                case "dynamic": case "nint": case "nuint":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the given value is a C# keyword or contextual
        /// keyword that should be escaped in an identifier position.
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
                case "add":
                case "alias":
                case "and":
                case "ascending":
                case "async":
                case "await":
                case "by":
                case "descending":
                case "dynamic":
                case "equals":
                case "field":
                case "file":
                case "from":
                case "get":
                case "global":
                case "group":
                case "init":
                case "into":
                case "join":
                case "let":
                case "managed":
                case "nameof":
                case "nint":
                case "not":
                case "notnull":
                case "nuint":
                case "on":
                case "or":
                case "orderby":
                case "partial":
                case "record":
                case "remove":
                case "required":
                case "scoped":
                case "select":
                case "set":
                case "unmanaged":
                case "value":
                case "var":
                case "when":
                case "where":
                case "with":
                case "yield":
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
