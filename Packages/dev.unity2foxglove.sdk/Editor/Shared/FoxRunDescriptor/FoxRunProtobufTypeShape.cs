// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Represents host-independent FoxRun Protobuf field and DTO shapes.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Editor
{
    public enum FoxRunProtobufTypeShapeKind
    {
        Canonical = 0,
        Object = 1,
        Enum = 2
    }

    public enum FoxRunProtobufRepeatedCollectionKind
    {
        None = 0,
        Array = 1,
        List = 2
    }

    public sealed class FoxRunProtobufTypeShape
    {
        private FoxRunProtobufTypeShape(
            FoxRunProtobufTypeShapeKind kind,
            string typeName,
            string canonicalType,
            IReadOnlyList<FoxRunProtobufTypeField> fields,
            IReadOnlyList<FoxRunProtobufEnumValue> enumValues)
        {
            Kind = kind;
            TypeName = typeName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            Fields = new List<FoxRunProtobufTypeField>(fields ?? Array.Empty<FoxRunProtobufTypeField>()).AsReadOnly();
            EnumValues = new List<FoxRunProtobufEnumValue>(enumValues ?? Array.Empty<FoxRunProtobufEnumValue>()).AsReadOnly();
        }

        public FoxRunProtobufTypeShapeKind Kind { get; }
        public string TypeName { get; }
        public string CanonicalType { get; }
        public IReadOnlyList<FoxRunProtobufTypeField> Fields { get; }
        public IReadOnlyList<FoxRunProtobufEnumValue> EnumValues { get; }

        public static FoxRunProtobufTypeShape Canonical(string canonicalType)
        {
            if (string.IsNullOrWhiteSpace(canonicalType))
                throw new ArgumentException("A canonical Protobuf field type is required.", nameof(canonicalType));
            return new FoxRunProtobufTypeShape(
                FoxRunProtobufTypeShapeKind.Canonical,
                canonicalType,
                canonicalType,
                Array.Empty<FoxRunProtobufTypeField>(),
                Array.Empty<FoxRunProtobufEnumValue>());
        }

        public static FoxRunProtobufTypeShape Object(
            string typeName,
            IReadOnlyList<FoxRunProtobufTypeField> fields)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A Protobuf DTO type name is required.", nameof(typeName));
            return new FoxRunProtobufTypeShape(
                FoxRunProtobufTypeShapeKind.Object,
                typeName,
                string.Empty,
                fields,
                Array.Empty<FoxRunProtobufEnumValue>());
        }

        public static FoxRunProtobufTypeShape Enum(
            string typeName,
            IReadOnlyList<FoxRunProtobufEnumValue> values)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A Protobuf enum type name is required.", nameof(typeName));
            return new FoxRunProtobufTypeShape(
                FoxRunProtobufTypeShapeKind.Enum,
                typeName,
                string.Empty,
                Array.Empty<FoxRunProtobufTypeField>(),
                values);
        }
    }

    public sealed class FoxRunProtobufTypeField
    {
        public FoxRunProtobufTypeField(
            string jsonName,
            string memberName,
            FoxRunProtobufTypeShape typeShape,
            bool repeated = false,
            int protobufFieldNumber = 0,
            FoxRunProtobufRepeatedCollectionKind repeatedCollectionKind = FoxRunProtobufRepeatedCollectionKind.None,
            bool canAssign = true,
            bool isNullable = false)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            TypeShape = typeShape ?? throw new ArgumentNullException(nameof(typeShape));
            Repeated = repeated;
            ProtobufFieldNumber = protobufFieldNumber;
            RepeatedCollectionKind = repeated
                ? repeatedCollectionKind == FoxRunProtobufRepeatedCollectionKind.None
                    ? FoxRunProtobufRepeatedCollectionKind.List
                    : repeatedCollectionKind
                : FoxRunProtobufRepeatedCollectionKind.None;
            CanAssign = canAssign;
            IsNullable = isNullable;
        }

        public string JsonName { get; }
        public string MemberName { get; }
        public FoxRunProtobufTypeShape TypeShape { get; }
        public bool Repeated { get; }
        public int ProtobufFieldNumber { get; }
        public FoxRunProtobufRepeatedCollectionKind RepeatedCollectionKind { get; }
        /// <summary>Whether generated inbound Protobuf code can assign this DTO member.</summary>
        public bool CanAssign { get; }
        /// <summary>Whether this value-type field or repeated element may be absent.</summary>
        public bool IsNullable { get; }
    }

    public sealed class FoxRunProtobufEnumValue
    {
        public FoxRunProtobufEnumValue(string name, int number)
        {
            Name = name ?? string.Empty;
            Number = number;
        }

        public string Name { get; }
        public int Number { get; }
    }
}
