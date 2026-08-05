// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn-backed FoxService schema preview adapter.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;
using static Unity.FoxgloveSDK.SourceGenerators.FoxServiceRoslynTypeHelpers;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxServiceRoslynSchemaBuilder
    {
        public static FoxServiceSchemaModel Build(ITypeSymbol type, string side, int depth)
            => Build(
                type,
                side,
                depth,
                new Dictionary<string, FoxServiceSchemaModel>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));

        public static string EmptyServiceSchemaPreview()
            => FoxServiceSchemaEmitter.Emit(FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>()));

        public static bool IsBlockingSchemaPreviewDiagnostic(ServiceDiagnostic diagnostic)
            => diagnostic != null
               && Diags.Service(diagnostic.Id).DefaultSeverity == DiagnosticSeverity.Error;

        private static FoxServiceSchemaModel Build(
            ITypeSymbol type,
            string side,
            int depth,
            IDictionary<string, FoxServiceSchemaModel> memo,
            ISet<string> stack)
        {
            if (type == null || type.SpecialType == SpecialType.System_Void)
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (depth > FoxServiceDtoRules.MaxDepth)
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            type = UnwrapNullable(type);
            if (type is IArrayTypeSymbol array)
                return FoxServiceSchemaModel.ArrayOf(Build(array.ElementType, side, depth + 1, memo, stack));

            if (!(type is INamedTypeSymbol named))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            if (TryGetJsonScalarType(named, out var scalar))
                return FoxServiceSchemaModel.Scalar(scalar);

            if (named.TypeKind == TypeKind.Enum)
                return FoxServiceSchemaModel.Scalar("integer");

            if (TryGetDictionaryValueType(named, out _, out var valueType))
                return FoxServiceSchemaModel.Dictionary(Build(valueType, side, depth + 1, memo, stack));

            if (TryGetListElementType(named, side, out var elementType))
                return FoxServiceSchemaModel.ArrayOf(Build(elementType, side, depth + 1, memo, stack));

            if (IsUnsupportedSchemaPreviewType(named))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            var typeKey = FullTypeName(named);
            if (memo.TryGetValue(typeKey, out var cached))
                return cached;
            if (!stack.Add(typeKey))
                return FoxServiceSchemaModel.Object(Array.Empty<FoxServiceSchemaProperty>());

            var properties = new List<FoxServiceSchemaProperty>();
            foreach (var member in InheritedAndDeclaredMembers(named))
            {
                if (member.IsStatic || HasIgnoredSerializationAttribute(member))
                    continue;
                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(field), Build(field.Type, side, depth + 1, memo, stack)));
                }
                else if (member is IPropertySymbol property)
                {
                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsIndexer
                        || property.GetMethod == null)
                        continue;
                    properties.Add(new FoxServiceSchemaProperty(JsonPropertyName(property), Build(property.Type, side, depth + 1, memo, stack)));
                }
            }

            var model = FoxServiceSchemaModel.Object(properties);
            stack.Remove(typeKey);
            memo[typeKey] = model;
            return model;
        }

        private static bool IsUnsupportedSchemaPreviewType(INamedTypeSymbol named)
        {
            var fullName = FullTypeName(named);
            return named.TypeKind == TypeKind.Delegate
                   || named.TypeKind == TypeKind.Interface
                   || fullName == "System.Object"
                   || FoxServiceDtoTypeNames.IsTaskLike(fullName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                   || IsUnityObjectType(named);
        }

        private static bool TryGetJsonScalarType(INamedTypeSymbol named, out string jsonType)
        {
            jsonType = null;
            switch (named.SpecialType)
            {
                case SpecialType.System_Boolean:
                    jsonType = "boolean";
                    return true;
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    jsonType = "integer";
                    return true;
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    jsonType = "number";
                    return true;
                case SpecialType.System_String:
                case SpecialType.System_Char:
                    jsonType = "string";
                    return true;
            }

            var fullName = FullTypeName(named);
            if (fullName == "System.DateTime"
                || fullName == "System.DateTimeOffset"
                || fullName == "System.Guid"
                || fullName == "System.TimeSpan")
            {
                jsonType = "string";
                return true;
            }

            return false;
        }
    }
}
