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
        private static string
            ReadStringConstructorArgument(
                AttributeData attribute)
        {
            if (attribute == null
                || attribute.ConstructorArguments
                    .Length == 0)
            {
                return string.Empty;
            }

            return attribute.ConstructorArguments[0]
                       .Value as string
                   ?? string.Empty;
        }

        private static string DeclaringTypeName(
            INamedTypeSymbol type)
        {
            if (type == null)
                return string.Empty;
            var names = new Stack<string>();
            for (var current = type;
                 current != null;
                 current = current.ContainingType)
            {
                names.Push(current.Name);
            }

            var prefix =
                type.ContainingNamespace != null
                && !type.ContainingNamespace
                    .IsGlobalNamespace
                    ? type.ContainingNamespace
                          .ToDisplayString()
                      + "."
                    : string.Empty;
            return prefix + string.Join(
                ".",
                names);
        }

        private static bool TryReadFloatConstant(
            TypedConstant constant,
            out float value)
        {
            value = 0f;
            if (constant.Value == null)
                return false;
            try
            {
                value = Convert.ToSingle(
                    constant.Value);
                return true;
            }
            catch (Exception exception)
                when (exception is OverflowException
                      || exception
                          is InvalidCastException
                      || exception
                          is FormatException)
            {
                return false;
            }
        }

        private static bool TryReadIntConstant(
            TypedConstant constant,
            out int value)
        {
            value = 0;
            if (constant.Value == null)
                return false;
            try
            {
                value = Convert.ToInt32(
                    constant.Value);
                return true;
            }
            catch (Exception exception)
                when (exception is OverflowException
                      || exception
                          is InvalidCastException
                      || exception
                          is FormatException)
            {
                return false;
            }
        }

        private static string[] ReadStringArrayConstant(
            TypedConstant constant)
        {
            if (constant.IsNull)
                return null;
            if (constant.Kind
                != TypedConstantKind.Array)
            {
                return Array.Empty<string>();
            }

            var result =
                new string[constant.Values.Length];
            for (var index = 0;
                 index < result.Length;
                 index++)
            {
                result[index] =
                    constant.Values[index].Value
                        as string;
            }

            return result;
        }

        private static string TypeNameForEmission(
            ITypeSymbol type)
            => type == null
                ? string.Empty
                : type.ToDisplayString(
                    SymbolDisplayFormat
                        .FullyQualifiedFormat);

        private static bool TryGetArrayElementType(
            ITypeSymbol type,
            out ITypeSymbol elementType)
        {
            if (type is IArrayTypeSymbol array
                && array.Rank == 1)
            {
                elementType = array.ElementType;
                return true;
            }

            if (type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1)
            {
                var definition =
                    named.ConstructedFrom
                        .ToDisplayString();
                if (definition
                        == "System.Collections.Generic.List<T>"
                    || definition
                        == "System.Collections.Generic.IReadOnlyList<T>"
                    || definition
                        == "System.Collections.Generic.IList<T>")
                {
                    elementType =
                        named.TypeArguments[0];
                    return true;
                }
            }

            elementType = null;
            return false;
        }
    }
}
