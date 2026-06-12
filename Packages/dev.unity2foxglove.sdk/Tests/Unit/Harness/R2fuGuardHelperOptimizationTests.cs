// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-83 runtime harness helper source-shape checks.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-83")]
    [Trait("Domain", "Harness")]
    public sealed class R2fuGuardHelperOptimizationTests
    {
        [Fact]
        public void AllR2fuReferencesAreGuardedScansTokensWithoutPerCallArrayCopy()
        {
            var method = AllR2fuReferencesAreGuardedMethod();

            Assert.Contains(
                method.ParameterList.Parameters,
                parameter => parameter.Identifier.ValueText == "tokens"
                             && parameter.Type?.ToString() == "IReadOnlyList<string>");

            Assert.Contains(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => invocation.Expression.ToString() == "FindToken"
                              && invocation.ArgumentList.Arguments.Count == 2
                              && invocation.ArgumentList.Arguments[1].Expression.ToString() == "tokens");

            Assert.DoesNotContain(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => invocation.Expression.ToString().EndsWith(".ToArray", StringComparison.Ordinal));

            Assert.DoesNotContain(
                method.DescendantNodes().OfType<VariableDeclaratorSyntax>(),
                variable => variable.Identifier.ValueText == "tokenList");
        }

        [Fact]
        public void AllR2fuReferencesAreGuardedUsesTopFrameGuardState()
        {
            var method = AllR2fuReferencesAreGuardedMethod();

            Assert.Contains(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => invocation.Expression.ToString() == "CurrentGuarded"
                              && invocation.ArgumentList.Arguments.Count == 1
                              && invocation.ArgumentList.Arguments[0].Expression.ToString() == "stack");

            Assert.DoesNotContain(
                method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                invocation => invocation.Expression.ToString().EndsWith(".Any", StringComparison.Ordinal)
                              && invocation.ArgumentList.ToString().Contains("CurrentGuarded", StringComparison.Ordinal));
        }

        [Fact]
        public void Phase14083RemainsRegisteredInConsoleRunner()
        {
            var registry = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Assert.Contains("\"--phase140-83\"", registry, StringComparison.Ordinal);
            Assert.Contains("Phase140_83Validation.Validate", registry, StringComparison.Ordinal);

            var project = Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Assert.Contains("Phase140_83Validation.cs", project, StringComparison.Ordinal);
        }

        private static MethodDeclarationSyntax AllR2fuReferencesAreGuardedMethod()
        {
            return RuntimeSyntax("PhaseRos2ForUnityValidationHelpers.cs")
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(method => method.Identifier.ValueText == "AllR2fuReferencesAreGuarded");
        }

        private static SyntaxTree RuntimeSyntax(string fileName)
            => CSharpSyntaxTree.ParseText(Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName));

        private static string Text(string relativePath)
            => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                        || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                        return dir.FullName;

                    dir = dir.Parent;
                }

                throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
            }
        }
    }
}
