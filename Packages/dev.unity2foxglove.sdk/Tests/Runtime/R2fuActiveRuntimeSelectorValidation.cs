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
    public static class R2fuActiveRuntimeSelectorValidation
    {
        private const string SelectionPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs";
        private const string InstallerPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs";
        private const string InspectorPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelectorInspector.cs";
        private const string PlayModeGuardPath =
            "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimePlayModeGuard.cs";
        private const string ReadmePath =
            "Packages/dev.unity2foxglove.ros2forunity/README.md";
        private const string ManagerInspectorPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.R2fuRuntime.cs";
        private const string ManagerInspectorAsmdefPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Unity.FoxgloveSDK.Editor.asmdef";
        private const string ManagerWorkflowPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.cs";
        private const string ManagerDataTransportPath =
            "Packages/dev.unity2foxglove.sdk/Editor/Manager/FoxgloveManagerEditor.DataTransport.cs";
        private const string RegistryPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs";
        private const string ProjectPath =
            "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 146A: R2FU Active Runtime Selector ===");
            _passed = 0;

            RuntimeSelectionDiscoversCandidatePackages();
            RuntimeSelectionUsesManifestAsTruth();
            DefineInstallerUsesOnlyBaseRuntimeSymbol();
            ManagerInspectorHostsOptionalSelector();
            RuntimeSelectorUsesOneDropdown();
            RuntimeSwitchRequiresEditorRestart();
            ReadmeDocumentsActiveRuntimeSelection();
            ValidationRegistryWiresPhase146A();

            Console.WriteLine($"Phase 146A: {_passed} checks passed.");
        }

        private static void RuntimeSelectionDiscoversCandidatePackages()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("RuntimePackagePrefix", StringComparison.Ordinal)
                  && source.Contains("DiscoverCandidateRuntimes", StringComparison.Ordinal),
                "146A-A1: runtime selector discovers runtime packages by package-id convention");
            Check(source.Contains("RepositoryPackagesDirectory", StringComparison.Ordinal)
                  && source.Contains("BuildRuntimePackageReference", StringComparison.Ordinal)
                  && source.Contains("GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)", StringComparison.Ordinal),
                "146A-A2: runtime selector derives manifest file references from the repository Packages directory");
            Check(source.Contains("SplitPathParts", StringComparison.Ordinal)
                  && !source.Contains("Uri.UnescapeDataString", StringComparison.Ordinal),
                "146A-A2b: runtime selector preserves literal percent characters when deriving relative package paths");
            Check(!source.Contains("KnownRuntimes", StringComparison.Ordinal)
                  && !source.Contains("KnownRuntimeDescriptors", StringComparison.Ordinal),
                "146A-A3: runtime selector no longer hardcodes known runtime descriptors");
            Check(!source.Contains("JazzyWin64CompileSymbol", StringComparison.Ordinal)
                  && !source.Contains("LyricalWin64CompileSymbol", StringComparison.Ordinal),
                "146A-A4: runtime selector no longer carries per-distro compile gates");
        }

        private static void RuntimeSelectionUsesManifestAsTruth()
        {
            var source = ReadRepoText(SelectionPath);

            Check(source.Contains("ReadManifestRuntimePackages", StringComparison.Ordinal)
                  && source.Contains("ActiveRuntimePackage", StringComparison.Ordinal),
                "146A-B1: active runtime selection is derived from the Unity package manifest");
            Check(source.Contains("SwitchActiveRuntimePackage", StringComparison.Ordinal)
                  && source.Contains("Client.Resolve()", StringComparison.Ordinal),
                "146A-B2: runtime changes atomically rewrite manifest then ask Unity to resolve packages");
            Check(!source.Contains("Unity2FoxgloveRos2ForUnitySettings.json", StringComparison.Ordinal)
                  && !source.Contains("SaveActiveRuntimePackage", StringComparison.Ordinal),
                "146A-B3: selector no longer treats ProjectSettings JSON as source of truth");
            Check(source.Contains("RemoveRuntimePackageDependencies", StringComparison.Ordinal)
                  && source.Contains("AddRuntimePackageDependency", StringComparison.Ordinal),
                "146A-B4: manifest switching reaches the final single-runtime dependency state in one write");
            Check(source.Contains("SessionRuntimeKey", StringComparison.Ordinal)
                  && source.Contains("SessionState", StringComparison.Ordinal)
                  && !source.Contains("EditorPrefs", StringComparison.Ordinal),
                "146A-B5: runtime guard records per-Editor-session runtime state without persistent drift");
        }

        private static void DefineInstallerUsesOnlyBaseRuntimeSymbol()
        {
            var source = ReadRepoText(InstallerPath);

            Check(source.Contains("Ros2ForUnityRuntimeSelection.GetStatus()", StringComparison.Ordinal),
                "146A-C1: define installer reads the manifest-derived runtime selection status");
            Check(source.Contains("Ros2ForUnityRuntimeSelection.BaseCompileSymbol", StringComparison.Ordinal)
                  && source.Contains("EnsureSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", StringComparison.Ordinal),
                "146A-C2: define installer enables only the base optional R2FU symbol");
            Check(source.Contains("RemoveSymbol(parts, Ros2ForUnityRuntimeSelection.BaseCompileSymbol)", StringComparison.Ordinal),
                "146A-C3: define installer removes the base symbol when no active runtime is available");
            Check(!source.Contains("RuntimeCompileSymbols", StringComparison.Ordinal)
                  && !source.Contains("SelectedRuntime.CompileSymbol", StringComparison.Ordinal),
                "146A-C4: define installer does not synchronize per-runtime compile symbols");
        }

        private static void ManagerInspectorHostsOptionalSelector()
        {
            var source = ReadRepoText(ManagerInspectorPath);
            var managerInspectorSyntaxErrors = CSharpSyntaxTree.ParseText(source)
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray();
            var asmdef = ReadRepoText(ManagerInspectorAsmdefPath);
            var workflow = ReadRepoText(ManagerWorkflowPath);
            var dataTransportSource = ReadRepoText(ManagerDataTransportPath);
            var guard = ReadRepoText(PlayModeGuardPath);
            var topLevel = FindMethod(workflow, "OnInspectorGUI");
            var dataTransport = FindMethod(dataTransportSource, "DrawDataTransportSection");
            var allManagerEditorSources = PhaseValidationSourceHelpers.ReadFoxgloveManagerEditorSources();
            var allNativeRuntimeSubsections = FindInvocations(allManagerEditorSources)
                .Where(invocation => IsNativeRuntimeSubsection(invocation))
                .ToArray();
            var allNativeRuntimeCallbacks = FindInvocations(allManagerEditorSources)
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasMethodGroupArgument(invocation, "DrawR2fuRuntimeSection"))
                .ToArray();
            var nativeRuntimeSubsections = FindInvocations(dataTransport)
                .Where(invocation => IsNativeRuntimeSubsection(invocation))
                .ToArray();
            var nativeRuntimeCallbacks = FindInvocations(dataTransport)
                .Where(invocation => IsInvocationNamed(invocation, "DrawDataTransportSubsection")
                                     && HasMethodGroupArgument(invocation, "DrawR2fuRuntimeSection"))
                .ToArray();
            var nativeDemandBranches = dataTransport?.Body?.Statements
                .OfType<IfStatementSyntax>()
                .Where(HasNativeDemandCondition)
                .ToArray()
                ?? Array.Empty<IfStatementSyntax>();
            var branchNativeRuntimeSubsections = nativeDemandBranches
                .SelectMany(DirectThenStatements)
                .OfType<ExpressionStatementSyntax>()
                .Select(statement => statement.Expression as InvocationExpressionSyntax)
                .Where(invocation => invocation != null && IsNativeRuntimeSubsection(invocation))
                .ToArray();
            var legacyTopLevelRuntimeSections = FindInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection")
                                     && (HasStringArgument(invocation, 0, "ROS2 Runtime (R2FU)")
                                         || HasStringArgument(invocation, 0, "ROS 2 Native Runtime (R2FU)")))
                .ToArray();
            var topLevelRuntimeCallbacks = FindInvocations(topLevel)
                .Where(invocation => IsInvocationNamed(invocation, "DrawSection")
                                     && HasMethodGroupArgument(invocation, "DrawR2fuRuntimeSection"))
                .ToArray();
            var directRuntimeDraws = FindInvocations(allManagerEditorSources)
                .Where(invocation => IsInvocationNamed(invocation, "DrawR2fuRuntimeSection"))
                .ToArray();

            Check(managerInspectorSyntaxErrors.Length == 0,
                "146A-D0: the core optional R2FU Inspector seam remains valid C# source for Unity compilation");

            Check(source.Contains("FoxRunNativeDemandPolicy.HasNativeRuntimeDemand", StringComparison.Ordinal)
                  && source.Contains("HasGeneratedExplicitSource", StringComparison.Ordinal)
                  && topLevel != null
                  && allNativeRuntimeSubsections.Length == 1
                  && allNativeRuntimeCallbacks.Length == 1
                  && nativeRuntimeSubsections.Length == 1
                  && nativeRuntimeCallbacks.Length == 1
                  && nativeDemandBranches.Length == 1
                  && branchNativeRuntimeSubsections.Length == 1
                  && ReferenceEquals(nativeRuntimeSubsections[0], branchNativeRuntimeSubsections[0])
                  && legacyTopLevelRuntimeSections.Length == 0
                  && topLevelRuntimeCallbacks.Length == 0
                  && directRuntimeDraws.Length == 0
                  && guard.Contains("FoxRunNativeDemandPolicy.HasNativeRuntimeDemand", StringComparison.Ordinal)
                  && guard.Contains("_enableFoxRunInbound", StringComparison.Ordinal)
                  && guard.Contains("_defaultFoxRunSubscriptionSource", StringComparison.Ordinal),
                "146A-D1: unified native demand reaches one optional runtime selector only through the conditional Data Transport native-runtime subsection");
            const string selectorReflectionSeam =
                "Unity2Foxglove.Ros2ForUnity.Editor.Ros2ForUnityRuntimeSelectorInspector, Unity2Foxglove.Ros2ForUnity.Editor";
            const string diagnosticsReflectionSeam =
                "Unity2Foxglove.Ros2ForUnity.Native.Editor.FoxRunRos2SubscriptionDiagnosticsInspector, Unity2Foxglove.Ros2ForUnity.Native.Editor";
            const string customTypesupportReflectionSeam =
                "Unity2Foxglove.Ros2ForUnity.Editor.FoxRunRos2CustomTypesupportInspector, Unity2Foxglove.Ros2ForUnity.Editor";
            Check(source.Contains("\"" + selectorReflectionSeam + "\"", StringComparison.Ordinal)
                  && source.Contains("\"" + diagnosticsReflectionSeam + "\"", StringComparison.Ordinal)
                  && source.Contains("\"" + customTypesupportReflectionSeam + "\"", StringComparison.Ordinal)
                  && source.Contains("Type.GetType(R2fuRuntimeSelectorInspectorTypeName)", StringComparison.Ordinal)
                  && source.Contains("Type.GetType(R2fuNativeSubscriptionDiagnosticsInspectorTypeName)", StringComparison.Ordinal)
                  && source.Contains("Type.GetType(R2fuCustomTypesupportInspectorTypeName)", StringComparison.Ordinal)
                  && source.Contains("GetMethod", StringComparison.Ordinal),
                "146A-D2: core SDK declares its three optional R2FU Inspector reflection endpoints explicitly");
            Check(CountOccurrences(source, "Unity2Foxglove.Ros2ForUnity") == 6
                  && !source.Contains("using Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal)
                  && !source.Contains("global::Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal)
                  && !asmdef.Contains("Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal),
                "146A-D2b: core Inspector permits only the documented reflection seam and keeps no optional R2FU assembly dependency");
            Check(source.Contains("TargetInvocationException", StringComparison.Ordinal)
                  && source.Contains("DrawOptionalR2fuInspectorFailure", StringComparison.Ordinal),
                "146A-D3: optional selector failures are contained inside the Inspector UI");
            Check(!source.Contains("InnerException.Message", StringComparison.Ordinal)
                  && !source.Contains("ex.Message", StringComparison.Ordinal),
                "146A-D3b: optional selector failures never expose raw reflected exception messages in the Inspector");
            Check(source.Contains("private bool HasR2fuNativeSubscriptionDemand()", StringComparison.Ordinal)
                  && source.Contains("nativeOutputEnabled: false", StringComparison.Ordinal)
                  && source.Contains(
                      "var subscriptionDemand = HasR2fuNativeSubscriptionDemand() || HasCustomNativeSubscriptionDemand();",
                      StringComparison.Ordinal)
                  && source.Contains("if (outputDemand && subscriptionDemand)", StringComparison.Ordinal),
                "146A-D4: Inspector distinguishes simultaneous native publish and subscription demand from WebSocket-only subscriptions");
        }

        private static void RuntimeSelectorUsesOneDropdown()
        {
            var source = ReadRepoText(InspectorPath);

            Check(source.Contains("EditorGUILayout.Popup(\"Active Runtime\"", StringComparison.Ordinal),
                "146A-E1: runtime selection is a single Active Runtime dropdown");
            Check(source.Contains("EditorGUI.BeginChangeCheck()", StringComparison.Ordinal)
                  && source.Contains("EditorGUI.EndChangeCheck()", StringComparison.Ordinal),
                "146A-E2: dropdown switches runtime only after a user-driven change");
            Check(source.Contains("EditorApplication.isPlayingOrWillChangePlaymode", StringComparison.Ordinal)
                  && source.Contains("SwitchAndResolve(projectDirectory, installed[runtimeIndex])", StringComparison.Ordinal)
                  && !source.Contains("GUILayout.Button(\"Use", StringComparison.Ordinal),
                "146A-E3: selector has no extra confirmation button and refuses unsafe Play Mode switching");
            Check(source.Contains("No active runtime", StringComparison.Ordinal),
                "146A-E4: runtime selector shows a neutral placeholder when no runtime is active");
        }

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
            return File.ReadAllText(path);
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
