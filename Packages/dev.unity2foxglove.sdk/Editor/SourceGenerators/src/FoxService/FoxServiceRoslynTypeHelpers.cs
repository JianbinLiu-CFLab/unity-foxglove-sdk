// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Roslyn symbol helpers shared by FoxService source-generator adapters.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxServiceRoslynTypeHelpers
    {
        public static IEnumerable<ISymbol> InheritedAndDeclaredMembers(INamedTypeSymbol type)
        {
            var seenJsonNames = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
            {
                var members = new List<ISymbol>(current.GetMembers().Length);
                foreach (var member in current.GetMembers())
                    members.Add(member);
                members.Sort((left, right) => MemberOrder(left).CompareTo(MemberOrder(right)));

                foreach (var member in members)
                {
                    if (member is IFieldSymbol field)
                    {
                        if (!CanParticipateInJsonNameDedup(field))
                            continue;
                        if (seenJsonNames.Add(JsonPropertyName(field)))
                            yield return field;
                    }
                    else if (member is IPropertySymbol property)
                    {
                        if (!CanParticipateInJsonNameDedup(property))
                            continue;
                        if (seenJsonNames.Add(JsonPropertyName(property)))
                            yield return property;
                    }
                }
            }
        }

        public static bool CanParticipateInJsonNameDedup(ISymbol member)
        {
            if (member.IsStatic)
                return false;

            if (member is IFieldSymbol field)
                return !field.IsConst && field.DeclaredAccessibility == Accessibility.Public;

            if (member is IPropertySymbol property)
                return property.DeclaredAccessibility == Accessibility.Public;

            return false;
        }

        public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named
                && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
            return type;
        }

        public static bool IsScalarDtoType(INamedTypeSymbol named)
            => IsPrimitiveDtoType(named) || FoxServiceDtoTypeNames.IsScalar(FullTypeName(named));

        public static bool IsPrimitiveDtoType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_String:
                case SpecialType.System_Char:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsStringDtoType(ITypeSymbol type)
            => type != null && type.SpecialType == SpecialType.System_String;

        public static bool TryGetListElementType(INamedTypeSymbol named, out ITypeSymbol elementType)
            => TryGetListElementType(named, FoxServiceDtoRules.RequestSide, out elementType);

        public static bool TryGetListElementType(ITypeSymbol type, string side, out ITypeSymbol elementType)
            => TryGetListElementType(type as INamedTypeSymbol, side, out elementType);

        public static bool TryGetListElementType(INamedTypeSymbol named, string side, out ITypeSymbol elementType)
        {
            elementType = null;
            if (named == null || named.TypeArguments.Length != 1)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            if (!FoxServiceDtoTypeNames.IsListContract(contract, side))
                return false;

            elementType = named.TypeArguments[0];
            return true;
        }

        public static bool IsMutableCollectionContract(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named) || named.TypeArguments.Length != 1)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return FoxServiceDtoTypeNames.IsMutableCollectionContract(contract);
        }

        public static bool TryGetDictionaryValueType(INamedTypeSymbol named, out ITypeSymbol keyType, out ITypeSymbol valueType)
        {
            keyType = null;
            valueType = null;
            if (named.TypeArguments.Length != 2)
                return false;

            var contract = named.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            if (!FoxServiceDtoTypeNames.IsDictionaryContract(contract))
                return false;

            keyType = named.TypeArguments[0];
            valueType = named.TypeArguments[1];
            return true;
        }

        public static bool IsDelegateType(INamedTypeSymbol named)
        {
            for (var current = named; current != null; current = current.BaseType)
            {
                var fullName = FullTypeName(current);
                if (fullName == "System.Delegate" || fullName == "System.MulticastDelegate")
                    return true;
            }
            return false;
        }

        public static bool IsUnityObjectType(INamedTypeSymbol named)
        {
            for (var current = named; current != null; current = current.BaseType)
            {
                if (FullTypeName(current) == "UnityEngine.Object")
                    return true;
            }
            return false;
        }

        public static bool HasIgnoredSerializationAttribute(ISymbol symbol)
            => symbol.GetAttributes().Any(attribute =>
            {
                var name = attribute.AttributeClass == null ? string.Empty : FullTypeName(attribute.AttributeClass);
                return name == "Newtonsoft.Json.JsonIgnoreAttribute"
                       || name == "System.Text.Json.Serialization.JsonIgnoreAttribute"
                       || name == "System.NonSerializedAttribute";
            });

        public static int MemberOrder(ISymbol symbol)
        {
            foreach (var candidate in symbol.Locations)
            {
                if (candidate.IsInSource)
                    return candidate.SourceSpan.Start;
            }

            return int.MaxValue;
        }

        public static string JsonPropertyName(ISymbol member)
        {
            foreach (var attribute in member.GetAttributes())
            {
                var attributeName = FullTypeName(attribute.AttributeClass);
                if (attributeName != "Newtonsoft.Json.JsonPropertyAttribute")
                    continue;

                foreach (var namedArgument in attribute.NamedArguments)
                {
                    if (namedArgument.Key == "PropertyName"
                        && namedArgument.Value.Value is string namedValue
                        && !string.IsNullOrWhiteSpace(namedValue))
                        return namedValue;
                }

                if (attribute.ConstructorArguments.Length > 0
                    && attribute.ConstructorArguments[0].Value is string constructorValue
                    && !string.IsNullOrWhiteSpace(constructorValue))
                    return constructorValue;
            }

            return member.Name;
        }

        public static string FullTypeName(ITypeSymbol type)
            => type == null
                ? string.Empty
                : FoxServiceDtoTypeNames.Normalize(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty));

        public static string DisplayTypeName(ITypeSymbol type)
            => type == null
                ? string.Empty
                : FoxServiceDtoTypeNames.Normalize(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

        public static string DiagnosticTypeName(ITypeSymbol type)
        {
            if (type == null)
                return string.Empty;

            if (type.SpecialType == SpecialType.System_Object)
                return "object";

            if (IsPrimitiveDtoType(type))
                return DisplayTypeName(type);

            return FullTypeName(type);
        }
    }
}
