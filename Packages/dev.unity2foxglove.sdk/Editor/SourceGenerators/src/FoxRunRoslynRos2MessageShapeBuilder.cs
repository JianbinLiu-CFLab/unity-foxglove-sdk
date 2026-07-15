// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Builds host-neutral native ROS2 copy shapes from Roslyn metadata symbols.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    internal static class FoxRunRoslynRos2MessageShapeBuilder
    {
        private const string Ros2MessageMetadataName = "ROS2.Message";
        private const string Ros2MessageInternalsMetadataName = "ROS2.Internal.MessageInternals";
        private const string Ros2ExtendedDisposableMetadataName = "ROS2.IExtendedDisposable";
        // The tracked Humble/Jazzy/Lyrical packages define the public
        // ROS2.Message contract in ros2cs_common; ros2cs_core consumes it.
        private const string Ros2MessageAssemblyName = "ros2cs_common";

        public static FoxRunRos2MessageShape Build(ITypeSymbol type, Compilation compilation)
        {
            var contract = compilation?.GetTypeByMetadataName(Ros2MessageMetadataName);
            if (contract != null
                && !string.Equals(contract.ContainingAssembly?.Identity.Name, Ros2MessageAssemblyName, StringComparison.Ordinal))
                contract = null;
            return BuildMessage(
                type as INamedTypeSymbol,
                contract,
                MetadataTypeName(type),
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static FoxRunRos2MessageShape BuildMessage(
            INamedTypeSymbol type,
            INamedTypeSymbol contract,
            string path,
            ISet<string> stack)
        {
            var typeName = MetadataTypeName(type);
            var diagnostics = new List<string>();
            var implements = ImplementsContract(type, contract);
            var canonical = implements ? CanonicalRosType(type, path, diagnostics) : string.Empty;
            if (!implements)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN038", path,
                    "Type must implement ROS2.Message from ros2cs_common by metadata identity."));
            }

            var hasConstructor = type != null
                && !type.IsAbstract
                && type.InstanceConstructors.Any(ctor =>
                    ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0);
            if (implements && !hasConstructor)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN039", path,
                    "Native ROS2 message type requires a public parameterless constructor."));
            }

            var members = new List<FoxRunRos2MessageMemberShape>();
            if (implements)
            {
                if (!stack.Add(typeName))
                {
                    diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                        "FOXRUN042", path,
                        "Native ROS2 message graph contains a recursive reference."));
                }
                else
                {
                    BuildMembers(type, contract, path, stack, members, diagnostics);
                    stack.Remove(typeName);
                }
            }

            var identity = diagnostics.Count == 0 ? BuildIdentity(canonical, members) : string.Empty;
            return new FoxRunRos2MessageShape(
                "global::" + typeName,
                canonical,
                hasConstructor,
                implements,
                identity,
                members,
                diagnostics);
        }

        private static void BuildMembers(
            INamedTypeSymbol type,
            INamedTypeSymbol contract,
            string rootPath,
            ISet<string> stack,
            ICollection<FoxRunRos2MessageMemberShape> members,
            ICollection<string> diagnostics)
        {
            foreach (var symbol in PublicInstanceMembers(type).OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                var memberType = MemberType(symbol);
                var canRead = CanRead(symbol);
                var canWrite = CanWrite(symbol);
                var path = rootPath + "." + symbol.Name;

                if (TrySequence(memberType, out var elementType, out var representation, out var isUnsupportedRank))
                {
                    if (isUnsupportedRank)
                    {
                        AddUnsupported(path, "Only one-dimensional native ROS2 sequences are supported.", diagnostics);
                        continue;
                    }

                    var fixedArray = representation == FoxRunRos2SequenceRepresentation.Array && canRead && !canWrite;
                    if (!canRead || (!canWrite && !fixedArray))
                    {
                        AddNotWritable(path, diagnostics);
                        continue;
                    }

                    var nestedIdentity = string.Empty;
                    FoxRunRos2MessageShape nestedShape = null;
                    if (!IsSupportedLeaf(elementType) && ImplementsContract(elementType as INamedTypeSymbol, contract))
                    {
                        var nested = BuildMessage(elementType as INamedTypeSymbol, contract, path + "[]", stack);
                        foreach (var diagnostic in nested.Diagnostics)
                            diagnostics.Add(diagnostic);
                        nestedIdentity = nested.CopyShapeIdentity;
                        nestedShape = nested;
                    }
                    else if (!IsSupportedLeaf(elementType))
                    {
                        AddUnsupported(path, "Sequence element type '" + MetadataTypeName(elementType) + "' cannot be deep-copied safely.", diagnostics);
                        continue;
                    }

                    members.Add(new FoxRunRos2MessageMemberShape(
                        symbol.Name,
                        FoxRunRos2MessageMemberKind.Sequence,
                        MetadataTypeName(memberType),
                        MetadataTypeName(elementType),
                        nestedIdentity,
                        canRead,
                        canWrite,
                        fixedArray ? FoxRunRos2SequenceRepresentation.FixedArray : representation,
                        nestedShape: nestedShape));
                    continue;
                }

                if (!canRead || !canWrite)
                {
                    AddNotWritable(path, diagnostics);
                    continue;
                }

                var leafKind = LeafKind(memberType);
                if (leafKind != null)
                {
                    members.Add(new FoxRunRos2MessageMemberShape(
                        symbol.Name, leafKind.Value, MetadataTypeName(memberType), "", "", canRead, canWrite));
                    continue;
                }

                if (ImplementsContract(memberType as INamedTypeSymbol, contract))
                {
                    var nested = BuildMessage(memberType as INamedTypeSymbol, contract, path, stack);
                    foreach (var diagnostic in nested.Diagnostics)
                        diagnostics.Add(diagnostic);
                    members.Add(new FoxRunRos2MessageMemberShape(
                        symbol.Name,
                        FoxRunRos2MessageMemberKind.NestedMessage,
                        MetadataTypeName(memberType),
                        "",
                        nested.CopyShapeIdentity,
                        canRead,
                        canWrite,
                        nestedShape: nested));
                    continue;
                }

                AddUnsupported(path, "Unsupported native ROS2 message member type '" + MetadataTypeName(memberType) + "'.", diagnostics);
            }
        }

        private static IEnumerable<ISymbol> PublicInstanceMembers(INamedTypeSymbol type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var infrastructureImplementations = Ros2csInfrastructureImplementations(type);
            // Generated ros2cs payload properties are declared on the concrete
            // message. Members that actually implement ros2cs lifecycle/type-
            // support interfaces are not wire data and must never enter a copy.
            foreach (var member in type.GetMembers())
            {
                if (infrastructureImplementations.Contains(member))
                    continue;
                var supported = member is IFieldSymbol field
                    ? !field.IsStatic && !field.IsConst && field.DeclaredAccessibility == Accessibility.Public
                    : member is IPropertySymbol property
                      && !property.IsStatic
                      && !property.IsIndexer
                      && property.DeclaredAccessibility == Accessibility.Public;
                if (supported && names.Add(member.Name))
                    yield return member;
            }
        }

        private static ISet<ISymbol> Ros2csInfrastructureImplementations(INamedTypeSymbol type)
        {
            var implementations = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var contract in type.AllInterfaces.Where(IsRos2csInfrastructureContract))
            {
                foreach (var contractMember in contract.GetMembers())
                {
                    var implementation = type.FindImplementationForInterfaceMember(contractMember);
                    if (implementation != null)
                        implementations.Add(implementation);
                }
            }
            return implementations;
        }

        private static bool IsRos2csInfrastructureContract(INamedTypeSymbol candidate)
        {
            if (candidate == null
                || !string.Equals(candidate.ContainingAssembly?.Identity.Name, Ros2MessageAssemblyName, StringComparison.Ordinal))
                return false;

            var name = candidate.ToDisplayString();
            return string.Equals(name, Ros2MessageInternalsMetadataName, StringComparison.Ordinal)
                   || string.Equals(name, Ros2ExtendedDisposableMetadataName, StringComparison.Ordinal);
        }

        private static ITypeSymbol MemberType(ISymbol symbol)
            => symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)symbol).Type;

        private static bool CanRead(ISymbol symbol)
            => symbol is IFieldSymbol
               || ((IPropertySymbol)symbol).GetMethod?.DeclaredAccessibility == Accessibility.Public;

        private static bool CanWrite(ISymbol symbol)
            => symbol is IFieldSymbol field
                ? !field.IsReadOnly
                : ((IPropertySymbol)symbol).SetMethod?.DeclaredAccessibility == Accessibility.Public
                  && ((IPropertySymbol)symbol).SetMethod.IsInitOnly == false;

        private static bool TrySequence(
            ITypeSymbol type,
            out ITypeSymbol elementType,
            out FoxRunRos2SequenceRepresentation representation,
            out bool unsupportedRank)
        {
            elementType = null;
            representation = FoxRunRos2SequenceRepresentation.None;
            unsupportedRank = false;
            if (type is IArrayTypeSymbol array)
            {
                elementType = array.ElementType;
                representation = FoxRunRos2SequenceRepresentation.Array;
                unsupportedRank = array.Rank != 1;
                return true;
            }

            if (type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1
                && string.Equals(MetadataDefinitionName(named), "System.Collections.Generic.List", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                representation = FoxRunRos2SequenceRepresentation.List;
                return true;
            }

            return false;
        }

        private static FoxRunRos2MessageMemberKind? LeafKind(ITypeSymbol type)
        {
            if (type?.SpecialType == SpecialType.System_String)
                return FoxRunRos2MessageMemberKind.String;
            if (type?.TypeKind == TypeKind.Enum)
                return FoxRunRos2MessageMemberKind.Enum;
            return IsScalar(type?.SpecialType ?? SpecialType.None)
                ? FoxRunRos2MessageMemberKind.Scalar
                : (FoxRunRos2MessageMemberKind?)null;
        }

        private static bool IsSupportedLeaf(ITypeSymbol type) => LeafKind(type) != null;

        private static bool IsScalar(SpecialType type)
            => type >= SpecialType.System_Boolean && type <= SpecialType.System_Double
               && type != SpecialType.System_Decimal
               || type == SpecialType.System_Char;

        private static bool ImplementsContract(INamedTypeSymbol type, INamedTypeSymbol contract)
            => type != null
               && contract != null
               && type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, contract));

        private static string CanonicalRosType(INamedTypeSymbol type, string path, ICollection<string> diagnostics)
        {
            var ns = type?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            const string suffix = ".msg";
            if (!ns.EndsWith(suffix, StringComparison.Ordinal)
                || ns.Length == suffix.Length
                || ns.Substring(0, ns.Length - suffix.Length).IndexOf('.') >= 0)
            {
                diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                    "FOXRUN040", path,
                    "Native ROS2 message type must be declared directly in a <package>.msg namespace; srv/action types are unsupported."));
                return string.Empty;
            }

            return ns.Substring(0, ns.Length - suffix.Length) + "/msg/" + type.Name;
        }

        private static void AddNotWritable(string path, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode(
                "FOXRUN028", path,
                "Native ROS2 message members must be both readable and writable, except getter-only fixed arrays."));

        private static void AddUnsupported(string path, string message, ICollection<string> diagnostics)
            => diagnostics.Add(FoxRunRos2ShapeDiagnostic.Encode("FOXRUN042", path, message));

        private static string BuildIdentity(string canonical, IEnumerable<FoxRunRos2MessageMemberShape> members)
            => canonical + "|" + string.Join(";", members.Select(member =>
                member.Name + ":" + member.Kind + ":" + member.FullyQualifiedTypeName + ":"
                + member.SequenceRepresentation + ":" + member.SequenceElementTypeName + ":"
                + member.NestedShapeIdentity + ":" + member.CanRead + ":" + member.CanWrite + ":" + member.FixedSize));

        private static string MetadataDefinitionName(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            var name = type.Name;
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
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
