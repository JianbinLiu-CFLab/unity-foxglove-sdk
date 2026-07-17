// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural guard for the public Data Transport Inspector hierarchy.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Guards the public FoxgloveManager Inspector hierarchy planned for Phase 180.
    /// </summary>
    public static class DataTransportInspectorValidation
    {
        private const string ManagerEditorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";

        private static int _passed;

        /// <summary>
        /// Validates the public Data Transport grouping without changing Inspector behavior.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 180: Data Transport Inspector Structure ===");
            _passed = 0;

            var mainInspector = PhaseValidationSourceHelpers.ReadRequiredRepoText(ManagerEditorPath);
            var editorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var topLevel = FindMethod(mainInspector, "OnInspectorGUI");
            var dataTransport = FindMethod(editorSources, "DrawDataTransportSection");

            VerifyTopLevelWorkflow(topLevel);
            VerifyNestedTransportWorkflow(dataTransport);

            Console.WriteLine("Phase 180: " + _passed + " checks passed.");
        }

        private static void VerifyTopLevelWorkflow(MethodDeclarationSyntax topLevel)
        {
            var directSections = DirectInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection"))
                .ToArray();
            var executableSections = ExecutableInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection"))
                .ToArray();
            var dataTransport = directSections.Where(invocation => HasStringHeading(invocation, "Data Transport")).ToArray();
            var mcap = directSections.Where(invocation => HasStringHeading(invocation, "MCAP Record & Replay")).ToArray();
            var allDataTransport = executableSections.Where(invocation => HasStringHeading(invocation, "Data Transport")).ToArray();
            var allMcap = executableSections.Where(invocation => HasStringHeading(invocation, "MCAP Record & Replay")).ToArray();

            Check(allDataTransport.Length == 1
                  && dataTransport.Length == 1
                  && ReferenceEquals(allDataTransport[0], dataTransport[0])
                  && HasMethodGroupArgument(dataTransport[0], "DrawDataTransportSection")
                  && allMcap.Length == 1
                  && mcap.Length == 1
                  && ReferenceEquals(allMcap[0], mcap[0])
                  && HasMethodGroupArgument(mcap[0], "DrawMcapSection")
                  && Array.IndexOf(directSections, dataTransport[0]) < Array.IndexOf(directSections, mcap[0]),
                "180A-1: Manager Inspector directly wires one Data Transport workflow before sibling MCAP Record & Replay");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "Publish Data")),
                "180A-2: Publish Data is no longer a top-level workflow section");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "Subscribe Data")),
                "180A-3: Subscribe Data is no longer a top-level workflow section");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "ROS2 Runtime (R2FU)")
                                                        || HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)")),
                "180A-4: ROS 2 Native Runtime (R2FU) is no longer a top-level workflow section");
            Check(!executableSections.Any(invocation => HasStringHeading(invocation, "ROS2 Bridge")),
                "180A-5: ROS2 Bridge is no longer a top-level workflow section");
        }

        private static void VerifyNestedTransportWorkflow(MethodDeclarationSyntax dataTransport)
        {
            var subsections = DirectInvocations(dataTransport)
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection"))
                .ToArray();
            var allInvocations = AllInvocations(dataTransport);
            var nativeSubsections = allInvocations
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)"))
                .ToArray();
            var nativeDemandBranches = DirectIfStatements(dataTransport)
                .Where(HasNativeDemandCondition)
                .ToArray();
            var branchNativeSubsections = nativeDemandBranches
                .SelectMany(DirectThenStatements)
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null
                                     && IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasStringHeading(invocation, "ROS 2 Native Runtime (R2FU)"))
                .ToArray();

            Check(!ContainsStringLiteral(dataTransport, "MCAP Record & Replay")
                  && !ContainsIdentifier(dataTransport, "DrawMcapSection"),
                "180B-1: Data Transport contains no MCAP Record & Replay child workflow");
            Check(HasExactlyOneSubsection(subsections, "Publish", "DrawPublishDataSection"),
                "180B-2: Data Transport nests the public Publish workflow");
            Check(HasExactlyOneSubsection(subsections, "Subscribe", "DrawSubscribeDataSection"),
                "180B-3: Data Transport nests the public Subscribe workflow");
            Check(nativeSubsections.Length == 1
                  && HasMethodGroupArgument(nativeSubsections[0], "DrawR2fuRuntimeSection")
                  && nativeDemandBranches.Length == 1
                  && branchNativeSubsections.Length == 1
                  && ReferenceEquals(nativeSubsections[0], branchNativeSubsections[0]),
                "180B-4: Data Transport nests ROS 2 Native Runtime (R2FU) only under native demand");
        }

        private static MethodDeclarationSyntax FindMethod(string source, string methodName)
        {
            var methods = CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText == methodName && method.Body != null)
                .ToArray();
            return methods.Length == 1 ? methods[0] : null;
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : DirectInvocations(method.Body.Statements);
        }

        private static IEnumerable<InvocationExpressionSyntax> DirectInvocations(IEnumerable<StatementSyntax> statements)
        {
            foreach (var statement in statements.OfType<ExpressionStatementSyntax>())
            {
                if (statement.Expression is InvocationExpressionSyntax invocation)
                    yield return invocation;
            }
        }

        private static IEnumerable<IfStatementSyntax> DirectIfStatements(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<IfStatementSyntax>()
                : method.Body.Statements.OfType<IfStatementSyntax>();
        }

        private static IEnumerable<InvocationExpressionSyntax> ExecutableInvocations(MethodDeclarationSyntax method)
        {
            return method?.Body == null
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : method.Body.Statements.SelectMany(ExecutableInvocations);
        }

        private static IEnumerable<InvocationExpressionSyntax> ExecutableInvocations(StatementSyntax statement)
        {
            return statement is LocalFunctionStatementSyntax
                ? Enumerable.Empty<InvocationExpressionSyntax>()
                : statement.DescendantNodes(ShouldDescendIntoExecutableNode).OfType<InvocationExpressionSyntax>();
        }

        private static bool ShouldDescendIntoExecutableNode(SyntaxNode node)
        {
            return !(node is AnonymousFunctionExpressionSyntax)
                   && !(node is LocalFunctionStatementSyntax);
        }

        private static IEnumerable<StatementSyntax> DirectThenStatements(IfStatementSyntax statement)
        {
            return DirectBranchStatements(statement.Statement);
        }

        private static IEnumerable<StatementSyntax> DirectBranchStatements(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements;

            return new[] { statement };
        }

        private static InvocationExpressionSyntax[] AllInvocations(MethodDeclarationSyntax method)
        {
            return method == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        }

        private static bool HasExactlyOneSubsection(
            IEnumerable<InvocationExpressionSyntax> subsections,
            string heading,
            string callback)
        {
            var matches = subsections.Where(invocation => HasStringHeading(invocation, heading)).ToArray();
            return matches.Length == 1 && HasMethodGroupArgument(matches[0], callback);
        }

        private static bool HasNativeDemandCondition(IfStatementSyntax statement)
        {
            return statement.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "HasR2fuNativeRuntimeDemand")
                   && invocation.ArgumentList.Arguments.Count == 0;
        }

        private static bool IsInvocationNamed(InvocationExpressionSyntax invocation, string name)
        {
            if (invocation.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == name;

            return invocation.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == name;
        }

        private static bool HasStringHeading(InvocationExpressionSyntax invocation, string heading)
        {
            return invocation.ArgumentList.Arguments.Count > 0
                   && invocation.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
                   && literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                   && literal.Token.ValueText == heading;
        }

        private static bool HasMethodGroupArgument(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier
                && identifier.Identifier.ValueText == methodName);
        }

        private static bool ContainsStringLiteral(MethodDeclarationSyntax method, string value)
        {
            return method != null
                   && method.DescendantNodes().OfType<LiteralExpressionSyntax>().Any(literal =>
                       literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                       && literal.Token.ValueText == value);
        }

        private static bool ContainsIdentifier(MethodDeclarationSyntax method, string identifier)
        {
            return method != null
                   && method.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name =>
                       name.Identifier.ValueText == identifier);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
