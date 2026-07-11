// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Adapts Roslyn FoxRun DTO symbols into host-independent Protobuf shapes.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;
using static Unity.FoxgloveSDK.SourceGenerators.FoxServiceRoslynTypeHelpers;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynProtobufTypeShapeBuilder
    {
        public static bool TryBuild(ITypeSymbol type, out FoxRunProtobufTypeShape shape)
        {
            try
            {
                shape = Build(
                    type,
                    0,
                    new Dictionary<string, FoxRunProtobufTypeShape>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal));
                return true;
            }
            catch (ArgumentException)
            {
                shape = null;
                return false;
            }
            catch (InvalidOperationException)
            {
                shape = null;
                return false;
            }
        }

        private static FoxRunProtobufTypeShape Build(
            ITypeSymbol type,
            int depth,
            IDictionary<string, FoxRunProtobufTypeShape> memo,
            ISet<string> stack)
        {
            type = UnwrapNullable(type);
            if (type == null)
                throw new ArgumentException("A FoxRun Protobuf field type is required.", nameof(type));
            if (depth > FoxServiceDtoRules.MaxDepth)
                throw new InvalidOperationException("FoxRun Protobuf DTO nesting exceeds the supported depth.");

            if (type is IArrayTypeSymbol array)
                return Build(array.ElementType, depth + 1, memo, stack);
            if (!(type is INamedTypeSymbol named))
                throw new InvalidOperationException("FoxRun Protobuf type is not a named type.");
            if (TryGetListElementType(named, FoxServiceDtoRules.ResponseSide, out var elementType))
                return Build(elementType, depth + 1, memo, stack);

            var canonicalType = FoxRunCanonicalTypeNormalizer.NormalizeTypeName(FullTypeName(named));
            if (FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(canonicalType))
                return FoxRunProtobufTypeShape.Canonical(canonicalType);
            if (named.TypeKind == TypeKind.Enum)
                return BuildEnum(named, memo);
            if (IsUnsupported(named))
            {
                throw new InvalidOperationException(
                    "FoxRun Protobuf DTO type '" + FullTypeName(named) + "' is not supported.");
            }

            var typeName = FullTypeName(named);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;
            if (!stack.Add(typeName))
            {
                throw new InvalidOperationException(
                    "FoxRun Protobuf DTO graph contains a cycle at '" + typeName + "'.");
            }

            var fields = new List<FoxRunProtobufTypeField>();
            foreach (var member in InheritedAndDeclaredMembers(named))
            {
                if (HasIgnoredSerializationAttribute(member))
                    continue;
                if (member is IFieldSymbol field)
                {
                    if (field.IsStatic || field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    AddMember(fields, field.Name, JsonPropertyName(field), field.Type, !field.IsReadOnly, depth, memo, stack);
                }
                else if (member is IPropertySymbol property)
                {
                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsIndexer
                        || property.GetMethod == null)
                        continue;
                    AddMember(fields, property.Name, JsonPropertyName(property), property.Type,
                        property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly,
                        depth, memo, stack);
                }
            }

            var result = FoxRunProtobufTypeShape.Object(typeName, fields);
            stack.Remove(typeName);
            memo[typeName] = result;
            return result;
        }

        private static void AddMember(
            ICollection<FoxRunProtobufTypeField> fields,
            string memberName,
            string jsonName,
            ITypeSymbol memberType,
            bool canAssign,
            int depth,
            IDictionary<string, FoxRunProtobufTypeShape> memo,
            ISet<string> stack)
        {
            var repeated = memberType is IArrayTypeSymbol;
            var collectionKind = repeated
                ? FoxRunProtobufRepeatedCollectionKind.Array
                : FoxRunProtobufRepeatedCollectionKind.None;
            ITypeSymbol elementType = repeated ? ((IArrayTypeSymbol)memberType).ElementType : null;
            if (!repeated && TryGetListElementType(memberType, FoxServiceDtoRules.ResponseSide, out var listElementType))
            {
                repeated = true;
                elementType = listElementType;
                collectionKind = FoxRunProtobufRepeatedCollectionKind.List;
            }

            fields.Add(new FoxRunProtobufTypeField(
                jsonName,
                memberName,
                Build(repeated ? elementType : memberType, depth + 1, memo, stack),
                repeated,
                repeatedCollectionKind: collectionKind,
                canAssign: canAssign));
        }

        private static FoxRunProtobufTypeShape BuildEnum(
            INamedTypeSymbol type,
            IDictionary<string, FoxRunProtobufTypeShape> memo)
        {
            var typeName = FullTypeName(type);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;

            var values = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue)
                .Select(field => new FoxRunProtobufEnumValue(field.Name, Convert.ToInt32(field.ConstantValue)))
                .OrderBy(value => value.Number)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .ToList();
            if (!values.Any(value => value.Number == 0))
                values.Insert(0, new FoxRunProtobufEnumValue("UNSPECIFIED", 0));

            var result = FoxRunProtobufTypeShape.Enum(typeName, values);
            memo[typeName] = result;
            return result;
        }

        private static bool IsUnsupported(INamedTypeSymbol type)
        {
            var typeName = FullTypeName(type);
            return type.SpecialType == SpecialType.System_Object
                   || type.TypeKind == TypeKind.Interface
                   || type.TypeKind == TypeKind.Delegate
                   || type.IsGenericType
                   || IsDelegateType(type)
                   || IsUnityObjectType(type)
                   || FoxServiceDtoTypeNames.IsTaskLike(typeName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(typeName);
        }
    }
}
