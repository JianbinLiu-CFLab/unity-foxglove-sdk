// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 96 validation for ROS2 Bridge topic profiles and QoS metadata.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2Bridge;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase96Validation.
    /// </summary>
    public static partial class Phase96Validation
    {
        private static IEnumerable<InvocationExpressionSyntax> DirectThenInvocations(IfStatementSyntax statement)
        {
            return DirectBranchStatements(statement?.Statement)
                .OfType<ExpressionStatementSyntax>()
                .Select(branchStatement => branchStatement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null);
        }

        private static IEnumerable<StatementSyntax> DirectBranchStatements(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
                return block.Statements;

            return statement == null
                ? Enumerable.Empty<StatementSyntax>()
                : new[] { statement };
        }

        private static InvocationExpressionSyntax[] AllInvocations(MethodDeclarationSyntax method)
        {
            return method == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        }

        private static bool HasGetBoolCondition(IfStatementSyntax statement, string propertyName)
        {
            return statement?.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "GetBool")
                   && HasStringArgument(invocation, 0, propertyName);
        }

        private static bool IsRos2BridgeOutputSubsection(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                   && HasStringArgument(invocation, 0, "ROS 2 Bridge Output")
                   && HasStringArgument(invocation, 1, "DataTransportRos2Bridge")
                   && HasRefIdentifierArgument(invocation, 2, "_dataTransportRos2BridgeExpanded")
                   && HasMethodGroupArgument(invocation, 3, "DrawRos2BridgeSection");
        }

        private static bool IsPublishDataTransportSubsection(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                   && HasStringArgument(invocation, 0, "Publish Data")
                   && HasStringArgument(invocation, 1, "DataTransportPublish")
                   && HasRefIdentifierArgument(invocation, 2, "_dataTransportPublishExpanded")
                   && HasMethodGroupArgument(invocation, 3, "DrawPublishDataSection");
        }

        private static bool HasStringArgument(InvocationExpressionSyntax invocation, int argumentIndex, string value)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is LiteralExpressionSyntax literal
                   && literal.IsKind(SyntaxKind.StringLiteralExpression)
                   && literal.Token.ValueText == value;
        }

        private static bool HasRefIdentifierArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static bool HasMethodGroupArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string methodName)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && IsMethodGroupNamed(invocation.ArgumentList.Arguments[argumentIndex].Expression, methodName);
        }

        private static bool HasMethodGroupArgument(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Any(argument =>
                       IsMethodGroupNamed(argument.Expression, methodName));
        }

        private static bool IsMethodGroupNamed(ExpressionSyntax expression, string methodName)
        {
            if (expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == methodName;

            return expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == methodName;
        }

        private static bool IsInvocationNamed(InvocationExpressionSyntax invocation, string name)
        {
            if (invocation?.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == name;

            return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == name;
        }

        private static string Resolve(string bridgeNamespace, string publisherTopic, string overrideTopic)
        {
            if (!Ros2BridgeTopicProfile.TryResolveRos2BridgeTopic(
                bridgeNamespace,
                publisherTopic,
                overrideTopic,
                out var effective,
                out var error))
            {
                throw new Exception(error);
            }

            return effective;
        }

        private static JObject ReadHeader(byte[] bytes)
        {
            var headerLength = ReadUInt32(bytes, 8);
            var headerJson = Encoding.UTF8.GetString(bytes, 16, checked((int)headerLength));
            return JObject.Parse(headerJson);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
            => (uint)(bytes[offset]
                      | (bytes[offset + 1] << 8)
                      | (bytes[offset + 2] << 16)
                      | (bytes[offset + 3] << 24));

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }
    }
}
