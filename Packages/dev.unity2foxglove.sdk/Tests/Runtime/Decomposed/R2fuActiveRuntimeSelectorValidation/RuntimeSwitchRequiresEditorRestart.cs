// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 146A validation for the project-level R2FU active runtime selector.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class R2fuActiveRuntimeSelectorValidation
    {
        private static void RuntimeSwitchRequiresEditorRestart()
        {
            var guard = ReadRepoText(PlayModeGuardPath);
            var inspector = ReadRepoText(InspectorPath);

            Check(guard.Contains("EditorApplication.playModeStateChanged", StringComparison.Ordinal)
                  && guard.Contains("PlayModeStateChange.ExitingEditMode", StringComparison.Ordinal)
                  && guard.Contains("BindActiveRuntimeForPlayMode", StringComparison.Ordinal)
                  && guard.Contains("GetRuntimePackageRequiringEditorRestart", StringComparison.Ordinal),
                "146A-F1: Play Mode binds the first runtime used by this Editor session");
            Check(guard.Contains("EditorApplication.isPlaying = false", StringComparison.Ordinal)
                  && guard.Contains("Restart Unity before entering Play Mode", StringComparison.Ordinal),
                "146A-F2: Play Mode guard cancels unsafe mixed-runtime entry and explains the restart requirement");
            Check(guard.Contains("HasR2fuNativeDemand()", StringComparison.Ordinal)
                  && guard.Contains("status.SelectedRuntime == null", StringComparison.Ordinal)
                  && guard.Contains("No selected ROS2 For Unity runtime is available", StringComparison.Ordinal)
                  && guard.Contains("EditorApplication.isPlaying = false", StringComparison.Ordinal),
                "146A-F2b: native demand fails closed before Play Mode when no valid runtime selection can bind RMW");
            Check(guard.Contains("CompilationPipeline.compilationStarted", StringComparison.Ordinal)
                  && guard.Contains("AssemblyReloadEvents.beforeAssemblyReload", StringComparison.Ordinal)
                  && guard.Contains("CompilationStartedWhileR2fuPlayModeKey", StringComparison.Ordinal)
                  && guard.Contains("native ROS2/RMW DLLs cannot be safely unloaded during Play Mode", StringComparison.Ordinal),
                "146A-F3: Play Mode guard exits for script-compilation reloads without blocking normal Play Mode domain reload");
            Check(inspector.Contains("GetRuntimePackageRequiringEditorRestart", StringComparison.Ordinal)
                  && inspector.Contains("Restart Unity", StringComparison.Ordinal)
                  && inspector.Contains("RestartEditor(projectDirectory)", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("RestartEditorInCleanProcess", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("ProcessStartInfo", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("UseShellExecute = false", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("BuildCleanRestartPath", StringComparison.Ordinal)
                  && ReadRepoText(SelectionPath).Contains("EditorApplication.Exit(0)", StringComparison.Ordinal)
                  && !ReadRepoText(SelectionPath).Contains("EditorApplication.OpenProject(projectDirectory)", StringComparison.Ordinal),
                "146A-F4: Inspector restart launches a clean child Editor without inheriting an older R2FU native plugin path");
        }

        private static void ReadmeDocumentsActiveRuntimeSelection()
        {
            var source = ReadRepoText(ReadmePath);

            Check(source.Contains("candidate runtime packages", StringComparison.Ordinal)
                  && source.Contains("exactly one active runtime", StringComparison.Ordinal),
                "146A-G1: README documents candidate runtimes versus the active manifest runtime");
            Check(source.Contains("manifest.json", StringComparison.Ordinal)
                  && source.Contains("ROS2 For Unity Runtime", StringComparison.Ordinal)
                  && source.Contains("package reimport", StringComparison.Ordinal)
                  && source.Contains("After an Editor session has loaded one ROS2 runtime", StringComparison.Ordinal),
                "146A-G2: README documents manifest switching and conditional restart requirement");
        }

        private static void ValidationRegistryWiresPhase146A()
        {
            var registry = ReadRepoText(RegistryPath);
            var project = ReadRepoText(ProjectPath);

            Check(registry.Contains("Ci(\"--phase146a\", \"Phase 146A: validation for the project-level R2FU active runtime selector\", R2fuActiveRuntimeSelectorValidation.Validate", StringComparison.Ordinal),
                "146A-H1: validation registry wires --phase146a to the runtime selector validation");
            Check(project.Contains("R2fuActiveRuntimeSelectorValidation.cs", StringComparison.Ordinal),
                "146A-H2: runtime validation project compiles the runtime selector validation");
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            Check(File.Exists(path), $"146A-file: {relativePath} exists");
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(Phase16Validation.FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
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

        private static InvocationExpressionSyntax[] FindInvocations(string source)
        {
            return CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .ToArray();
        }

        private static InvocationExpressionSyntax[] FindInvocations(MethodDeclarationSyntax method)
        {
            return method == null
                ? Array.Empty<InvocationExpressionSyntax>()
                : method.DescendantNodes().OfType<InvocationExpressionSyntax>().ToArray();
        }

        private static bool IsNativeRuntimeSubsection(InvocationExpressionSyntax invocation)
        {
            return IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                   && HasStringArgument(invocation, 0, "ROS 2 Native Runtime (R2FU) — Shared")
                   && HasStringArgument(invocation, 1, "DataTransportNativeRuntime")
                   && HasRefIdentifierArgument(invocation, 2, "_dataTransportNativeRuntimeExpanded")
                   && HasMethodGroupArgument(invocation, "DrawR2fuRuntimeSection");
        }

        private static bool HasNativeDemandCondition(IfStatementSyntax statement)
        {
            return statement?.Condition is InvocationExpressionSyntax invocation
                   && IsInvocationNamed(invocation, "HasR2fuNativeRuntimeDemand")
                   && invocation.ArgumentList.Arguments.Count == 0;
        }

        private static bool IsInvocationNamed(InvocationExpressionSyntax invocation, string methodName)
        {
            if (invocation?.Expression is IdentifierNameSyntax identifier)
                return identifier.Identifier.ValueText == methodName;

            return invocation?.Expression is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name.Identifier.ValueText == methodName;
        }

        private static bool HasStringArgument(InvocationExpressionSyntax invocation, int argumentIndex, string value)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is LiteralExpressionSyntax literal
                   && literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                   && literal.Token.ValueText == value;
        }

        private static bool HasRefIdentifierArgument(
            InvocationExpressionSyntax invocation,
            int argumentIndex,
            string identifier)
        {
            return invocation != null
                   && invocation.ArgumentList.Arguments.Count > argumentIndex
                   && invocation.ArgumentList.Arguments[argumentIndex].RefKindKeyword.RawKind == (int)SyntaxKind.RefKeyword
                   && invocation.ArgumentList.Arguments[argumentIndex].Expression is IdentifierNameSyntax argument
                   && argument.Identifier.ValueText == identifier;
        }

        private static bool HasMethodGroupArgument(InvocationExpressionSyntax invocation, string methodName)
        {
            return invocation != null && invocation.ArgumentList.Arguments.Any(argument =>
                argument.Expression is IdentifierNameSyntax identifier
                    && identifier.Identifier.ValueText == methodName);
        }

        private static System.Collections.Generic.IEnumerable<StatementSyntax> DirectThenStatements(IfStatementSyntax statement)
        {
            if (statement?.Statement is BlockSyntax block)
                return block.Statements;

            return statement == null
                ? Array.Empty<StatementSyntax>()
                : new[] { statement.Statement };
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[FAIL] " + message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }
    }
}
