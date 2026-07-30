// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Adapts Roslyn FoxRun DTO symbols into encoding-neutral shapes.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;
using static Unity.FoxgloveSDK.SourceGenerators.FoxServiceRoslynTypeHelpers;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynTypeShapeBuilder
    {
        public static bool TryBuild(ITypeSymbol type, out FoxRunTypeShape shape)
        {
            try
            {
                shape = Build(
                    type,
                    0,
                    new Dictionary<string, FoxRunTypeShape>(StringComparer.Ordinal),
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
            catch (OverflowException)
            {
                shape = null;
                return false;
            }
        }

        private static FoxRunTypeShape Build(
            ITypeSymbol type,
            int depth,
            IDictionary<string, FoxRunTypeShape> memo,
            ISet<string> stack)
        {
            if (type == null)
                throw new ArgumentException("A FoxRun field type is required.", nameof(type));
            if (depth > FoxServiceDtoRules.MaxDepth)
                throw new InvalidOperationException(
                    "FOXRUN616: FoxRun DTO nesting exceeds the supported depth.");

            var nullable = IsNullableValueType(type);
            type = UnwrapNullable(type);
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1)
                {
                    throw new InvalidOperationException(
                        "FoxRun MessagePack collections must be one-dimensional.");
                }
                var collectionKind = array.ElementType.SpecialType == SpecialType.System_Byte
                    ? FoxRunCollectionKind.Binary
                    : FoxRunCollectionKind.Array;
                var elementShape = Build(array.ElementType, depth + 1, memo, stack);
                if (elementShape.Kind == FoxRunTypeShapeKind.Collection)
                {
                    throw new InvalidOperationException(
                        "FoxRun MessagePack jagged or nested collections are not supported.");
                }
                return FoxRunTypeShape.Collection(
                    collectionKind,
                    elementShape,
                    nullable);
            }
            if (!(type is INamedTypeSymbol named))
                throw new InvalidOperationException("FoxRun type is not a named type.");
            if (TryGetLockedListElementType(named, out var elementType))
            {
                var elementShape = Build(elementType, depth + 1, memo, stack);
                if (elementShape.Kind == FoxRunTypeShapeKind.Collection)
                {
                    throw new InvalidOperationException(
                        "FoxRun MessagePack jagged or nested collections are not supported.");
                }
                return FoxRunTypeShape.Collection(
                    FoxRunCollectionKind.List,
                    elementShape,
                    nullable);
            }

            var typeName = FullTypeName(named);
            if (TryBuildUnityValueShape(typeName, nullable, out var unityValueShape))
                return unityValueShape;

            var canonicalType = FoxRunCanonicalTypeNormalizer.NormalizeTypeName(typeName);
            if (FoxRunCanonicalTypeNormalizer.IsKnownCanonicalType(canonicalType))
                return FoxRunTypeShape.Canonical(canonicalType, nullable);
            if (named.TypeKind == TypeKind.Enum)
                return BuildEnum(named, memo).WithNullable(nullable);
            if (IsUnsupported(named))
            {
                throw new InvalidOperationException(
                    "FoxRun DTO type '" + FullTypeName(named) + "' is not supported.");
            }

            if (memo.TryGetValue(typeName, out var cached))
            {
                EnsureCachedShapeFitsDepth(cached, depth);
                return cached.WithNullable(nullable);
            }
            if (!stack.Add(typeName))
            {
                throw new InvalidOperationException(
                    "FoxRun DTO graph contains a cycle at '" + typeName + "'.");
            }

            EnsureNoDuplicateDeclaredJsonNames(named);
            var fields = new List<FoxRunTypeField>();
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
                        || property.GetMethod == null
                        || property.GetMethod.DeclaredAccessibility != Accessibility.Public)
                        continue;
                    AddMember(fields, property.Name, JsonPropertyName(property), property.Type,
                        property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly,
                        depth, memo, stack);
                }
            }

            var result = FoxRunTypeShape.Object(
                typeName,
                fields,
                canConstruct: CanConstruct(named),
                isValueType: named.IsValueType);
            stack.Remove(typeName);
            memo[typeName] = result;
            return result.WithNullable(nullable);
        }

        private static void EnsureNoDuplicateDeclaredJsonNames(
            INamedTypeSymbol type)
        {
            var membersByJsonName =
                new Dictionary<string, ISymbol>(StringComparer.Ordinal);
            var lookupMembersByClrName =
                new Dictionary<string, ISymbol>(StringComparer.Ordinal);
            var ambiguousLookupMembers =
                new Dictionary<string, ISymbol[]>(StringComparer.Ordinal);
            var serializableClrNames =
                new HashSet<string>(StringComparer.Ordinal);
            var propertySlots =
                new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for (var current = type;
                 current != null
                 && current.SpecialType != SpecialType.System_Object;
                 current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (!CanAffectClrMemberLookup(member))
                        continue;

                    if (member is IPropertySymbol property)
                    {
                        var propertySlot = RootProperty(property);
                        if (!propertySlots.Add(propertySlot))
                            continue;
                    }

                    var isSerializable = IsSerializableMember(member);
                    if (lookupMembersByClrName.TryGetValue(
                            member.Name,
                            out var sameName)
                        && !SymbolEqualityComparer.Default.Equals(
                            sameName.ContainingType,
                            member.ContainingType)
                        && isSerializable)
                    {
                        if (!ambiguousLookupMembers.ContainsKey(member.Name))
                        {
                            ambiguousLookupMembers.Add(
                                member.Name,
                                new[] { sameName, member });
                        }
                    }
                    else if (!lookupMembersByClrName.ContainsKey(member.Name))
                    {
                        lookupMembersByClrName.Add(member.Name, member);
                    }

                    if (!isSerializable)
                        continue;
                    serializableClrNames.Add(member.Name);

                    if (HasIgnoredSerializationAttribute(member))
                        continue;

                    var jsonName = JsonPropertyName(member);
                    if (membersByJsonName.TryGetValue(
                            jsonName,
                            out var existing))
                    {
                        throw new InvalidOperationException(
                            "FOXRUN616: FoxRun DTO type '"
                            + FullTypeName(type)
                            + "' contains duplicate JSON field name '"
                            + jsonName
                            + "'.");
                    }
                    membersByJsonName.Add(jsonName, member);
                }
            }

            foreach (var name in serializableClrNames.OrderBy(
                         value => value,
                         StringComparer.Ordinal))
            {
                if (!ambiguousLookupMembers.TryGetValue(
                        name,
                        out var collision))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    "FOXRUN616: FoxRun DTO type '"
                    + FullTypeName(type)
                    + "' contains inherited members with ambiguous CLR name '"
                    + name
                    + "' ('"
                    + FullTypeName(collision[0].ContainingType)
                    + "' and '"
                    + FullTypeName(collision[1].ContainingType)
                    + "').");
            }
        }

        private static bool CanAffectClrMemberLookup(ISymbol member)
            => !member.IsImplicitlyDeclared
               && (member is IFieldSymbol
                   || member is IPropertySymbol
                   || member is IEventSymbol
                   || member is INamedTypeSymbol
                   || (member is IMethodSymbol method
                       && method.MethodKind == MethodKind.Ordinary));

        private static IPropertySymbol RootProperty(
            IPropertySymbol property)
        {
            while (property.OverriddenProperty != null)
                property = property.OverriddenProperty;
            return property;
        }

        private static bool IsSerializableMember(ISymbol member)
        {
            if (member is IFieldSymbol field)
            {
                return !field.IsStatic
                       && !field.IsConst
                       && field.DeclaredAccessibility
                       == Accessibility.Public;
            }

            if (member is IPropertySymbol property)
            {
                return !property.IsStatic
                       && property.DeclaredAccessibility
                       == Accessibility.Public
                       && !property.IsIndexer
                       && property.GetMethod != null
                       && property.GetMethod.DeclaredAccessibility
                       == Accessibility.Public;
            }

            return false;
        }

        private static void EnsureCachedShapeFitsDepth(
            FoxRunTypeShape shape,
            int depth)
        {
            if (FoxRunTypeShapeDepth.MaximumRelativeDepth(shape)
                > FoxServiceDtoRules.MaxDepth - depth)
            {
                throw new InvalidOperationException(
                    "FOXRUN616: FoxRun DTO nesting exceeds the supported depth.");
            }
        }

        private static void AddMember(
            ICollection<FoxRunTypeField> fields,
            string memberName,
            string jsonName,
            ITypeSymbol memberType,
            bool canAssign,
            int depth,
            IDictionary<string, FoxRunTypeShape> memo,
            ISet<string> stack)
        {
            var repeated = memberType is IArrayTypeSymbol;
            var collectionKind = repeated
                ? FoxRunCollectionKind.Array
                : FoxRunCollectionKind.None;
            ITypeSymbol elementType = repeated ? ((IArrayTypeSymbol)memberType).ElementType : null;
            if (!repeated
                && memberType is INamedTypeSymbol namedMemberType
                && TryGetLockedListElementType(namedMemberType, out var listElementType))
            {
                repeated = true;
                elementType = listElementType;
                collectionKind = FoxRunCollectionKind.List;
            }

            fields.Add(new FoxRunTypeField(
                jsonName,
                memberName,
                Build(memberType, depth + 1, memo, stack),
                repeated,
                repeatedCollectionKind: collectionKind,
                canAssign: canAssign,
                isNullable: IsNullableValueType(repeated ? elementType : memberType)));
        }

        private static bool IsNullableValueType(ITypeSymbol type)
            => type is INamedTypeSymbol named
               && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
               && named.TypeArguments.Length == 1;

        private static FoxRunTypeShape BuildEnum(
            INamedTypeSymbol type,
            IDictionary<string, FoxRunTypeShape> memo)
        {
            var typeName = FullTypeName(type);
            if (memo.TryGetValue(typeName, out var cached))
                return cached;

            var values = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => field.HasConstantValue)
                .Select(field => new FoxRunEnumValue(field.Name, CheckedEnumValue(type, field)))
                .OrderBy(value => value.Number)
                .ThenBy(value => value.Name, StringComparer.Ordinal)
                .ToList();

            var result = FoxRunTypeShape.Enum(typeName, values);
            memo[typeName] = result;
            return result;
        }

        private static int CheckedEnumValue(INamedTypeSymbol type, IFieldSymbol field)
        {
            var value = Convert.ToDecimal(field.ConstantValue);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "FOXRUN616: FoxRun MessagePack enum value '" + FullTypeName(type) + "." + field.Name
                    + "' is outside the signed Int32 range.");
            }
            return decimal.ToInt32(value);
        }

        private static bool IsUnsupported(INamedTypeSymbol type)
        {
            var typeName = FullTypeName(type);
            return FoxServiceDtoTypeNames.IsScalar(typeName)
                   || type.SpecialType == SpecialType.System_Object
                   || type.TypeKind == TypeKind.Interface
                   || type.IsAbstract
                   || type.TypeKind == TypeKind.Delegate
                   || type.IsGenericType
                   || string.Equals(
                       typeName,
                       "System.ValueTuple",
                       StringComparison.Ordinal)
                   || IsDelegateType(type)
                   || IsUnityObjectType(type)
                   || FoxServiceDtoTypeNames.IsTaskLike(typeName)
                   || FoxServiceDtoTypeNames.IsUnsafeRuntimeHandle(typeName);
        }

        private static bool CanConstruct(INamedTypeSymbol type)
            => type.IsValueType
               || type.InstanceConstructors.Any(constructor =>
                   constructor.Parameters.Length == 0
                   && constructor.DeclaredAccessibility == Accessibility.Public);

        private static bool TryGetLockedListElementType(
            INamedTypeSymbol type,
            out ITypeSymbol elementType)
        {
            elementType = null;
            if (type == null
                || !type.IsGenericType
                || type.TypeArguments.Length != 1)
            {
                return false;
            }

            var definition = type.OriginalDefinition;
            var namespaceName = definition.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!string.Equals(
                    namespaceName,
                    "System.Collections.Generic",
                    StringComparison.Ordinal)
                || (definition.MetadataName != "List`1"
                    && definition.MetadataName != "IList`1"
                    && definition.MetadataName != "IReadOnlyList`1"))
            {
                return false;
            }

            elementType = type.TypeArguments[0];
            return true;
        }

        private static bool TryBuildUnityValueShape(
            string typeName,
            bool nullable,
            out FoxRunTypeShape shape)
        {
            string[] components;
            switch (typeName ?? string.Empty)
            {
                case "UnityEngine.Vector2":
                    components = new[] { "x", "y" };
                    break;
                case "UnityEngine.Vector3":
                    components = new[] { "x", "y", "z" };
                    break;
                case "UnityEngine.Quaternion":
                    components = new[] { "x", "y", "z", "w" };
                    break;
                case "UnityEngine.Color":
                    components = new[] { "r", "g", "b", "a" };
                    break;
                default:
                    shape = null;
                    return false;
            }

            shape = FoxRunTypeShape.Object(
                typeName,
                components
                    .Select(component => new FoxRunTypeField(
                        component,
                        component,
                        FoxRunTypeShape.Canonical("float32")))
                    .ToList(),
                nullable,
                canConstruct: true,
                isValueType: true);
            return true;
        }
    }
}
