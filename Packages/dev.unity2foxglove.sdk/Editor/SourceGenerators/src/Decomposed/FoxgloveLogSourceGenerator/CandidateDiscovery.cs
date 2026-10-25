// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/SourceGenerators
// Purpose: Provider-neutral FoxRun and FoxService source generation.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.SourceGenerators
{
    public sealed partial class FoxgloveLogSourceGenerator : IIncrementalGenerator
    {
        private static bool IsFoxRunCandidate(SyntaxNode node)
        {
            if (node is FieldDeclarationSyntax field
                && field.AttributeLists.Count > 0)
            {
                // Attribute aliases are resolved only by the semantic model;
                // keep every attributed declaration as a candidate and let
                // ExtractMember perform the canonical metadata-name check.
                return true;
            }

            return node is PropertyDeclarationSyntax property
                   && property.AttributeLists.Count > 0;
        }

        private static bool IsServiceCandidate(SyntaxNode node)
            => node is MethodDeclarationSyntax method
               && method.AttributeLists.Count > 0;


        private static bool TryGetUnsupportedHostIdentity(
            INamedTypeSymbol containingType,
            out string reason)
        {
            reason = string.Empty;
            if (containingType == null)
            {
                reason = "missing declaring type";
                return true;
            }

            // The neutral model intentionally represents one top-level host as
            // namespace + simple class. Reject shapes that would otherwise be
            // flattened into a different partial declaration.
            if (containingType.ContainingType != null)
            {
                reason = "nested declaring types are not supported";
                return true;
            }

            if (containingType.IsGenericType || containingType.Arity > 0)
            {
                reason = "generic declaring types are not supported";
                return true;
            }

            if (containingType.IsRecord)
            {
                reason = "record declaring types are not supported";
                return true;
            }

            if (containingType.TypeKind != TypeKind.Class)
            {
                reason = containingType.TypeKind == TypeKind.Struct
                    ? "struct declaring types are not supported"
                    : containingType.TypeKind == TypeKind.Interface
                        ? "interface declaring types are not supported"
                        : "non-class declaring types are not supported";
                return true;
            }

            if (containingType.IsStatic)
            {
                reason = "static declaring classes are not supported";
                return true;
            }

            // Roslyn 4.2 does not expose a file-local accessibility enum or
            // SyntaxKind.FileKeyword. Match the modifier token text so this
            // guard remains compatible with newer compilers without rejecting
            // contextual identifiers used as ordinary type/namespace names.
            if (HasFileLocalModifier(containingType))
            {
                reason = "file-local declaring classes are not supported";
                return true;
            }

            // Keyword spellings are escaped by the shared source emitters;
            // they do not make the semantic host identity lossy.

            return false;
        }

        private static bool HasFileLocalModifier(
            INamedTypeSymbol containingType)
        {
            foreach (var reference in containingType.DeclaringSyntaxReferences)
            {
                if (!(reference.GetSyntax() is TypeDeclarationSyntax declaration))
                    continue;
                if (declaration.Modifiers.Any(modifier =>
                        string.Equals(
                            modifier.ValueText,
                            "file",
                            StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
