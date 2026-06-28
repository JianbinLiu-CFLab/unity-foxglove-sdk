// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxServiceDtoValidation
// Purpose: Stable type-name policy helpers for declarative FoxService DTO validation.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public static class FoxServiceDtoTypeNames
    {
        private static readonly HashSet<string> ScalarTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Boolean",
            "System.Byte",
            "System.SByte",
            "System.Int16",
            "System.UInt16",
            "System.Int32",
            "System.UInt32",
            "System.Int64",
            "System.UInt64",
            "System.Single",
            "System.Double",
            "System.Decimal",
            "System.String",
            "System.Char",
            "System.DateTime",
            "System.DateTimeOffset",
            "System.Guid",
            "System.TimeSpan",
        };

        private static readonly HashSet<string> ListTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.List<T>",
            "System.Collections.Generic.IList<T>",
            "System.Collections.Generic.IReadOnlyList<T>",
            "System.Collections.Generic.HashSet<T>",
            "System.Collections.Generic.ICollection<T>",
            "System.Collections.Generic.Queue<T>",
            "System.Collections.Generic.Stack<T>",
            "System.Collections.ObjectModel.Collection<T>",
        };

        private static readonly HashSet<string> ResponseOnlyListTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.IReadOnlyCollection<T>",
        };

        private static readonly HashSet<string> DictionaryTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.Dictionary<TKey, TValue>",
            "System.Collections.Generic.IDictionary<TKey, TValue>",
            "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
            "System.Collections.Generic.SortedDictionary<TKey, TValue>",
        };

        public static bool IsScalar(string fullName)
            => ScalarTypeNames.Contains(fullName ?? string.Empty);

        public static bool IsListContract(string constructedFrom)
            => ListTypeNames.Contains(NormalizeGenericContractName(constructedFrom));

        public static bool IsListContract(string constructedFrom, string side)
        {
            var name = NormalizeGenericContractName(constructedFrom);
            return ListTypeNames.Contains(name)
                   || (side == FoxServiceDtoRules.ResponseSide && ResponseOnlyListTypeNames.Contains(name));
        }

        public static bool IsMutableCollectionContract(string constructedFrom)
            => ListTypeNames.Contains(NormalizeGenericContractName(constructedFrom));

        public static bool IsDictionaryContract(string constructedFrom)
            => DictionaryTypeNames.Contains(NormalizeGenericContractName(constructedFrom));

        public static bool IsTaskLike(string fullName)
        {
            var name = fullName ?? string.Empty;
            return name == "System.Threading.Tasks.Task"
                   || name.StartsWith("System.Threading.Tasks.Task<", StringComparison.Ordinal)
                   || name.StartsWith("System.Threading.Tasks.Task`", StringComparison.Ordinal)
                   || name == "System.Threading.Tasks.ValueTask"
                   || name.StartsWith("System.Threading.Tasks.ValueTask<", StringComparison.Ordinal)
                   || name.StartsWith("System.Threading.Tasks.ValueTask`", StringComparison.Ordinal);
        }

        public static bool IsUnsafeRuntimeHandle(string fullName)
        {
            var name = fullName ?? string.Empty;
            return name == "System.IntPtr" || name == "System.UIntPtr";
        }

        public static bool IsFunctionPointerLike(string fullName)
            => (fullName ?? string.Empty).IndexOf("delegate*", StringComparison.Ordinal) >= 0;

        public static string Normalize(string fullName)
            => (fullName ?? string.Empty).Replace('+', '.');

        public static string NormalizeGenericContractName(string constructedFrom)
        {
            var name = constructedFrom ?? string.Empty;
            return name switch
            {
                "System.Collections.Generic.List`1" => "System.Collections.Generic.List<T>",
                "System.Collections.Generic.IList`1" => "System.Collections.Generic.IList<T>",
                "System.Collections.Generic.IReadOnlyList`1" => "System.Collections.Generic.IReadOnlyList<T>",
                "System.Collections.Generic.HashSet`1" => "System.Collections.Generic.HashSet<T>",
                "System.Collections.Generic.ICollection`1" => "System.Collections.Generic.ICollection<T>",
                "System.Collections.Generic.IReadOnlyCollection`1" => "System.Collections.Generic.IReadOnlyCollection<T>",
                "System.Collections.Generic.Queue`1" => "System.Collections.Generic.Queue<T>",
                "System.Collections.Generic.Stack`1" => "System.Collections.Generic.Stack<T>",
                "System.Collections.ObjectModel.Collection`1" => "System.Collections.ObjectModel.Collection<T>",
                "System.Collections.Generic.Dictionary`2" => "System.Collections.Generic.Dictionary<TKey, TValue>",
                "System.Collections.Generic.IDictionary`2" => "System.Collections.Generic.IDictionary<TKey, TValue>",
                "System.Collections.Generic.IReadOnlyDictionary`2" => "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
                "System.Collections.Generic.SortedDictionary`2" => "System.Collections.Generic.SortedDictionary<TKey, TValue>",
                _ => name,
            };
        }
    }
}
