// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 108 ROS2 For Unity facade boundary validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase108Validation.
    /// </summary>
    public static partial class Phase108Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string Runtime = OptionalPackage + "/Runtime";
        private const string CorePackageJson = "Packages/dev.unity2foxglove.sdk/package.json";
        private const string OptionalPackageValidator = "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py";

        private static int _passed;
        private static IReadOnlyList<string> _runtimeTextFiles;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 108: Unity2Foxglove ROS2 Facade Boundary ===");
            _passed = 0;
            _runtimeTextFiles = null;

            VerifyRuntimeSurface();
            VerifyUnavailableBehavior();
            VerifyPackageBoundaries();
            VerifyValidationWiring();
            VerifyDocsBoundary();
            VerifyBinaryBoundary();

            Console.WriteLine($"Phase 108: {_passed} checks passed.");
        }

        private static void VerifyRuntimeSurface()
        {
            Check(RepoDirectoryExists(Runtime), "108-A1: optional package Runtime folder exists");

            var asmdef = LoadJsonObject(Runtime + "/Unity2Foxglove.Ros2ForUnity.asmdef");
            Check((string)asmdef["name"] == "Unity2Foxglove.Ros2ForUnity"
                  && (string)asmdef["rootNamespace"] == "Unity2Foxglove.Ros2ForUnity",
                "108-A2: optional package Runtime asmdef defines the owned facade assembly");
            Check(asmdef["references"] is JArray references
                  && references.All(item => (string)item == "Unity.FoxgloveSDK"),
                "108-A3: optional package Runtime asmdef references only the core SDK");

            var required = new[]
            {
                Runtime + "/Unity2FoxgloveRos2Status.cs",
                Runtime + "/IUnity2FoxgloveRos2Context.cs",
                Runtime + "/IUnity2FoxgloveRos2Node.cs",
                Runtime + "/IUnity2FoxgloveRos2Publisher.cs",
                Runtime + "/IUnity2FoxgloveRos2Subscription.cs",
                Runtime + "/Unity2FoxgloveRos2UnavailableContext.cs",
                Runtime + "/Unity2FoxgloveRos2ContextFactory.cs"
            };

            foreach (var path in required)
                Check(RepoFileExists(path), "108-A4: facade runtime file exists: " + path);

            var combined = ReadRuntimeSources();
            Check(combined.Contains("namespace Unity2Foxglove.Ros2ForUnity", StringComparison.Ordinal),
                "108-A5: facade uses Unity2Foxglove.Ros2ForUnity namespace");
            Check(combined.Contains("enum Unity2FoxgloveRos2Status", StringComparison.Ordinal)
                  && combined.Contains("Unavailable = 0", StringComparison.Ordinal)
                  && combined.Contains("Ready = 1", StringComparison.Ordinal)
                  && combined.Contains("Error = 2", StringComparison.Ordinal)
                  && combined.Contains("Disposed = 3", StringComparison.Ordinal),
                "108-A6: facade exposes the expected status enum");
            Check(combined.Contains("interface IUnity2FoxgloveRos2Context", StringComparison.Ordinal)
                  && combined.Contains("interface IUnity2FoxgloveRos2Node", StringComparison.Ordinal)
                  && combined.Contains("interface IUnity2FoxgloveRos2Publisher<in T>", StringComparison.Ordinal)
                  && combined.Contains("interface IUnity2FoxgloveRos2Subscription", StringComparison.Ordinal),
                "108-A7: facade exposes context, node, publisher, and subscription interfaces");
            Check(combined.Contains("Unity2FoxgloveRos2ContextFactory", StringComparison.Ordinal)
                  && combined.Contains("Unity2FoxgloveRos2UnavailableContext.Instance", StringComparison.Ordinal),
                "108-A8: context factory defaults to unavailable context");
        }

        private static void VerifyUnavailableBehavior()
        {
            var factory = FindType("Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory");
            if (factory == null)
            {
                Check(true,
                    "108-B0: unavailable facade runtime behavior is skipped unless IncludeRos2ForUnityAdapter=true");
                return;
            }

            Check(true,
                "108-B0: unavailable facade runtime types are loaded when adapter validation is selected");

            var context = factory.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            Check(context != null, "108-B1: factory returns a context");
            Check(GetBoolProperty(context, "IsAvailable") == false
                  && string.Equals(GetProperty(context, "Status")?.ToString(), "Unavailable", StringComparison.Ordinal)
                  && GetStringProperty(context, "StatusMessage").Contains("not bundled", StringComparison.OrdinalIgnoreCase),
                "108-B2: default context reports unavailable with actionable message");

            var node = Invoke(context, "CreateNode", "unity2foxglove_phase108");
            Check(node != null && GetStringProperty(node, "Name") == "unity2foxglove_phase108",
                "108-B3: unavailable context returns an inspectable no-op node");

            var publisher = InvokeGeneric(node, "CreatePublisher", typeof(string), "/unity2foxglove/phase108/out");
            Check(GetStringProperty(publisher, "Topic") == "/unity2foxglove/phase108/out",
                "108-B4: unavailable publisher preserves topic");
            var tryPublish = publisher.GetType().GetMethod("TryPublish", new[] { typeof(string), typeof(string).MakeByRefType() });
            Check(tryPublish != null, "108-B5: unavailable publisher exposes TryPublish(string, out string)");
            if (tryPublish == null)
                return;
            var publishArgs = new object[] { "hello", null };
            var published = (bool)tryPublish.Invoke(publisher, publishArgs);
            Check(!published && !string.IsNullOrWhiteSpace((string)publishArgs[1]),
                "108-B6: unavailable publisher TryPublish returns false with error");

            var received = false;
            var subscription = InvokeGeneric(
                node,
                "CreateSubscription",
                typeof(string),
                "/unity2foxglove/phase108/in",
                new Action<string>(_ => received = true));
            Check(GetStringProperty(subscription, "Topic") == "/unity2foxglove/phase108/in" && !received,
                "108-B7: unavailable subscription preserves topic without invoking callback");

            try
            {
                DisposeTwice(publisher);
                DisposeTwice(subscription);
                DisposeTwice(node);
                DisposeTwice(context);
                Check(true, "108-B8: unavailable facade Dispose methods are idempotent");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("108-B8: unavailable facade Dispose methods are idempotent", ex);
            }
        }

        private static void VerifyPackageBoundaries()
        {
            var optional = LoadJsonObject(OptionalPackage + "/package.json");
            Check(optional["dependencies"] is JObject optionalDependencies
                  && optionalDependencies.Count == 1
                  && (string)optionalDependencies["dev.unity2foxglove.sdk"] == "1.9.6",
                "108-C1: optional package depends only on the core SDK package");

            var core = LoadJsonObject(CorePackageJson);
            var coreDependencies = core["dependencies"] as JObject;
            Check(coreDependencies == null || coreDependencies["dev.unity2foxglove.ros2forunity"] == null,
                "108-C2: core SDK package still does not depend on optional R2FU package");

            var offenders = RuntimeTextFiles()
                .SelectMany(path =>
                {
                    if (IsAllowedRuntimeTokenSource(path))
                        return Array.Empty<string>();

                    var text = File.ReadAllText(path);
                    return ForbiddenRuntimeTokens()
                        .Where(token => text.Contains(token, StringComparison.Ordinal))
                        .Select(token => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/') + " -> " + token);
                })
                .ToList();

            Check(offenders.Count == 0,
                "108-C3: optional Runtime has no upstream ROS2/R2FU API references"
                + (offenders.Count == 0 ? string.Empty : " (" + string.Join(", ", offenders) + ")"));
            var editorFiles = RepoDirectoryExists(OptionalPackage + "/Editor")
                ? Directory.GetFiles(
                        Path.Combine(RepoRoot(), OptionalPackage, "Editor"),
                        "*.*",
                        SearchOption.AllDirectories)
                    .Where(HasTextExtension)
                    .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList()
                : new List<string>();
            var allEditorFiles = RepoDirectoryExists(OptionalPackage + "/Editor")
                ? Directory.GetFiles(
                        Path.Combine(RepoRoot(), OptionalPackage, "Editor"),
                        "*",
                        SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList()
                : new List<string>();
            var expectedTopLevelEditorFiles = new[]
            {
                OptionalPackage + "/Editor/FoxRunRos2CustomTypesupportInspector.cs",
                OptionalPackage + "/Editor/Phase181Ros2RuntimeBatchSelection.cs",
                OptionalPackage + "/Editor/Ros2ForUnityCustomTypesupportDiscovery.cs",
                OptionalPackage + "/Editor/Ros2ForUnityCustomTypesupportPreflight.cs",
                OptionalPackage + "/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs",
                OptionalPackage + "/Editor/Ros2ForUnityEditorDiagnostics.cs",
                OptionalPackage + "/Editor/Ros2ForUnityEditorRestartRelay.cs",
                OptionalPackage + "/Editor/Ros2ForUnityInteractiveSelectionCoordinator.cs",
                OptionalPackage + "/Editor/Ros2ForUnityInteractiveSelectionState.cs",
                OptionalPackage + "/Editor/Ros2ForUnityManagerSetupDrawer.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimeCapabilityModel.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimeDefineInstaller.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimePlayModeGuard.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimePlayerStaging.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimeSelection.cs",
                OptionalPackage + "/Editor/Ros2ForUnityRuntimeSelectorInspector.cs",
                OptionalPackage + "/Editor/Ros2ForUnityZenohRouterSettings.cs",
                OptionalPackage + "/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef"
            };
            var expectedDecomposedEditorFiles = new[]
            {
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ManifestUtilities.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/Models.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/NativeClosure.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ValidationAndIdentity.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Discovery.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/EnvironmentLease.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Manifest.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RestartAndEnvironment.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RuntimeDescriptor.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SelectionStatus.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SessionAndCommunication.cs"
            };
            var expectedDecomposedEditorPaths = new[]
            {
                OptionalPackage + "/Editor/Decomposed.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ManifestUtilities.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ManifestUtilities.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/Models.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/Models.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/NativeClosure.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/NativeClosure.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ValidationAndIdentity.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityCustomTypesupportSelectionTransaction/ValidationAndIdentity.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Discovery.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Discovery.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/EnvironmentLease.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/EnvironmentLease.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Manifest.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/Manifest.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RestartAndEnvironment.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RestartAndEnvironment.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RuntimeDescriptor.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/RuntimeDescriptor.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SelectionStatus.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SelectionStatus.cs.meta",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SessionAndCommunication.cs",
                OptionalPackage + "/Editor/Decomposed/Ros2ForUnityRuntimeSelection/SessionAndCommunication.cs.meta"
            };
            var topLevelEditorFiles = editorFiles
                .Where(path =>
                    !path.Substring(
                            (OptionalPackage + "/Editor/").Length)
                        .Contains("/", StringComparison.Ordinal))
                .ToList();
            var unexpectedEditorFiles = editorFiles
                .Where(path =>
                    !expectedTopLevelEditorFiles.Contains(
                        path,
                        StringComparer.Ordinal)
                    && !path.StartsWith(
                        OptionalPackage + "/Editor/Native/",
                        StringComparison.Ordinal)
                    && !path.StartsWith(
                        OptionalPackage + "/Editor/SourceGenerators/",
                        StringComparison.Ordinal)
                    && !expectedDecomposedEditorFiles.Contains(
                        path,
                        StringComparer.Ordinal)
                    )
                .ToList();
            var decomposedEditorFiles = editorFiles
                .Where(path => path.StartsWith(
                    OptionalPackage + "/Editor/Decomposed/",
                    StringComparison.Ordinal))
                .ToList();
            var decomposedEditorPaths = allEditorFiles
                .Where(path => path.Equals(
                                   OptionalPackage + "/Editor/Decomposed.meta",
                                   StringComparison.Ordinal)
                               || path.StartsWith(
                                   OptionalPackage + "/Editor/Decomposed/",
                                   StringComparison.Ordinal))
                .ToList();
            Check(topLevelEditorFiles.SequenceEqual(expectedTopLevelEditorFiles)
                  && decomposedEditorFiles.SequenceEqual(expectedDecomposedEditorFiles)
                  && decomposedEditorPaths.SequenceEqual(expectedDecomposedEditorPaths)
                  && unexpectedEditorFiles.Count == 0
                  && editorFiles.Contains(
                      OptionalPackage + "/Editor/Native/FoxRunR2fuEmitterContribution.cs",
                      StringComparer.Ordinal)
                  && editorFiles.Contains(
                      OptionalPackage + "/Editor/Native/FoxRunR2fuProviderDrawer.cs",
                      StringComparer.Ordinal)
                  && editorFiles.Contains(
                      OptionalPackage + "/Editor/SourceGenerators/src/FoxRunR2fuAnalyzerPipeline.cs",
                      StringComparer.Ordinal),
                "108-C4: optional package Editor surface is limited to activation, Provider integration, and its owned source generator");
        }

        private static void VerifyValidationWiring()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var phase107 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs");
            var validator = ReadRepoText(OptionalPackageValidator);

            Check(registry.Contains("--phase108", StringComparison.Ordinal)
                  && registry.Contains("Phase108Validation.Validate", StringComparison.Ordinal),
                "108-D1: registry wires --phase108");
            Check(project.Contains("Phase108Validation.cs", StringComparison.Ordinal)
                  && project.Contains("../../../dev.unity2foxglove.ros2forunity/Runtime/**/*.cs", StringComparison.Ordinal)
                  && project.Contains("IncludeRos2ForUnityAdapter", StringComparison.Ordinal),
                "108-D2: test project compiles Phase108Validation and gates optional Runtime facade sources");
            Check(phase107.Contains("VerifyOptionalRuntimeBoundary", StringComparison.Ordinal)
                  && !phase107.Contains("!RepoDirectoryExists(OptionalPackage + \"/Runtime\")", StringComparison.Ordinal),
                "108-D3: Phase107 validation allows facade-only Runtime after Phase108");
            Check(validator.Contains("check_runtime_source_boundary", StringComparison.Ordinal)
                  && validator.Contains("FORBIDDEN_RUNTIME_TOKENS", StringComparison.Ordinal),
                "108-D4: optional package validator checks facade-only Runtime source boundary");
        }

        private static void VerifyDocsBoundary()
        {
            var readme = ReadRepoText(OptionalPackage + "/README.md");
            Check(readme.Contains("The facade is an API boundary only", StringComparison.Ordinal)
                  && readme.Contains("not end-user ready for ROS2 publishing", StringComparison.OrdinalIgnoreCase)
                  && readme.Contains("backing implementation", StringComparison.Ordinal),
                "108-E1: optional package README documents facade-only status");
        }

        private static void VerifyBinaryBoundary()
        {
            var tracked = PhaseRos2ForUnityValidationHelpers.GitLsFiles(RepoRoot());
            var forbiddenTracked = tracked
                .Where(path => path.StartsWith("Unity2Foxglove/Assets/Ros2ForUnity", StringComparison.Ordinal)
                               || IsForbiddenR2fuArtifact(path)
                               || IsOptionalPackageRuntimeBinary(path))
                .ToList();

            Check(forbiddenTracked.Count == 0,
                "108-F1: tracked files contain no R2FU imported assets, packages, metadata, or optional runtime binaries"
                + (forbiddenTracked.Count == 0 ? string.Empty : " (" + string.Join(", ", forbiddenTracked) + ")"));
        }

        private static IEnumerable<string> ForbiddenRuntimeTokens()
        {
            return new[]
            {
                "using ROS2;",
                "namespace ROS2",
                "ROS2UnityComponent",
                "ROS2Node",
                "IPublisher<",
                "ISubscription<",
                "std_msgs",
                "ros2cs"
            };
        }

        private static IReadOnlyList<string> RuntimeTextFiles()
        {
            if (_runtimeTextFiles != null)
                return _runtimeTextFiles;

            var root = Path.Combine(RepoRoot(), Runtime.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
            {
                _runtimeTextFiles = Array.Empty<string>();
                return _runtimeTextFiles;
            }

            _runtimeTextFiles = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(HasTextExtension)
                .ToArray();
            return _runtimeTextFiles;
        }

        private static bool IsAllowedRuntimeTokenSource(string path)
        {
            // Runtime/Native is the productized ROS2 integration layer and intentionally
            // references upstream ROS2 APIs, validated by distribution/runtime validators.
            var runtimeRoot = Path.Combine(RepoRoot(), Runtime.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var runtimeRootNormalized = Path.GetFullPath(runtimeRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            if (!normalizedPath.StartsWith(runtimeRootNormalized, StringComparison.Ordinal))
                return false;

            var relative = Path.GetRelativePath(runtimeRoot, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length > 0 && string.Equals(parts[0], "Native", StringComparison.Ordinal);
        }

        private static string ReadRuntimeSources()
        {
            return string.Join("\n", RuntimeTextFiles()
                .Where(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        }
    }
}
