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
        private static bool IsForbiddenR2fuArtifact(string path)
        {
            return !IsExplicitRuntimePackagePath(path)
                   && PhaseRos2ForUnityValidationHelpers.IsForbiddenR2fuArtifact(path);
        }

        private static bool IsExplicitRuntimePackagePath(string path)
            => path.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal);

        private static bool IsOptionalPackageRuntimeBinary(string path)
        {
            if (IsOwnedOptionalEditorAnalyzer(path))
                return false;

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

        private static bool IsOwnedOptionalEditorAnalyzer(string path)
        {
            var absolute = Path.IsPathRooted(path)
                ? path
                : Path.Combine(
                    RepoRoot(),
                    path.Replace('/', Path.DirectorySeparatorChar));
            var relative = Path.GetRelativePath(RepoRoot(), absolute).Replace('\\', '/');
            return relative.Equals(
                OptionalPackage
                + "/Editor/SourceGenerators/analyzers/dotnet/cs/"
                + "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll",
                StringComparison.Ordinal);
        }

        private static JObject LoadJsonObject(string relativePath)
        {
            var text = ReadRepoText(relativePath);
            return JObject.Parse(text);
        }

        private static bool HasTextExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
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
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
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
                    && ParametersMatch(candidate.GetParameters(), args));
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.MakeGenericMethod(genericType).Invoke(target, args);
        }

        private static bool ParametersMatch(ParameterInfo[] parameters, object[] args)
        {
            if (parameters.Length != args.Length)
                return false;

            for (var i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                    continue;

                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]))
                    return false;
            }

            return true;
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
