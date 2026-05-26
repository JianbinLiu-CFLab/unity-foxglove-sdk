// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 108 ROS2 For Unity facade boundary validation.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase108Validation
    {
        private const string OptionalPackage = "Packages/dev.unity2foxglove.ros2forunity";
        private const string Runtime = OptionalPackage + "/Runtime";
        private const string CorePackageJson = "Packages/dev.unity2foxglove.sdk/package.json";
        private const string OptionalPackageValidator = "Scripts/release/validate_ros2forunity_package.py";

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 108: Unity2Foxglove ROS2 Facade Boundary ===");
            _passed = 0;

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
            Check(asmdef["references"] is JArray references && references.Count == 0,
                "108-A3: optional package Runtime asmdef has no assembly references");

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
            var publishArgs = new object[] { "hello", null };
            var published = (bool)tryPublish.Invoke(publisher, publishArgs);
            Check(!published && !string.IsNullOrWhiteSpace((string)publishArgs[1]),
                "108-B5: unavailable publisher TryPublish returns false with error");

            var received = false;
            var subscription = InvokeGeneric(
                node,
                "CreateSubscription",
                typeof(string),
                "/unity2foxglove/phase108/in",
                new Action<string>(_ => received = true));
            Check(GetStringProperty(subscription, "Topic") == "/unity2foxglove/phase108/in" && !received,
                "108-B6: unavailable subscription preserves topic without invoking callback");

            DisposeTwice(publisher);
            DisposeTwice(subscription);
            DisposeTwice(node);
            DisposeTwice(context);
            Check(true, "108-B7: unavailable facade Dispose methods are idempotent");
        }

        private static void VerifyPackageBoundaries()
        {
            var optional = LoadJsonObject(OptionalPackage + "/package.json");
            Check(optional["dependencies"] is JObject optionalDependencies && optionalDependencies.Count == 0,
                "108-C1: optional package still has no dependencies");

            var core = LoadJsonObject(CorePackageJson);
            var coreDependencies = core["dependencies"] as JObject;
            Check(coreDependencies == null || coreDependencies["dev.unity2foxglove.ros2forunity"] == null,
                "108-C2: core SDK package still does not depend on optional R2FU package");

            var offenders = RuntimeTextFiles()
                .SelectMany(path => ForbiddenRuntimeTokens()
                    .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                    .Select(token => Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/') + " -> " + token))
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
            var expectedEditorFiles = new[]
            {
                OptionalPackage + "/Editor/Ros2ForUnityRuntimeDefineInstaller.cs",
                OptionalPackage + "/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef"
            };
            Check(editorFiles.SequenceEqual(expectedEditorFiles),
                "108-C4: optional package Editor surface is limited to runtime define management");
        }

        private static void VerifyValidationWiring()
        {
            var program = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Program.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var phase107 = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase107Validation.cs");
            var validator = ReadRepoText(OptionalPackageValidator);

            Check(program.Contains("--phase108", StringComparison.Ordinal)
                  && program.Contains("RunPhase108Only", StringComparison.Ordinal)
                  && program.Contains("Phase108Validation.Validate()", StringComparison.Ordinal),
                "108-D1: Program.cs wires --phase108");
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
            var tracked = GitLsFiles();
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

        private static IEnumerable<string> RuntimeTextFiles()
        {
            var root = Path.Combine(RepoRoot(), Runtime.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(root))
                return Array.Empty<string>();

            return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(HasTextExtension);
        }

        private static string ReadRuntimeSources()
        {
            return string.Join("\n", RuntimeTextFiles()
                .Where(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));
        }

        private static bool IsForbiddenR2fuArtifact(string path)
        {
            if (path.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/", StringComparison.Ordinal))
                return false;

            return path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("Ros2ForUnity_humble_standalone_windows11.zip", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOptionalPackageRuntimeBinary(string path)
        {
            if (!path.StartsWith(OptionalPackage + "/", StringComparison.Ordinal))
                return false;

            var extension = Path.GetExtension(path);
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".so", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2cs.xml", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("metadata_ros2_for_unity.xml", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject LoadJsonObject(string relativePath)
        {
            var text = ReadRepoText(relativePath);
            return JObject.Parse(text);
        }

        private static IReadOnlyList<string> GitLsFiles()
        {
            var root = RepoRoot();
            var start = new ProcessStartInfo("git", "ls-files")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(start);
            if (process == null)
                throw new InvalidOperationException("Could not start git ls-files.");

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new InvalidOperationException("git ls-files failed: " + error);

            return output.Replace("\r\n", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Replace('\\', '/'))
                .ToList();
        }

        private static bool HasTextExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RepoFileExists(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path);
        }

        private static bool RepoDirectoryExists(string relativePath)
        {
            var path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(path);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = RepoRoot();
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase108 file: " + relativePath, path);
            return File.ReadAllText(path);
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(type => type != null);
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(target);
        }

        private static bool GetBoolProperty(object target, string name)
        {
            return GetProperty(target, name) is bool value && value;
        }

        private static string GetStringProperty(object target, string name)
        {
            return GetProperty(target, name) as string ?? string.Empty;
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName
                    && !candidate.IsGenericMethodDefinition
                    && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.Invoke(target, args);
        }

        private static object InvokeGeneric(object target, string methodName, Type genericType, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName
                    && candidate.IsGenericMethodDefinition
                    && candidate.GetGenericArguments().Length == 1
                    && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.MakeGenericMethod(genericType).Invoke(target, args);
        }

        private static void DisposeTwice(object target)
        {
            if (target is not IDisposable disposable)
                throw new InvalidOperationException("Target is not disposable: " + target.GetType().FullName);

            disposable.Dispose();
            disposable.Dispose();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
            _passed++;
            Console.WriteLine("[PASS] " + message);
        }

        private static string RepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase108 validation.");
            return root;
        }
    }
}
