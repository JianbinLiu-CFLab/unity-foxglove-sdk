// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    internal static partial class PhaseValidationSourceHelpers
    {
        public static bool SourceMethodContains(string source, string methodName, string needle)
            => RequiredSourceMethod(source, methodName).Contains(needle, StringComparison.Ordinal);

        public static int InvocationCountInMethod(
            string source,
            string methodName,
            string invocationName)
            => QualifiedInvocationCountInMethod(
                source,
                methodName,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCountInMethod(
            string source,
            string methodName,
            string receiverName,
            string invocationName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method =>
                    string.Equals(
                        method.Identifier.ValueText,
                        methodName,
                        StringComparison.Ordinal))
                .ToArray();
            if (methods.Length != 1)
                return -1;

            return methods[0]
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                // Calls inside local-function declarations belong to that
                // nested owner, not the containing method being inspected.
                .Where(invocation => !invocation.Ancestors()
                    .Any(ancestor => ancestor is LocalFunctionStatementSyntax))
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));
        }

        public static int InvocationCount(
            string source,
            string invocationName)
            => QualifiedInvocationCount(
                source,
                receiverName: null,
                invocationName);

        public static int QualifiedInvocationCount(
            string source,
            string receiverName,
            string invocationName)
            => CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(invocation =>
                    InvocationMatches(
                        invocation,
                        receiverName,
                        invocationName));

        public static bool TypeHasAttribute(
            string source,
            string typeName,
            string attributeName)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(typeName))
                throw new ArgumentException(
                    "Type name cannot be empty.",
                    nameof(typeName));
            if (string.IsNullOrWhiteSpace(attributeName))
                throw new ArgumentException(
                    "Attribute name cannot be empty.",
                    nameof(attributeName));

            var shortName = attributeName.EndsWith(
                "Attribute",
                StringComparison.Ordinal)
                ? attributeName.Substring(
                    0,
                    attributeName.Length - "Attribute".Length)
                : attributeName;
            var fullName = shortName + "Attribute";
            var matches = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(type =>
                    string.Equals(
                        type.Identifier.ValueText,
                        typeName,
                        StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                return false;

            return matches[0].AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attribute =>
                {
                    var identifier = attribute.Name
                        .DescendantNodesAndSelf()
                        .OfType<IdentifierNameSyntax>()
                        .LastOrDefault()
                        ?.Identifier.ValueText;
                    return string.Equals(
                               identifier,
                               shortName,
                               StringComparison.Ordinal)
                           || string.Equals(
                               identifier,
                               fullName,
                               StringComparison.Ordinal);
                });
        }

        public static string SourceMethodContaining(
            string source,
            string methodName,
            string bodyMarker)
        {
            if (string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(methodName)
                || string.IsNullOrWhiteSpace(bodyMarker))
            {
                throw new ArgumentException(
                    "Source, method name, and body marker are required.");
            }

            var matches = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => string.Equals(
                    method.Identifier.ValueText,
                    methodName,
                    StringComparison.Ordinal))
                // Keep a method span even when a formatter leaves a recoverable
                // diagnostic in a tuple header; the body marker is the gate.
                .Select(method => source.Substring(method.SpanStart, method.Span.Length))
                .Where(method => method.Contains(bodyMarker, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "Could not resolve exactly one source method containing '"
                    + bodyMarker
                    + "' for: "
                    + methodName
                    + "; matches="
                    + matches.Length);
            }

            return matches[0];
        }

        public static string RequiredSourceMethod(string source, string methodName)
            => SourceMethod(source, methodName);

        public static string SourceMethodWithPreprocessorSymbols(
            string source,
            string methodName,
            params string[] preprocessorSymbols)
        {
            if (preprocessorSymbols == null || preprocessorSymbols.Length == 0)
                return string.Empty;

            return SourceDeclaration(
                source,
                methodName,
                IsSourceMethodDeclaration,
                CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols));
        }

        public static string SourceType(string source, string typeName)
            => SourceDeclaration(
                source,
                typeName,
                node => node is TypeDeclarationSyntax,
                CSharpParseOptions.Default);

        public static string SourceProperty(string source, string propertyName)
            => SourceDeclaration(
                source,
                propertyName,
                node => node is PropertyDeclarationSyntax,
                CSharpParseOptions.Default);
        private static bool IsSourceMethodDeclaration(SyntaxNode node)
            => node is MethodDeclarationSyntax
               || node is ConstructorDeclarationSyntax
               || node is LocalFunctionStatementSyntax;

        private static bool SourceDeclarationMatches(
            string source,
            SyntaxNode declaration,
            string requestedDeclaration)
        {
            var identifier = declaration switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.ValueText,
                TypeDeclarationSyntax type => type.Identifier.ValueText,
                PropertyDeclarationSyntax property => property.Identifier.ValueText,
                _ => string.Empty
            };

            if (SyntaxFacts.IsValidIdentifier(requestedDeclaration))
                return string.Equals(identifier, requestedDeclaration, StringComparison.Ordinal);
            if (!ContainsIdentifierToken(requestedDeclaration, identifier))
                return false;

            var headerEnd = declaration switch
            {
                MethodDeclarationSyntax method => SourceMethodHeaderEnd(
                    method.Body?.OpenBraceToken.SpanStart,
                    method.ExpressionBody?.ArrowToken.SpanStart,
                    method.SemicolonToken.SpanStart),
                ConstructorDeclarationSyntax constructor => SourceMethodHeaderEnd(
                    constructor.Body?.OpenBraceToken.SpanStart,
                    constructor.ExpressionBody?.ArrowToken.SpanStart,
                    constructor.SemicolonToken.SpanStart),
                LocalFunctionStatementSyntax localFunction => SourceMethodHeaderEnd(
                    localFunction.Body?.OpenBraceToken.SpanStart,
                    localFunction.ExpressionBody?.ArrowToken.SpanStart,
                    localFunction.SemicolonToken.SpanStart),
                TypeDeclarationSyntax type => type.OpenBraceToken.SpanStart,
                PropertyDeclarationSyntax property => SourceMethodHeaderEnd(
                    property.AccessorList?.OpenBraceToken.SpanStart,
                    property.ExpressionBody?.ArrowToken.SpanStart,
                    property.SemicolonToken.SpanStart),
                _ => -1
            };
            if (headerEnd < declaration.SpanStart)
                return false;

            var header = source.Substring(declaration.SpanStart, headerEnd - declaration.SpanStart);
            return CollapseSourceWhitespace(header)
                .Contains(CollapseSourceWhitespace(requestedDeclaration), StringComparison.Ordinal);
        }

        private static int SourceMethodHeaderEnd(int? bodyStart, int? expressionBodyStart, int semicolonStart)
            => bodyStart ?? expressionBodyStart ?? semicolonStart;

        private static string CollapseSourceWhitespace(string value)
        {
            var result = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var current in value)
            {
                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }

                if (pendingSpace)
                    result.Append(' ');
                result.Append(current);
                pendingSpace = false;
            }

            return result.ToString();
        }

        private static bool ContainsIdentifierToken(string value, string identifier)
        {
            var offset = 0;
            while (offset < value.Length)
            {
                var index = value.IndexOf(identifier, offset, StringComparison.Ordinal);
                if (index < 0)
                    return false;

                var beforeIsIdentifier = index > 0
                                         && SyntaxFacts.IsIdentifierPartCharacter(value[index - 1]);
                var afterIndex = index + identifier.Length;
                var afterIsIdentifier = afterIndex < value.Length
                                        && SyntaxFacts.IsIdentifierPartCharacter(value[afterIndex]);
                if (!beforeIsIdentifier && !afterIsIdentifier)
                    return true;

                offset = index + identifier.Length;
            }

            return false;
        }

        private static bool InvocationMatches(
            InvocationExpressionSyntax invocation,
            string receiverName,
            string invocationName)
        {
            if (invocation?.Expression
                is IdentifierNameSyntax identifier)
            {
                return receiverName == null
                       && identifier.Identifier.ValueText
                       == invocationName;
            }

            if (invocation?.Expression
                is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            return memberAccess.Name.Identifier.ValueText
                   == invocationName
                   && (receiverName == null
                       || string.Equals(
                           memberAccess.Expression.ToString(),
                           receiverName,
                           StringComparison.Ordinal));
        }

    }
}
