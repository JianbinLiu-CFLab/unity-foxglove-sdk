// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Runtime DTOs for generated encoding-neutral FoxRun contract metadata.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxRunTypeShapeInfoKind
    {
        Canonical = 0,
        Object = 1,
        Enum = 2,
        Collection = 3
    }

    public enum FoxRunCollectionInfoKind
    {
        None = 0,
        Array = 1,
        List = 2,
        Binary = 3
    }

    /// <summary>One checked signed Int32 enum value in a generated FoxRun shape.</summary>
    public sealed class FoxRunEnumValueInfo
    {
        public FoxRunEnumValueInfo(string name, int number)
        {
            Name = name ?? string.Empty;
            Number = number;
        }

        public string Name { get; }
        public int Number { get; }
    }

    /// <summary>One ordered object field in a generated FoxRun shape.</summary>
    public sealed class FoxRunTypeFieldInfo
    {
        public FoxRunTypeFieldInfo(
            string jsonName,
            string memberName,
            FoxRunTypeShapeInfo typeShape,
            bool repeated = false,
            FoxRunCollectionInfoKind repeatedCollectionKind = FoxRunCollectionInfoKind.None,
            bool canAssign = true,
            bool nullable = false)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Repeated = repeated;
            RepeatedCollectionKind = repeated
                ? repeatedCollectionKind == FoxRunCollectionInfoKind.None
                    ? FoxRunCollectionInfoKind.List
                    : repeatedCollectionKind
                : FoxRunCollectionInfoKind.None;
            if (typeShape == null)
                throw new ArgumentNullException(nameof(typeShape));
            TypeShape = repeated && typeShape.Kind != FoxRunTypeShapeInfoKind.Collection
                ? new FoxRunTypeShapeInfo(
                    FoxRunTypeShapeInfoKind.Collection,
                    string.Empty,
                    string.Empty,
                    false,
                    RepeatedCollectionKind,
                    typeShape,
                    Array.Empty<FoxRunTypeFieldInfo>(),
                    Array.Empty<FoxRunEnumValueInfo>())
                : typeShape;
            CanAssign = canAssign;
            Nullable = nullable;
        }

        public string JsonName { get; }
        public string MemberName { get; }
        public FoxRunTypeShapeInfo TypeShape { get; }
        public bool Repeated { get; }
        public FoxRunCollectionInfoKind RepeatedCollectionKind { get; }
        public bool CanAssign { get; }
        public bool Nullable { get; }
    }

    /// <summary>
    /// Encoding-neutral recursive shape generated at build time. It is
    /// metadata only and never enables runtime reflection serialization.
    /// </summary>
    public sealed class FoxRunTypeShapeInfo
    {
        public FoxRunTypeShapeInfo(
            FoxRunTypeShapeInfoKind kind,
            string typeName,
            string canonicalType,
            bool nullable,
            FoxRunCollectionInfoKind collectionKind,
            FoxRunTypeShapeInfo elementShape,
            IReadOnlyList<FoxRunTypeFieldInfo> fields,
            IReadOnlyList<FoxRunEnumValueInfo> enumValues,
            bool canConstruct = true)
        {
            Kind = kind;
            TypeName = typeName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            Nullable = nullable;
            CollectionKind = collectionKind;
            ElementShape = elementShape;
            Fields = new List<FoxRunTypeFieldInfo>(
                fields ?? Array.Empty<FoxRunTypeFieldInfo>()).AsReadOnly();
            EnumValues = new List<FoxRunEnumValueInfo>(
                enumValues ?? Array.Empty<FoxRunEnumValueInfo>()).AsReadOnly();
            CanConstruct = canConstruct;
        }

        public FoxRunTypeShapeInfoKind Kind { get; }
        public string TypeName { get; }
        public string CanonicalType { get; }
        public bool Nullable { get; }
        public FoxRunCollectionInfoKind CollectionKind { get; }
        public FoxRunTypeShapeInfo ElementShape { get; }
        public IReadOnlyList<FoxRunTypeFieldInfo> Fields { get; }
        public IReadOnlyList<FoxRunEnumValueInfo> EnumValues { get; }
        public bool CanConstruct { get; }
        public bool IsBinary => CollectionKind == FoxRunCollectionInfoKind.Binary;
    }

    /// <summary>One normalized generated scheduling tuple.</summary>
    public sealed class FoxRunNormalizedScheduleInfo
    {
        public FoxRunNormalizedScheduleInfo(
            int policy,
            bool hasExplicitHz,
            float hz,
            float tolerance,
            string onlyIf,
            int conditionMemberKind)
        {
            Policy = policy;
            HasExplicitHz = hasExplicitHz;
            Hz = hz;
            Tolerance = tolerance;
            OnlyIf = onlyIf ?? string.Empty;
            ConditionMemberKind = conditionMemberKind;
        }

        public int Policy { get; }
        public bool HasExplicitHz { get; }
        public float Hz { get; }
        public float Tolerance { get; }
        public string OnlyIf { get; }
        public int ConditionMemberKind { get; }
    }
}
