// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Builds deterministic custom ROS2 DTO shapes from Roslyn metadata symbols.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynRos2CustomDtoShapeBuilder
    {
        public static FoxRunRos2CustomDtoShape Build(ITypeSymbol type, Compilation compilation)
        {
            var typeName = MetadataTypeName(type);
            if (!(type is INamedTypeSymbol named))
            {
                // Arrays, pointers, and type parameters are not DTO roots.  Do
                // not lose their identity by casting to INamedTypeSymbol: the
                // reflection fallback keeps that identity in its stable
                // FOXRUN606 shape, and descriptor parity relies on it.
                var diagnostics = new List<string>();
                AddUnsupported(
                    typeName,
                    "Custom ROS2 DTO roots and nested values must be concrete, non-generic classes.",
                    diagnostics);
                return Unsupported(typeName, false, diagnostics);
            }

            return BuildDto(named, typeName, new HashSet<string>(StringComparer.Ordinal));
        }

        private static FoxRunRos2CustomDtoShape BuildDto(
            INamedTypeSymbol type,
            string path,
            ISet<string> stack)
        {
            var typeName = MetadataTypeName(type);
            var diagnostics = new List<string>();
            var isConstructible = IsConcreteDto(type)
                && type.InstanceConstructors.Any(ctor =>
                    ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0);

            if (!IsConcreteDto(type))
            {
                AddUnsupported(path, "Custom ROS2 DTO roots and nested values must be concrete, non-generic classes.", diagnostics);
                return Unsupported(typeName, isConstructible, diagnostics);
            }

            if (!isConstructible)
                AddNonConstructible(path, diagnostics);

            if (!stack.Add(typeName))
            {
                AddUnsupported(path, "Custom ROS2 DTO graph contains a cyclic type reference.", diagnostics);
                return Unsupported(typeName, isConstructible, diagnostics);
            }

            var members = new List<FoxRunRos2CustomDtoMemberShape>();
            var rosNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in PublicInstanceMembers(type).OrderBy(member => member.Name, StringComparer.Ordinal))
                BuildMember(member, path, stack, members, rosNames, diagnostics);
            stack.Remove(typeName);

            var canonical = diagnostics.Count == 0
                ? FoxRunRos2CustomIdentity.BuildCanonicalIdentity(typeName, members)
                : string.Empty;
            return new FoxRunRos2CustomDtoShape(
                typeName,
                canonical,
                diagnostics.Count == 0 ? FoxRunRos2CustomIdentity.BuildPayloadIdentity(typeName, canonical) : string.Empty,
                isConstructible,
                diagnostics.Count == 0,
                members,
                diagnostics);
        }

        private static FoxRunRos2CustomDtoShape Unsupported(
            string typeName,
            bool isConstructible,
            IReadOnlyList<string> diagnostics)
            => new FoxRunRos2CustomDtoShape(typeName, string.Empty, string.Empty, isConstructible, false,
                Array.Empty<FoxRunRos2CustomDtoMemberShape>(), diagnostics);

        private static void BuildMember(
            ISymbol member,
            string rootPath,
            ISet<string> stack,
            ICollection<FoxRunRos2CustomDtoMemberShape> members,
            ISet<string> rosNames,
            ICollection<string> diagnostics)
        {
            var path = rootPath + "." + member.Name;
            var rosName = FoxRunRos2CustomNamingPolicy.ToRosFieldName(member.Name);
            if (string.IsNullOrEmpty(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member names must contain at least one letter or digit.", diagnostics);
                return;
            }

            if (FoxRunRos2CustomNamingPolicy.IsReservedUserField(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member '" + rosName + "' uses the reserved foxrun_ prefix.", diagnostics);
                return;
            }

            if (!rosNames.Add(rosName))
            {
                AddUnsupported(path, "Custom ROS2 DTO member name collides after ROS snake_case conversion: '" + rosName + "'.", diagnostics);
                return;
            }

            if (!CanRead(member) || !CanWrite(member))
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    FoxRunRos2CustomDtoDiagnostic.NonWritableInboundMember,
                    path,
                    "Custom ROS2 DTO members must be readable and writable for native inbound application."));
                return;
            }

            var valueType = MemberType(member);
            var hasPresence = IsReferenceOrNullable(valueType);
            if (TryNullable(valueType, out var nullableElement))
            {
                valueType = nullableElement;
                hasPresence = true;
            }

            if (TrySequence(valueType, out var sequenceElement, out var sequenceRepresentation, out var sequenceError))
            {
                if (!string.IsNullOrEmpty(sequenceError))
                {
                    AddUnsupported(path, sequenceError, diagnostics);
                    return;
                }

                if (TryNullable(sequenceElement, out _))
                {
                    AddUnsupported(path, "Custom ROS2 DTO sequences cannot contain nullable elements.", diagnostics);
                    return;
                }

                var sequenceRosType = RosType(sequenceElement, path, stack, diagnostics, out var nestedShape);
                if (string.IsNullOrEmpty(sequenceRosType))
                    return;
                members.Add(new FoxRunRos2CustomDtoMemberShape(
                    member.Name, rosName, FoxRunRos2CustomDtoMemberKind.Sequence,
                    MetadataTypeName(MemberType(member)), sequenceRosType + "[]", MetadataTypeName(sequenceElement),
                    nestedShape?.CanonicalIdentity ?? string.Empty, true, true, true,
                    sequenceRepresentation, nestedShape));
                return;
            }

            var rosType = RosType(valueType, path, stack, diagnostics, out var nested);
            if (string.IsNullOrEmpty(rosType))
                return;
            var kind = valueType.SpecialType == SpecialType.System_String
                ? FoxRunRos2CustomDtoMemberKind.String
                : valueType.TypeKind == TypeKind.Enum
                    ? FoxRunRos2CustomDtoMemberKind.Enum
                    : nested != null ? FoxRunRos2CustomDtoMemberKind.NestedDto : FoxRunRos2CustomDtoMemberKind.Scalar;
            members.Add(new FoxRunRos2CustomDtoMemberShape(
                member.Name, rosName, kind, MetadataTypeName(MemberType(member)), rosType, string.Empty,
                nested?.CanonicalIdentity ?? string.Empty, hasPresence, true, true,
                nestedShape: nested));
        }

        private static string RosType(
            ITypeSymbol type,
            string path,
            ISet<string> stack,
            ICollection<string> diagnostics,
            out FoxRunRos2CustomDtoShape nested)
        {
            nested = null;
            var scalar = ScalarRosType(type?.SpecialType ?? SpecialType.None);
            if (!string.IsNullOrEmpty(scalar))
                return scalar;
            if (type?.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
                return ScalarRosType(enumType.EnumUnderlyingType?.SpecialType ?? SpecialType.None);

            if (IsExplicitlyUnsupported(type))
            {
                AddUnsupported(path, "Custom ROS2 DTO member type '" + DisplayTypeName(type) + "' cannot be represented without loss.", diagnostics);
                return string.Empty;
            }

            if (!IsConcreteDto(type as INamedTypeSymbol))
            {
                AddUnsupported(path, "Unsupported custom ROS2 DTO member type '" + DisplayTypeName(type) + "'.", diagnostics);
                return string.Empty;
            }

            nested = BuildDto((INamedTypeSymbol)type, path, stack);
            foreach (var diagnostic in nested.Diagnostics)
                diagnostics.Add(diagnostic);
            return nested.IsSupported ? nested.PayloadIdentity : string.Empty;
        }

        private static IEnumerable<ISymbol> PublicInstanceMembers(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol field
                    && !field.IsStatic
                    && !field.IsConst
                    && field.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return field;
                }
                else if (member is IPropertySymbol property
                         && !property.IsStatic
                         && !property.IsIndexer
                         && property.DeclaredAccessibility == Accessibility.Public)
                {
                    yield return property;
                }
            }
        }

        private static ITypeSymbol MemberType(ISymbol member)
            => member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type;

        private static bool CanRead(ISymbol member)
            => member is IFieldSymbol
               || ((IPropertySymbol)member).GetMethod?.DeclaredAccessibility == Accessibility.Public;

        private static bool CanWrite(ISymbol member)
            => member is IFieldSymbol field
                ? !field.IsReadOnly
                : ((IPropertySymbol)member).SetMethod?.DeclaredAccessibility == Accessibility.Public
                  && ((IPropertySymbol)member).SetMethod.IsInitOnly == false;

        private static bool TryNullable(ITypeSymbol type, out ITypeSymbol element)
        {
            element = null;
            if (!(type is INamedTypeSymbol named)
                || !named.IsGenericType
                || !string.Equals(MetadataDefinitionName(named), "System.Nullable", StringComparison.Ordinal))
                return false;
            element = named.TypeArguments[0];
            return true;
        }

        private static bool TrySequence(
            ITypeSymbol type,
            out ITypeSymbol element,
            out FoxRunRos2CustomDtoSequenceRepresentation representation,
            out string error)
        {
            element = null;
            representation = FoxRunRos2CustomDtoSequenceRepresentation.None;
            error = string.Empty;
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1)
                {
                    error = "Only one-dimensional custom ROS2 DTO sequences are supported.";
                    return true;
                }

                element = array.ElementType;
                if (element is IArrayTypeSymbol)
                    error = "Jagged custom ROS2 DTO arrays are unsupported.";
                representation = FoxRunRos2CustomDtoSequenceRepresentation.Array;
                return true;
            }

            if (type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1
                && string.Equals(MetadataDefinitionName(named), "System.Collections.Generic.List", StringComparison.Ordinal))
            {
                element = named.TypeArguments[0];
                if (element is IArrayTypeSymbol)
                    error = "Jagged custom ROS2 DTO arrays are unsupported.";
                representation = FoxRunRos2CustomDtoSequenceRepresentation.List;
                return true;
            }

            return false;
        }

        private static bool IsConcreteDto(INamedTypeSymbol type)
            => type != null
               && type.TypeKind == TypeKind.Class
               && !type.IsAbstract
               && !type.IsGenericType
               && type.Arity == 0
               && !IsExplicitlyUnsupported(type);

        private static bool IsReferenceOrNullable(ITypeSymbol type)
            => type != null && (!type.IsValueType || TryNullable(type, out _));

        private static bool IsExplicitlyUnsupported(ITypeSymbol type)
        {
            if (type == null
                || type.SpecialType == SpecialType.System_Object
                || type.SpecialType == SpecialType.System_Char
                || type.SpecialType == SpecialType.System_Decimal
                || type.TypeKind == TypeKind.Pointer
                || type.TypeKind == TypeKind.Delegate
                || type.TypeKind == TypeKind.Interface
                || type.IsAbstract)
                return true;
            if (IsUnityObject(type as INamedTypeSymbol))
                return true;
            if (!(type is INamedTypeSymbol named) || !named.IsGenericType)
                return false;
            var definition = MetadataDefinitionName(named);
            return string.Equals(definition, "System.Collections.Generic.Dictionary", StringComparison.Ordinal)
                   || string.Equals(definition, "System.Collections.Generic.HashSet", StringComparison.Ordinal)
                   || string.Equals(definition, "System.Collections.Generic.ISet", StringComparison.Ordinal)
                   || string.Equals(definition, "System.Collections.Generic.IDictionary", StringComparison.Ordinal);
        }

        private static bool IsUnityObject(INamedTypeSymbol type)
        {
            for (var candidate = type; candidate != null; candidate = candidate.BaseType)
            {
                if (string.Equals(MetadataDefinitionName(candidate), "UnityEngine.Object", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static string ScalarRosType(SpecialType type)
        {
            switch (type)
            {
                case SpecialType.System_Boolean: return "bool";
                case SpecialType.System_SByte: return "int8";
                case SpecialType.System_Byte: return "uint8";
                case SpecialType.System_Int16: return "int16";
                case SpecialType.System_UInt16: return "uint16";
                case SpecialType.System_Int32: return "int32";
                case SpecialType.System_UInt32: return "uint32";
                case SpecialType.System_Int64: return "int64";
                case SpecialType.System_UInt64: return "uint64";
                case SpecialType.System_Single: return "float32";
                case SpecialType.System_Double: return "float64";
                case SpecialType.System_String: return "string";
                default: return string.Empty;
            }
        }

        private static void AddUnsupported(string path, string message, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(FoxRunRos2CustomDtoDiagnostic.UnsupportedShape, path, message));

        private static void AddNonConstructible(string path, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                FoxRunRos2CustomDtoDiagnostic.NonConstructible,
                path,
                "Custom ROS2 DTO roots and nested values require a public parameterless constructor for native inbound application."));

        private static string DisplayTypeName(ITypeSymbol type)
            => type?.SpecialType == SpecialType.System_Decimal ? "Decimal" : MetadataTypeName(type);

        private static string MetadataDefinitionName(INamedTypeSymbol type)
        {
            var ns = type?.ContainingNamespace?.ToDisplayString();
            return string.IsNullOrEmpty(ns) ? type?.Name ?? string.Empty : ns + "." + type.Name;
        }

        private static string MetadataTypeName(ITypeSymbol type)
        {
            if (type == null)
                return string.Empty;
            if (type is IArrayTypeSymbol array)
                return MetadataTypeName(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
            if (!(type is INamedTypeSymbol named))
                return type.Name ?? string.Empty;
            if (named.IsGenericType)
                return MetadataDefinitionName(named) + "<" + string.Join(",", named.TypeArguments.Select(MetadataTypeName)) + ">";
            var containing = named.ContainingType == null
                ? named.ContainingNamespace?.ToDisplayString()
                : MetadataTypeName(named.ContainingType);
            return string.IsNullOrEmpty(containing) ? named.Name : containing + "." + named.Name;
        }
    }
}
