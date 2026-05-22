// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Canonical type normalization shared by FoxRun manifest and descriptor generation.

using System;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// String-only canonical type normalizer. Kept free of Unity, Roslyn, and
    /// reflection dependencies so analyzer and build-time paths cannot drift.
    /// </summary>
    public static class FoxRunCanonicalTypeNormalizer
    {
        public static string NormalizeTypeName(string typeName)
        {
            var name = UnwrapNullable(typeName ?? string.Empty).Replace('+', '.');
            switch (name)
            {
                case "float":
                case "Single":
                case "System.Single":
                    return "float32";
                case "double":
                case "Double":
                case "System.Double":
                    return "float64";
                case "bool":
                case "Boolean":
                case "System.Boolean":
                    return "bool";
                case "byte":
                case "Byte":
                case "System.Byte":
                    return "uint8";
                case "sbyte":
                case "SByte":
                case "System.SByte":
                    return "int8";
                case "short":
                case "Int16":
                case "System.Int16":
                    return "int16";
                case "ushort":
                case "UInt16":
                case "System.UInt16":
                    return "uint16";
                case "int":
                case "Int32":
                case "System.Int32":
                    return "int32";
                case "uint":
                case "UInt32":
                case "System.UInt32":
                    return "uint32";
                case "long":
                case "Int64":
                case "System.Int64":
                    return "int64";
                case "ulong":
                case "UInt64":
                case "System.UInt64":
                    return "uint64";
                case "string":
                case "String":
                case "System.String":
                    return "string";
                case "Vector2":
                case "UnityEngine.Vector2":
                    return "unity.vector2.float32";
                case "Vector3":
                case "UnityEngine.Vector3":
                    return "unity.vector3.float32";
                case "Quaternion":
                case "UnityEngine.Quaternion":
                    return "unity.quaternion.float32";
                case "Color":
                case "UnityEngine.Color":
                    return "unity.color.float32";
                default:
                    return name;
            }
        }

        public static bool IsNullableType(string typeName)
        {
            var name = typeName ?? string.Empty;
            return name.StartsWith("System.Nullable", StringComparison.Ordinal)
                   || name.EndsWith("?", StringComparison.Ordinal);
        }

        public static bool IsStringType(string typeName)
        {
            var name = typeName ?? string.Empty;
            return name == "string" || name == "String" || name == "System.String";
        }

        public static bool IsKnownUnityValueType(string typeName)
        {
            var name = typeName ?? string.Empty;
            return name == "Vector2"
                   || name == "UnityEngine.Vector2"
                   || name == "Vector3"
                   || name == "UnityEngine.Vector3"
                   || name == "Quaternion"
                   || name == "UnityEngine.Quaternion"
                   || name == "Color"
                   || name == "UnityEngine.Color";
        }

        public static bool IsKnownCanonicalType(string canonicalType)
        {
            switch (canonicalType ?? string.Empty)
            {
                case "float32":
                case "float64":
                case "bool":
                case "uint8":
                case "int8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "string":
                case "unity.vector2.float32":
                case "unity.vector3.float32":
                case "unity.quaternion.float32":
                case "unity.color.float32":
                    return true;
                default:
                    return false;
            }
        }

        public static string UnwrapNullable(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return string.Empty;

            if (typeName.EndsWith("?", StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - 1);

            const string genericPrefix = "System.Nullable`1[[";
            if (typeName.StartsWith(genericPrefix, StringComparison.Ordinal))
            {
                var start = genericPrefix.Length;
                var comma = typeName.IndexOf(',', start);
                var end = comma >= 0 ? comma : typeName.IndexOf("]]", start, StringComparison.Ordinal);
                if (end > start)
                    return typeName.Substring(start, end - start);
            }

            const string friendlyPrefix = "System.Nullable<";
            if (typeName.StartsWith(friendlyPrefix, StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
                return typeName.Substring(friendlyPrefix.Length, typeName.Length - friendlyPrefix.Length - 1);

            return typeName;
        }
    }
}
