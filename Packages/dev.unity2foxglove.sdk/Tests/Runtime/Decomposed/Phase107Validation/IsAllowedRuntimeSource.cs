// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 107 ROS2 For Unity optional package distribution gate validation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase107Validation.
    /// </summary>
    public static partial class Phase107Validation
    {
        private static bool IsAllowedRuntimeSource(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsForbiddenRuntimeToken(string path)
        {
            if (IsAllowedRuntimeTokenSource(path))
            {
                return false;
            }

            if (!HasTextExtension(path))
                return false;

            var text = File.ReadAllText(path);
            return OptionalEditorForbiddenTokens().Any(token => text.Contains(token, StringComparison.Ordinal));
        }

        private static bool IsAllowedRuntimeTokenSource(string path)
        {
            // Native runtime integration files intentionally depend on ROS2 APIs and are
            // validated as part of runtime distribution checks.
            var runtimeRoot = Path.Combine(
                RepoRoot(),
                OptionalPackage.Replace('/', Path.DirectorySeparatorChar),
                "Runtime");
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var runtimeRootNormalized = Path.GetFullPath(runtimeRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path);
            if (!normalizedPath.StartsWith(runtimeRootNormalized, StringComparison.Ordinal))
            {
                return false;
            }

            var relative = Path.GetRelativePath(runtimeRoot, normalizedPath);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length > 0 && string.Equals(parts[0], "Native", StringComparison.Ordinal);
        }

        private static IReadOnlyList<string> OptionalEditorForbiddenTokens()
            => OptionalEditorForbiddenTokenList;

        private static bool IsSanctionedOptionalEditorMetadataToken(string path, string token, string text)
        {
            if (!String.Equals(token, "ros2cs", StringComparison.Ordinal)
                || !HasMetadataOnlyRos2csIdentityVerifier(Path.GetDirectoryName(path) ?? string.Empty, path, text))
            {
                return false;
            }

            var relative = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
            return String.Equals(
                relative,
                OptionalPackage + "/Editor/Ros2ForUnityCustomTypesupportSelectionTransaction.cs",
                StringComparison.Ordinal);
        }

        private static bool HasMetadataOnlyRos2csIdentityVerifier(string editorRoot)
        {
            var selectionTransaction = Path.Combine(editorRoot, "Ros2ForUnityCustomTypesupportSelectionTransaction.cs");
            return File.Exists(selectionTransaction)
                   && HasMetadataOnlyRos2csIdentityVerifier(
                       editorRoot,
                       selectionTransaction,
                       File.ReadAllText(selectionTransaction));
        }

        private static bool HasMetadataOnlyRos2csIdentityVerifier(string editorRoot, string path, string text)
        {
            var expected = Path.Combine(editorRoot, "Ros2ForUnityCustomTypesupportSelectionTransaction.cs");
            return String.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase)
                   && text.Contains("Ros2csCommonRelativePath", StringComparison.Ordinal)
                   && text.Contains("TryReadAssemblyMvid(baseAssembly", StringComparison.Ordinal)
                   && text.Contains("FileSha256(baseAssembly)", StringComparison.Ordinal)
                   && !text.Contains("using ROS2;", StringComparison.Ordinal)
                   && !text.Contains("ROS2UnityComponent", StringComparison.Ordinal)
                   && !text.Contains("ROS2Node", StringComparison.Ordinal)
                   && !text.Contains("Assembly.Load", StringComparison.Ordinal);
        }

        private static bool IsForbiddenR2fuArtifact(string path)
        {
            return PhaseRos2ForUnityValidationHelpers.IsForbiddenR2fuArtifact(path);
        }

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

        private static bool IsAllowedRuntimePackageFile(string path)
        {
            if (!IsExplicitRuntimePackagePath(path))
                return false;

            return !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitRuntimePackagePath(string path)
            => path.StartsWith("Packages/dev.unity2foxglove.ros2forunity.runtime.", StringComparison.Ordinal);

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
                throw new FileNotFoundException("Missing required Phase107 file: " + relativePath, path);
            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
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
                throw new DirectoryNotFoundException("Could not find repository root for Phase107 validation.");
            return root;
        }
    }
}
