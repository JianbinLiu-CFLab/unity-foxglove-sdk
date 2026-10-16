// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the Provider-neutral Data Transport Inspector.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class DataTransportInspectorValidation
    {
        private static string ExpandGuardContext(
            MethodDeclarationSyntax method,
            ExpressionSyntax condition)
        {
            var declarations = method.DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable =>
                    variable.Initializer != null)
                .GroupBy(variable =>
                    variable.Identifier.ValueText,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
            var pending = new Queue<string>(
                condition.DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
                    .Select(identifier =>
                        identifier.Identifier.ValueText));
            var visited =
                new HashSet<string>(StringComparer.Ordinal);
            var fragments =
                new List<string>
                {
                    condition.ToFullString()
                };

            while (pending.Count > 0)
            {
                var name = pending.Dequeue();
                if (!visited.Add(name)
                    || !declarations.TryGetValue(
                        name,
                        out var declaration))
                {
                    continue;
                }

                var initializer =
                    declaration.Initializer.Value;
                fragments.Add(initializer.ToFullString());
                foreach (var identifier in
                         initializer.DescendantNodesAndSelf()
                             .OfType<IdentifierNameSyntax>())
                {
                    pending.Enqueue(
                        identifier.Identifier.ValueText);
                }
            }

            return string.Join("\n", fragments);
        }
        private static SyntaxNode Parse(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var errors = tree.GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.Severity
                    == DiagnosticSeverity.Error)
                .ToArray();
            if (errors.Length != 0)
            {
                throw new InvalidOperationException(
                    "Source contains syntax errors: "
                    + errors[0]);
            }

            return tree.GetRoot();
        }
        private static MethodDeclarationSyntax FindMethod(
            string source,
            string methodName)
        {
            var methods = Parse(source)
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    method.Identifier.ValueText == methodName)
                .ToArray();
            if (methods.Length != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one method named "
                    + methodName
                    + ", found "
                    + methods.Length
                    + ".");
            }

            return methods[0];
        }
        private static IEnumerable<InvocationExpressionSyntax>
            InvocationsNamed(
                SyntaxNode node,
                string methodName)
            => node.DescendantNodesAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation =>
                    IsInvocationNamed(
                        invocation,
                        methodName));
        private static bool IsInvocationNamed(
            InvocationExpressionSyntax invocation,
            string methodName)
        {
            if (invocation.Expression
                is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.ValueText
                       == methodName;
            }

            return invocation.Expression
                       is MemberAccessExpressionSyntax access
                   && access.Name.Identifier.ValueText
                   == methodName;
        }
        private static string StringArgument(
            InvocationExpressionSyntax invocation,
            int index)
        {
            if (invocation.ArgumentList.Arguments.Count <= index
                || invocation.ArgumentList.Arguments[index]
                       .Expression
                   is not LiteralExpressionSyntax literal
                || !literal.IsKind(
                    SyntaxKind.StringLiteralExpression))
            {
                return string.Empty;
            }

            return literal.Token.ValueText;
        }
        private static ExpressionSyntax UnwrapParentheses(
            ExpressionSyntax expression)
        {
            while (expression
                   is ParenthesizedExpressionSyntax parentheses)
            {
                expression = parentheses.Expression;
            }

            return expression;
        }
        private static bool HasStringArgument(
            InvocationExpressionSyntax invocation,
            string expected)
            => invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<LiteralExpressionSyntax>()
                .Any(literal =>
                    literal.IsKind(
                        SyntaxKind.StringLiteralExpression)
                    && string.Equals(
                        literal.Token.ValueText,
                        expected,
                        StringComparison.Ordinal));
        private static bool HasIdentifierArgument(
            InvocationExpressionSyntax invocation,
            string expected)
            => invocation.ArgumentList.Arguments
                .Select(argument => argument.Expression)
                .OfType<IdentifierNameSyntax>()
                .Any(identifier =>
                    identifier.Identifier.ValueText
                    == expected);
        private static bool References(
            MethodDeclarationSyntax method,
            string identifier)
            => method.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Any(name =>
                    name.Identifier.ValueText
                    == identifier);
        private static int Count(
            string source,
            string token)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(
                       token,
                       index,
                       StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }
    }
}
