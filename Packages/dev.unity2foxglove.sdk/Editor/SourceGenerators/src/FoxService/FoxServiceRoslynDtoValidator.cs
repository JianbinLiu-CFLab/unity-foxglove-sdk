// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn-backed FoxService DTO validation adapter.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;
using static Unity.FoxgloveSDK.SourceGenerators.FoxServiceRoslynTypeHelpers;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxServiceRoslynDtoValidator
    {
        public static IEnumerable<ServiceDiagnostic> ValidateServiceDtoType(
            ITypeSymbol type,
            string side,
            string rootPath,
            string serviceName,
            Location location)
        {
            var diagnostics = new List<FoxServiceDtoDiagnostic>();
            var stack = new HashSet<string>(StringComparer.Ordinal);
            var validatedTypes = new HashSet<string>(StringComparer.Ordinal);
            ValidateServiceDtoType(type, side, rootPath, type, diagnostics, stack, validatedTypes, 0);
            return diagnostics.Select(diagnostic => new ServiceDiagnostic(
                diagnostic.Id,
                location,
                diagnostic.FormatTarget(serviceName)));
        }

        private static void ValidateServiceDtoType(
            ITypeSymbol type,
            string side,
            string path,
            ITypeSymbol rootType,
            List<FoxServiceDtoDiagnostic> diagnostics,
            HashSet<string> stack,
            HashSet<string> validatedTypes,
            int depth)
        {
            if (type == null || type.SpecialType == SpecialType.System_Void)
                return;

            type = UnwrapNullable(type);
            var typeName = DiagnosticTypeName(type);
            var rootName = DisplayTypeName(rootType);

            if (depth > FoxServiceDtoRules.MaxDepth)
            {
                AddDtoDiagnostic(FoxServiceDtoRules.DepthDiagnosticId, side, rootName, path, typeName, "DTO graph exceeds the supported traversal depth.", diagnostics);
                return;
            }

            if (type.TypeKind == TypeKind.Pointer || type.TypeKind == TypeKind.TypeParameter)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Pointer and open generic DTO members cannot be serialized safely.", diagnostics);
                return;
            }

            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1)
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Only single-dimensional arrays are supported.", diagnostics);
                    return;
                }

                ValidateServiceDtoType(array.ElementType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (!(type is INamedTypeSymbol named))
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "DTO member type is not a supported named type.", diagnostics);
                return;
            }

            if (named.IsUnboundGenericType
                || named.TypeArguments.Any(argument => argument.TypeKind == TypeKind.TypeParameter)
                || named.IsRefLikeType)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Open generic and by-ref-like DTO members are unsupported.", diagnostics);
                return;
            }

            if (IsScalarDtoType(named) || named.TypeKind == TypeKind.Enum)
                return;

            var fullName = FullTypeName(named);
            var stackKey = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (validatedTypes.Contains(stackKey))
                return;

            if (FoxServiceDtoTypeNames.IsTaskLike(fullName)
                || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(fullName)
                || FoxServiceDtoTypeNames.IsFunctionPointerLike(fullName)
                || IsDelegateType(named)
                || IsUnityObjectType(named)
                || named.SpecialType == SpecialType.System_Object)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "DTO member type is not JSON DTO serializable.", diagnostics);
                return;
            }

            if (TryGetDictionaryValueType(named, out var keyType, out var valueType))
            {
                if (!IsStringDtoType(UnwrapNullable(keyType)))
                {
                    AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Dictionary DTO members must use string keys.", diagnostics);
                    return;
                }

                ValidateServiceDtoType(valueType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (TryGetListElementType(named, side, out var elementType))
            {
                ValidateServiceDtoType(elementType, side, path, rootType, diagnostics, stack, validatedTypes, depth + 1);
                return;
            }

            if (named.TypeKind == TypeKind.Interface)
            {
                AddUnsupportedDtoDiagnostic(side, rootName, path, typeName, "Interface DTO members are unsupported unless they are a known collection contract.", diagnostics);
                return;
            }

            if (!stack.Add(stackKey))
            {
                AddDtoDiagnostic(FoxServiceDtoRules.CycleDiagnosticId, side, rootName, path, typeName, "DTO graph contains a recursive reference.", diagnostics);
                return;
            }

            var diagnosticCountBeforeMembers = diagnostics.Count;
            foreach (var member in InheritedAndDeclaredMembers(named))
            {
                if (member.IsStatic)
                    continue;

                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    if (HasIgnoredSerializationAttribute(field))
                    {
                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.Type), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (field.IsReadOnly)
                    {
                        if (string.Equals(
                                side,
                                FoxServiceDtoRules.ResponseSide,
                                StringComparison.Ordinal))
                        {
                            ValidateServiceDtoType(field.Type, side, path + "." + field.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                            continue;
                        }

                        AddDtoWarning(side, rootName, path + "." + field.Name, DiagnosticTypeName(field.Type), "Readonly fields may serialize but may not round-trip from request JSON.", diagnostics);
                        continue;
                    }
                    ValidateServiceDtoType(field.Type, side, path + "." + field.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                    continue;
                }

                if (member is IPropertySymbol property)
                {
                    if (property.DeclaredAccessibility != Accessibility.Public
                        || property.IsIndexer
                        || property.GetMethod == null)
                        continue;
                    if (HasIgnoredSerializationAttribute(property))
                    {
                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.Type), "Member is ignored by serialization attributes.", diagnostics);
                        continue;
                    }
                    if (property.SetMethod == null)
                    {
                        if (string.Equals(
                                side,
                                FoxServiceDtoRules.ResponseSide,
                                StringComparison.Ordinal))
                        {
                            ValidateServiceDtoType(property.Type, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                            continue;
                        }

                        if (TryGetListElementType(property.Type, side, out var getOnlyElementType)
                            && IsMutableCollectionContract(property.Type))
                        {
                            ValidateServiceDtoType(getOnlyElementType, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                            continue;
                        }
                        AddDtoWarning(side, rootName, path + "." + property.Name, DiagnosticTypeName(property.Type), "Get-only properties are not populated during request deserialization.", diagnostics);
                        continue;
                    }
                    ValidateServiceDtoType(property.Type, side, path + "." + property.Name, rootType, diagnostics, stack, validatedTypes, depth + 1);
                }
            }

            stack.Remove(stackKey);
            if (diagnostics.Count == diagnosticCountBeforeMembers)
                validatedTypes.Add(stackKey);
        }

        private static void AddUnsupportedDtoDiagnostic(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.UnsupportedDiagnosticId(side), side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoWarning(
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => AddDtoDiagnostic(FoxServiceDtoRules.WarningDiagnosticId, side, rootType, path, offendingType, reason, diagnostics);

        private static void AddDtoDiagnostic(
            string id,
            string side,
            string rootType,
            string path,
            string offendingType,
            string reason,
            List<FoxServiceDtoDiagnostic> diagnostics)
            => diagnostics.Add(new FoxServiceDtoDiagnostic(
                id,
                id == FoxServiceDtoRules.WarningDiagnosticId || id == FoxServiceDtoRules.DepthDiagnosticId,
                side,
                rootType,
                path,
                offendingType,
                reason));
    }
}
