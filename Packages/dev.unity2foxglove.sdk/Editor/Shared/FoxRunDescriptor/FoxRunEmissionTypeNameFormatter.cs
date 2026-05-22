// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Formats host-observed CLR/Roslyn type identities into stable C# type text for emission.

using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxRunEmissionTypeNameFormatter
    {
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Boolean"] = "bool",
            ["Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["Byte"] = "byte",
            ["System.SByte"] = "sbyte",
            ["SByte"] = "sbyte",
            ["System.Int16"] = "short",
            ["Int16"] = "short",
            ["System.UInt16"] = "ushort",
            ["UInt16"] = "ushort",
            ["System.Int32"] = "int",
            ["Int32"] = "int",
            ["System.UInt32"] = "uint",
            ["UInt32"] = "uint",
            ["System.Int64"] = "long",
            ["Int64"] = "long",
            ["System.UInt64"] = "ulong",
            ["UInt64"] = "ulong",
            ["System.Single"] = "float",
            ["Single"] = "float",
            ["System.Double"] = "double",
            ["Double"] = "double",
            ["System.Decimal"] = "decimal",
            ["Decimal"] = "decimal",
            ["System.String"] = "string",
            ["String"] = "string",
            ["System.Object"] = "object",
            ["Object"] = "object",
            ["System.Char"] = "char",
            ["Char"] = "char",
        };

        public static string FromReflectionType(Type type)
        {
            if (type == null)
                return "object";

            if (type.IsArray && type.GetArrayRank() == 1)
                return FromReflectionType(type.GetElementType()) + "[]";

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null)
                return FromReflectionType(nullable) + "?";

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                var name = QualifiedName(definition);
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);

                var args = type.GetGenericArguments();
                var sb = new StringBuilder(name);
                sb.Append('<');
                for (var i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FromReflectionType(args[i]));
                }
                sb.Append('>');
                return sb.ToString();
            }

            return NormalizeCSharpTypeName(QualifiedName(type));
        }

        public static string NormalizeCSharpTypeName(string typeName)
        {
            var name = (typeName ?? string.Empty).Trim();
            if (name.Length == 0)
                return "object";

            name = name.Replace("global::", string.Empty);
            name = name.Replace("+", ".");

            if (Aliases.TryGetValue(name, out var alias))
                return alias;

            var clrGeneric = NormalizeClrGenericTypeName(name);
            if (clrGeneric != null)
                return clrGeneric;

            if (name.EndsWith("[]", StringComparison.Ordinal))
                return NormalizeCSharpTypeName(name.Substring(0, name.Length - 2)) + "[]";

            const string nullablePrefix = "System.Nullable<";
            if (name.StartsWith(nullablePrefix, StringComparison.Ordinal) && name.EndsWith(">", StringComparison.Ordinal))
                return NormalizeCSharpTypeName(name.Substring(nullablePrefix.Length, name.Length - nullablePrefix.Length - 1)) + "?";

            var genericStart = FindTopLevel(name, '<');
            if (genericStart >= 0 && name.EndsWith(">", StringComparison.Ordinal))
            {
                var baseName = name.Substring(0, genericStart);
                var args = name.Substring(genericStart + 1, name.Length - genericStart - 2);
                return baseName + "<" + string.Join(", ", SplitGenericArguments(args).ConvertAll(NormalizeCSharpTypeName)) + ">";
            }

            return name;
        }

        private static string NormalizeClrGenericTypeName(string name)
        {
            var tick = name.IndexOf('`');
            var argsStart = name.IndexOf("[[", StringComparison.Ordinal);
            var argsEnd = name.LastIndexOf("]]", StringComparison.Ordinal);
            if (tick < 0 || argsStart < 0 || argsEnd <= argsStart)
                return null;

            var baseName = name.Substring(0, tick);
            var argsText = name.Substring(argsStart + 2, argsEnd - argsStart - 2);
            var args = SplitClrGenericArguments(argsText);
            if (baseName == "System.Nullable" && args.Count == 1)
                return NormalizeCSharpTypeName(args[0]) + "?";

            var sb = new StringBuilder(baseName);
            sb.Append('<');
            for (var i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(NormalizeCSharpTypeName(args[i]));
            }
            sb.Append('>');
            return sb.ToString();
        }

        private static List<string> SplitClrGenericArguments(string value)
        {
            var result = new List<string>();
            foreach (var part in (value ?? string.Empty).Split(new[] { "],[" }, StringSplitOptions.None))
                result.Add(StripAssembly(part));
            return result;
        }

        private static string StripAssembly(string value)
        {
            var text = (value ?? string.Empty).Trim().Trim('[', ']');
            var comma = text.IndexOf(',');
            return comma >= 0 ? text.Substring(0, comma).Trim() : text;
        }

        private static string QualifiedName(Type type)
        {
            if (type.DeclaringType != null)
                return FromReflectionType(type.DeclaringType) + "." + type.Name;

            return string.IsNullOrEmpty(type.Namespace)
                ? type.Name
                : type.Namespace + "." + type.Name;
        }

        private static int FindTopLevel(string value, char needle)
        {
            var depth = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '<') depth++;
                if (ch == '>') depth--;
                if (ch == needle && depth == 1)
                    return i;
            }
            return -1;
        }

        private static List<string> SplitGenericArguments(string value)
        {
            var result = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if (ch == '<') depth++;
                if (ch == '>') depth--;
                if (ch == ',' && depth == 0)
                {
                    result.Add(value.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            result.Add(value.Substring(start).Trim());
            return result;
        }
    }
}
