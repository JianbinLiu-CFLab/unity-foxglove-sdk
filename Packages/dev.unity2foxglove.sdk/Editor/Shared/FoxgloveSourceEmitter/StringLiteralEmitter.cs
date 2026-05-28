// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxgloveSourceEmitter

using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Escapes a string value for use as a C# string literal, handling
    /// backslash, double-quote, and control-character escapes.
    /// </summary>
    internal static class StringLiteralEmitter
    {
        /// <summary>
        /// Returns a C# string literal for the given value with all required
        /// escape sequences applied.
        /// </summary>
        internal static string CSharpStringLiteral(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }
    }
}
