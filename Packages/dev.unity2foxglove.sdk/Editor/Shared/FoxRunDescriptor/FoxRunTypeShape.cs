// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunDescriptor
// Purpose: Represents the encoding-neutral recursive FoxRun field and DTO contract.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Unity.FoxgloveSDK.Editor
{
    public enum FoxRunTypeShapeKind
    {
        Canonical = 0,
        Object = 1,
        Enum = 2,
        Collection = 3
    }

    public enum FoxRunCollectionKind
    {
        None = 0,
        Array = 1,
        List = 2,
        Binary = 3
    }

    public sealed class FoxRunTypeShape
    {
        private FoxRunTypeShape(
            FoxRunTypeShapeKind kind,
            string typeName,
            string canonicalType,
            IReadOnlyList<FoxRunTypeField> fields,
            IReadOnlyList<FoxRunEnumValue> enumValues,
            bool nullable,
            FoxRunCollectionKind collectionKind,
            FoxRunTypeShape elementShape,
            bool canConstruct)
        {
            Kind = kind;
            TypeName = typeName ?? string.Empty;
            CanonicalType = canonicalType ?? string.Empty;
            Fields = NormalizeObjectFields(
                typeName,
                fields ?? Array.Empty<FoxRunTypeField>());
            EnumValues = new List<FoxRunEnumValue>(enumValues ?? Array.Empty<FoxRunEnumValue>()).AsReadOnly();
            Nullable = nullable;
            CollectionKind = collectionKind;
            ElementShape = elementShape;
            CanConstruct = canConstruct;
        }

        private static IReadOnlyList<FoxRunTypeField> NormalizeObjectFields(
            string typeName,
            IEnumerable<FoxRunTypeField> fields)
            => fields
                .OrderBy(
                    field => UnityComponentOrder(typeName, field?.JsonName),
                    Comparer<int>.Default)
                .ThenBy(field => field?.JsonName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(field => field?.MemberName ?? string.Empty, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();

        private static int UnityComponentOrder(string typeName, string jsonName)
        {
            switch (typeName ?? string.Empty)
            {
                case "UnityEngine.Vector2":
                    return ComponentOrder(jsonName, "x", "y");
                case "UnityEngine.Vector3":
                    return ComponentOrder(jsonName, "x", "y", "z");
                case "UnityEngine.Quaternion":
                    return ComponentOrder(jsonName, "x", "y", "z", "w");
                case "UnityEngine.Color":
                    return ComponentOrder(jsonName, "r", "g", "b", "a");
                default:
                    return int.MaxValue;
            }
        }

        private static int ComponentOrder(string value, params string[] orderedNames)
        {
            for (var index = 0; index < orderedNames.Length; index++)
            {
                if (string.Equals(value, orderedNames[index], StringComparison.Ordinal))
                    return index;
            }
            return orderedNames.Length;
        }

        public FoxRunTypeShapeKind Kind { get; }
        public string TypeName { get; }
        public string CanonicalType { get; }
        public IReadOnlyList<FoxRunTypeField> Fields { get; }
        public IReadOnlyList<FoxRunEnumValue> EnumValues { get; }
        public bool Nullable { get; }
        public FoxRunCollectionKind CollectionKind { get; }
        public FoxRunTypeShape ElementShape { get; }
        /// <summary>
        /// Whether generated inbound code can create a fresh instance of this
        /// object shape. This is direction-neutral capability evidence; only
        /// Subscribe consumes it.
        /// </summary>
        public bool CanConstruct { get; }
        public bool IsBinary => CollectionKind == FoxRunCollectionKind.Binary;

        public static FoxRunTypeShape Canonical(string canonicalType, bool nullable = false)
        {
            if (string.IsNullOrWhiteSpace(canonicalType))
                throw new ArgumentException("A canonical FoxRun field type is required.", nameof(canonicalType));
            return new FoxRunTypeShape(
                FoxRunTypeShapeKind.Canonical,
                canonicalType,
                canonicalType,
                Array.Empty<FoxRunTypeField>(),
                Array.Empty<FoxRunEnumValue>(),
                nullable,
                FoxRunCollectionKind.None,
                null,
                canConstruct: true);
        }

        public static FoxRunTypeShape Object(
            string typeName,
            IReadOnlyList<FoxRunTypeField> fields,
            bool nullable = false,
            bool canConstruct = true)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A FoxRun DTO type name is required.", nameof(typeName));
            return new FoxRunTypeShape(
                FoxRunTypeShapeKind.Object,
                typeName,
                string.Empty,
                fields,
                Array.Empty<FoxRunEnumValue>(),
                nullable,
                FoxRunCollectionKind.None,
                null,
                canConstruct);
        }

        public static FoxRunTypeShape Enum(
            string typeName,
            IReadOnlyList<FoxRunEnumValue> values,
            bool nullable = false)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException("A FoxRun enum type name is required.", nameof(typeName));
            return new FoxRunTypeShape(
                FoxRunTypeShapeKind.Enum,
                typeName,
                string.Empty,
                Array.Empty<FoxRunTypeField>(),
                values,
                nullable,
                FoxRunCollectionKind.None,
                null,
                canConstruct: true);
        }

        public static FoxRunTypeShape Collection(
            FoxRunCollectionKind collectionKind,
            FoxRunTypeShape elementShape,
            bool nullable = false)
        {
            if (collectionKind == FoxRunCollectionKind.None)
                throw new ArgumentOutOfRangeException(nameof(collectionKind));
            if (elementShape == null)
                throw new ArgumentNullException(nameof(elementShape));
            return new FoxRunTypeShape(
                FoxRunTypeShapeKind.Collection,
                string.Empty,
                string.Empty,
                Array.Empty<FoxRunTypeField>(),
                Array.Empty<FoxRunEnumValue>(),
                nullable,
                collectionKind,
                elementShape,
                canConstruct: true);
        }

        public FoxRunTypeShape WithNullable(bool nullable = true)
        {
            if (Nullable == nullable)
                return this;
            return new FoxRunTypeShape(
                Kind,
                TypeName,
                CanonicalType,
                Fields,
                EnumValues,
                nullable,
                CollectionKind,
                ElementShape,
                CanConstruct);
        }
    }

    public sealed class FoxRunTypeField
    {
        public FoxRunTypeField(
            string jsonName,
            string memberName,
            FoxRunTypeShape typeShape,
            bool repeated = false,
            FoxRunCollectionKind repeatedCollectionKind = FoxRunCollectionKind.None,
            bool canAssign = true,
            bool isNullable = false)
        {
            JsonName = jsonName ?? string.Empty;
            MemberName = memberName ?? string.Empty;
            Repeated = repeated;
            RepeatedCollectionKind = repeated
                ? repeatedCollectionKind == FoxRunCollectionKind.None
                    ? FoxRunCollectionKind.List
                    : repeatedCollectionKind
                : FoxRunCollectionKind.None;
            if (typeShape == null)
                throw new ArgumentNullException(nameof(typeShape));
            TypeShape = repeated && typeShape.Kind != FoxRunTypeShapeKind.Collection
                ? FoxRunTypeShape.Collection(
                    RepeatedCollectionKind,
                    typeShape.WithNullable(isNullable))
                : typeShape;
            CanAssign = canAssign;
            IsNullable = isNullable;
        }

        public string JsonName { get; }
        public string MemberName { get; }
        public FoxRunTypeShape TypeShape { get; }
        public bool Repeated { get; }
        public FoxRunCollectionKind RepeatedCollectionKind { get; }
        /// <summary>Whether generated inbound code can assign this DTO member.</summary>
        public bool CanAssign { get; }
        /// <summary>Whether this value-type field or repeated element may be absent.</summary>
        public bool IsNullable { get; }
    }

    public sealed class FoxRunEnumValue
    {
        public FoxRunEnumValue(string name, int number)
        {
            Name = name ?? string.Empty;
            Number = number;
        }

        public string Name { get; }
        public int Number { get; }
    }

    /// <summary>
    /// Protobuf-only member metadata kept outside the encoding-neutral type
    /// shape. The root override and all nested tag/presence decisions live
    /// here so changing a Protobuf tag cannot change another codec's shape.
    /// </summary>
    public sealed class FoxRunProtobufMetadata
    {
        public FoxRunProtobufMetadata(
            int fieldNumber,
            FoxRunProtobufTypeMetadata typeMetadata = null)
        {
            FieldNumber = fieldNumber;
            TypeMetadata = typeMetadata;
        }

        public int FieldNumber { get; }
        public FoxRunProtobufTypeMetadata TypeMetadata { get; }

        public static FoxRunProtobufMetadata FromTypeShape(
            FoxRunTypeShape shape,
            int fieldNumber = 0)
            => new FoxRunProtobufMetadata(
                fieldNumber,
                FoxRunProtobufTypeMetadata.FromTypeShape(shape));
    }

    public sealed class FoxRunProtobufTypeMetadata
    {
        public FoxRunProtobufTypeMetadata(
            string typeName,
            IReadOnlyList<FoxRunProtobufFieldMetadata> fields)
        {
            TypeName = typeName ?? string.Empty;
            Fields = new List<FoxRunProtobufFieldMetadata>(
                fields ?? Array.Empty<FoxRunProtobufFieldMetadata>()).AsReadOnly();
        }

        public string TypeName { get; }
        public IReadOnlyList<FoxRunProtobufFieldMetadata> Fields { get; }

        public FoxRunProtobufFieldMetadata Find(
            string memberName,
            string jsonName = null)
        {
            foreach (var field in Fields)
            {
                if (string.Equals(field.MemberName, memberName, StringComparison.Ordinal)
                    && (jsonName == null
                        || string.Equals(field.JsonName, jsonName, StringComparison.Ordinal)))
                    return field;
            }
            return null;
        }

        public static FoxRunProtobufTypeMetadata FromTypeShape(FoxRunTypeShape shape)
        {
            while (shape != null && shape.Kind == FoxRunTypeShapeKind.Collection)
                shape = shape.ElementShape;
            if (shape == null || shape.Kind != FoxRunTypeShapeKind.Object)
                return null;

            var fields = new List<FoxRunProtobufFieldMetadata>(shape.Fields.Count);
            foreach (var field in shape.Fields)
            {
                fields.Add(new FoxRunProtobufFieldMetadata(
                    field.MemberName,
                    field.JsonName,
                    fieldNumber: 0,
                    typeMetadata: FoxRunProtobufTypeMetadata.FromTypeShape(field.TypeShape)));
            }
            return new FoxRunProtobufTypeMetadata(shape.TypeName, fields);
        }
    }

    public sealed class FoxRunProtobufFieldMetadata
    {
        public FoxRunProtobufFieldMetadata(
            string memberName,
            string jsonName = "",
            int fieldNumber = 0,
            FoxRunProtobufTypeMetadata typeMetadata = null,
            bool presenceOnly = false,
            bool presenceUsesHasValue = false)
        {
            MemberName = memberName ?? string.Empty;
            JsonName = jsonName ?? string.Empty;
            FieldNumber = fieldNumber;
            TypeMetadata = typeMetadata;
            PresenceOnly = presenceOnly;
            PresenceUsesHasValue = presenceUsesHasValue;
        }

        public string MemberName { get; }
        public string JsonName { get; }
        public int FieldNumber { get; }
        public FoxRunProtobufTypeMetadata TypeMetadata { get; }
        public bool PresenceOnly { get; }
        public bool PresenceUsesHasValue { get; }
    }

    /// <summary>
    /// Projects encoding-neutral Unity object shapes back onto their existing
    /// canonical Protobuf wire contracts. MessagePack and JSON continue to see
    /// the original recursive object shape.
    /// </summary>
    internal static class FoxRunProtobufTypeShapeProjection
    {
        public static FoxRunTypeShape ProjectValue(FoxRunTypeShape shape)
        {
            if (shape == null || shape.Kind != FoxRunTypeShapeKind.Object)
                return shape;

            string canonicalType;
            switch (shape.TypeName)
            {
                case "UnityEngine.Vector2":
                    canonicalType = "unity.vector2.float32";
                    break;
                case "UnityEngine.Vector3":
                    canonicalType = "unity.vector3.float32";
                    break;
                case "UnityEngine.Quaternion":
                    canonicalType = "unity.quaternion.float32";
                    break;
                case "UnityEngine.Color":
                    canonicalType = "unity.color.float32";
                    break;
                default:
                    return shape;
            }

            return FoxRunTypeShape.Canonical(canonicalType, shape.Nullable);
        }
    }

    /// <summary>
    /// Stable encoding-neutral shape identity. Callers choose whether
    /// usage-only traits participate: MessagePack reader/writer method
    /// signatures require them, while Protobuf wire identity deliberately
    /// excludes them.
    /// </summary>
    internal static class FoxRunTypeShapeIdentityFormatter
    {
        public static string Build(
            FoxRunTypeShape shape,
            bool includeUsageTraits)
        {
            var sb = new StringBuilder();
            AppendShape(sb, shape, includeUsageTraits);
            return sb.ToString();
        }

        private static void AppendShape(
            StringBuilder sb,
            FoxRunTypeShape shape,
            bool includeUsageTraits)
        {
            if (shape == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append(((int)shape.Kind).ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(shape.TypeName)
                .Append(':')
                .Append(shape.CanonicalType)
                .Append(':')
                .Append(((int)shape.CollectionKind).ToString(CultureInfo.InvariantCulture));
            if (includeUsageTraits)
            {
                sb.Append(':')
                    .Append(shape.Nullable ? '1' : '0')
                    .Append(':')
                    .Append(shape.CanConstruct ? '1' : '0');
            }
            sb.Append('[');
            foreach (var field in shape.Fields)
            {
                sb.Append(field.JsonName)
                    .Append('=')
                    .Append(field.MemberName)
                    .Append(':')
                    .Append(field.Repeated ? '1' : '0')
                    .Append(':')
                    .Append(((int)field.RepeatedCollectionKind).ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(field.CanAssign ? '1' : '0')
                    .Append(':')
                    .Append(field.IsNullable ? '1' : '0')
                    .Append('{');
                AppendShape(sb, field.TypeShape, includeUsageTraits);
                sb.Append("};");
            }
            sb.Append(']');
            if (shape.EnumValues.Count > 0)
            {
                sb.Append('(');
                foreach (var value in shape.EnumValues
                             .OrderBy(candidate => candidate.Number)
                             .ThenBy(candidate => candidate.Name, StringComparer.Ordinal))
                {
                    sb.Append(value.Name)
                        .Append('=')
                        .Append(value.Number.ToString(CultureInfo.InvariantCulture))
                        .Append(';');
                }
                sb.Append(')');
            }
            if (shape.ElementShape != null)
            {
                sb.Append('<');
                AppendShape(sb, shape.ElementShape, includeUsageTraits);
                sb.Append('>');
            }
        }
    }

    /// <summary>
    /// Stable MessagePack code-generation identity. Nullable and
    /// constructibility traits affect generated reader/writer signatures and
    /// therefore cannot share the Protobuf wire identity.
    /// </summary>
    internal static class FoxRunMessagePackTypeShapeIdentity
    {
        public static string Build(FoxRunTypeShape shape)
            => FoxRunTypeShapeIdentityFormatter.Build(
                shape,
                includeUsageTraits: true);
    }

    /// <summary>
    /// Stable Protobuf object identity shared by descriptor and generated-code
    /// emission. Reusing one CLR type name with a different nested wire shape
    /// must fail closed rather than silently reusing the first definition.
    /// </summary>
    internal static class FoxRunProtobufObjectShapeIdentity
    {
        public static string Build(
            FoxRunTypeShape shape,
            FoxRunProtobufTypeMetadata metadata)
        {
            var sb = new StringBuilder();
            sb.Append(FoxRunTypeShapeIdentityFormatter.Build(
                shape,
                includeUsageTraits: false));
            sb.Append("|protobuf:");
            AppendMetadata(sb, metadata);
            return sb.ToString();
        }

        private static void AppendMetadata(
            StringBuilder sb,
            FoxRunProtobufTypeMetadata metadata)
        {
            if (metadata == null)
            {
                sb.Append("null");
                return;
            }

            sb.Append(metadata.TypeName).Append('[');
            foreach (var field in metadata.Fields
                         .OrderBy(candidate => candidate.JsonName, StringComparer.Ordinal)
                         .ThenBy(candidate => candidate.MemberName, StringComparer.Ordinal))
            {
                sb.Append(field.JsonName)
                    .Append('=')
                    .Append(field.MemberName)
                    .Append(':')
                    .Append(field.FieldNumber.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(field.PresenceOnly ? '1' : '0')
                    .Append(':')
                    .Append(field.PresenceUsesHasValue ? '1' : '0')
                    .Append('{');
                AppendMetadata(sb, field.TypeMetadata);
                sb.Append("};");
            }
            sb.Append(']');
        }
    }

    /// <summary>
    /// Directional legality rules for the shared type shape. Publish only
    /// needs a readable bounded shape; Subscribe additionally needs every
    /// object to be constructible and every member to be writable.
    /// </summary>
    internal static class FoxRunMessagePackTypeShapeRules
    {
        public static bool IsPublishSupported(
            FoxRunTypeShape shape,
            string canonicalType)
            => IsSupported(shape, canonicalType, requireInbound: false, new HashSet<FoxRunTypeShape>());

        public static bool IsSubscribeSupported(
            FoxRunTypeShape shape,
            string canonicalType)
            => IsSupported(shape, canonicalType, requireInbound: true, new HashSet<FoxRunTypeShape>());

        private static bool IsSupported(
            FoxRunTypeShape shape,
            string canonicalType,
            bool requireInbound,
            ISet<FoxRunTypeShape> visited)
        {
            if (shape == null)
                return FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(canonicalType);
            if (!visited.Add(shape))
                return true;

            switch (shape.Kind)
            {
                case FoxRunTypeShapeKind.Canonical:
                    return FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(shape.CanonicalType);
                case FoxRunTypeShapeKind.Enum:
                    return shape.EnumValues.Count > 0;
                case FoxRunTypeShapeKind.Collection:
                    return shape.CollectionKind != FoxRunCollectionKind.None
                           && shape.ElementShape != null
                           && IsSupported(
                               shape.ElementShape,
                               shape.ElementShape.CanonicalType,
                               requireInbound,
                               visited);
                case FoxRunTypeShapeKind.Object:
                    if (requireInbound && !shape.CanConstruct)
                        return false;
                    foreach (var field in shape.Fields)
                    {
                        if (field == null
                            || (requireInbound && !field.CanAssign)
                            || !IsSupported(
                                field.TypeShape,
                                field.TypeShape?.CanonicalType,
                                requireInbound,
                                visited))
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }
    }
}
