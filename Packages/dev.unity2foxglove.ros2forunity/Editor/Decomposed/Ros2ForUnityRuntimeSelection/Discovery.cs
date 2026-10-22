// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityRuntimeSelection
    {
        public static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> DiscoverCandidateRuntimes(string projectDirectory)
        {
            if (_cachedCandidates != null
                && string.Equals(_cachedCandidatesProjectDirectory, projectDirectory, StringComparison.Ordinal))
            {
                return _cachedCandidates;
            }

            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
            {
                _cachedCandidatesProjectDirectory = projectDirectory;
                _cachedCandidates = Array.Empty<Ros2ForUnityRuntimeDescriptor>();
                return _cachedCandidates;
            }

            var candidates = Directory
                .GetDirectories(packagesDirectory, RuntimePackagePrefix + "*", SearchOption.TopDirectoryOnly)
                .Where(path => !IsEmbeddedPackage(projectDirectory, path))
                .Select(TryCreateDescriptor)
                .Where(descriptor => descriptor != null)
                .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ToArray();
            _cachedCandidatesProjectDirectory = projectDirectory;
            _cachedCandidates = candidates;
            return candidates;
        }

        public static string RepositoryPackagesDirectory(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                return string.Empty;

            var project = new DirectoryInfo(projectDirectory);
            return Path.Combine(project.Parent?.FullName ?? string.Empty, "Packages");
        }

        public static IReadOnlyList<string> ReadManifestRuntimePackages(string projectDirectory)
        {
            var manifestPath = ManifestPath(projectDirectory);
            var lockPath = Path.Combine(projectDirectory, "Packages", "packages-lock.json");
            if (!File.Exists(manifestPath))
            {
                _cachedManifestProjectDirectory = projectDirectory;
                _cachedManifestWriteTimeUtc = DateTime.MinValue;
                _cachedManifestLength = -1;
                _cachedLockWriteTimeUtc = DateTime.MinValue;
                _cachedLockLength = -1;
                _cachedManifestRuntimePackages = Array.Empty<string>();
                return _cachedManifestRuntimePackages;
            }

            var manifestInfo = new FileInfo(manifestPath);
            var lockInfo = File.Exists(lockPath) ? new FileInfo(lockPath) : null;
            if (_cachedManifestRuntimePackages != null
                && string.Equals(_cachedManifestProjectDirectory, projectDirectory, StringComparison.Ordinal)
                && _cachedManifestWriteTimeUtc == manifestInfo.LastWriteTimeUtc
                && _cachedManifestLength == manifestInfo.Length
                && _cachedLockWriteTimeUtc == (lockInfo?.LastWriteTimeUtc ?? DateTime.MinValue)
                && _cachedLockLength == (lockInfo?.Length ?? -1))
            {
                return _cachedManifestRuntimePackages;
            }

            var packages = new HashSet<string>(StringComparer.Ordinal);
            var dependencies = ReadManifestDependencies(File.ReadAllText(manifestPath), manifestPath);
            if (dependencies == null)
            {
                _cachedManifestProjectDirectory = projectDirectory;
                _cachedManifestWriteTimeUtc = manifestInfo.LastWriteTimeUtc;
                _cachedManifestLength = manifestInfo.Length;
            }
            else
            {
                AddRuntimePackageNames(dependencies, packages);
            }

            if (lockInfo != null)
            {
                var lockDependencies = ReadManifestDependencies(
                    File.ReadAllText(lockPath),
                    lockPath);
                if (lockDependencies != null)
                    AddRuntimePackageNames(lockDependencies, packages);
            }

            _cachedManifestProjectDirectory = projectDirectory;
            _cachedManifestWriteTimeUtc = manifestInfo.LastWriteTimeUtc;
            _cachedManifestLength = manifestInfo.Length;
            _cachedLockWriteTimeUtc = lockInfo?.LastWriteTimeUtc ?? DateTime.MinValue;
            _cachedLockLength = lockInfo?.Length ?? -1;
            _cachedManifestRuntimePackages = packages.ToArray();
            return _cachedManifestRuntimePackages;
        }

        private static void AddRuntimePackageNames(
            JObject dependencies,
            ISet<string> destination)
        {
            foreach (var property in dependencies.Properties())
            {
                if (property.Name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                    destination.Add(property.Name);
            }
        }

        public static bool HasManifestRuntimePackage(string projectDirectory)
            => ReadManifestRuntimePackages(projectDirectory).Count > 0;

        public static void InvalidateStatusCache()
        {
            Ros2ForUnityCustomTypesupportDiscovery.InvalidateCache();
            _cachedCandidatesProjectDirectory = null;
            _cachedCandidates = null;
            _cachedManifestProjectDirectory = null;
            _cachedManifestWriteTimeUtc = DateTime.MinValue;
            _cachedManifestLength = -1;
            _cachedLockWriteTimeUtc = DateTime.MinValue;
            _cachedLockLength = -1;
            _cachedManifestRuntimePackages = null;
            ZenohPayloadDiagnostics.Clear();
        }

        private static Ros2ForUnityRuntimeDescriptor TryCreateDescriptor(string packageDirectory)
        {
            var packageName = Path.GetFileName(packageDirectory);
            if (string.IsNullOrWhiteSpace(packageName) || !packageName.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                return null;

            var packageJson = Path.Combine(packageDirectory, "package.json");
            if (!File.Exists(packageJson) || !ContainsPackageName(File.ReadAllText(packageJson), packageName))
                return null;

            var suffix = packageName.Substring(RuntimePackagePrefix.Length);
            var parts = suffix.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return null;

            var packageRosDistro = parts[0];
            var packagePlatform = string.Join(".", parts.Skip(1));
            var capabilities = ReadRuntimeCapabilities(packageDirectory);
            if (!capabilities.IsValid || capabilities.CommunicationModes.Count == 0)
                return null;

            if (!HasCanonicalRuntimeIdentity(
                    packageDirectory,
                    packageName,
                    packageRosDistro,
                    packagePlatform))
                return null;

            var pluginsRoot = Path.Combine(packageDirectory, "Runtime", "Ros2ForUnity", "Plugins");
            var scriptsRoot = Path.Combine(packageDirectory, "Runtime", "Ros2ForUnity", "Scripts");
            if (!Directory.Exists(pluginsRoot) || !Directory.Exists(scriptsRoot))
                return null;

            // A runtime package is authoritative only when its manifest identity
            // agrees with the package suffix and every advertised Fast DDS mode
            // has its native RMW payload present.  Do not select a package whose
            // bytes claim another distro or an incomplete native closure.
            if ((!string.IsNullOrWhiteSpace(capabilities.RosDistro)
                 && !string.Equals(
                     capabilities.RosDistro,
                     packageRosDistro,
                     StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(capabilities.Platform)
                    && !string.Equals(
                        capabilities.Platform,
                        packagePlatform,
                        StringComparison.OrdinalIgnoreCase)))
                return null;

            if (capabilities.CommunicationModes.Any(mode =>
                    string.Equals(
                        mode.RmwImplementation,
                        Ros2ForUnityRuntimeCapabilityParser.FastDdsRmwImplementation,
                        StringComparison.Ordinal))
                && !HasNativeLibrary(
                    Path.Combine(packageDirectory, WindowsNativePluginRelativeDirectory),
                    "rmw_fastrtps_cpp"))
                return null;

            var rosDistro = string.IsNullOrWhiteSpace(capabilities.RosDistro)
                ? packageRosDistro
                : capabilities.RosDistro;
            var platform = string.IsNullOrWhiteSpace(capabilities.Platform)
                ? packagePlatform
                : capabilities.Platform;
            var runtimeId = string.IsNullOrWhiteSpace(capabilities.RuntimeId)
                ? "r2fu-" + rosDistro + "-" + platform.Replace('.', '-')
                : capabilities.RuntimeId;
            var displayName = ToDisplayName(rosDistro) + " " + ToDisplayName(platform.Replace('.', ' '));
            var zenohPayloadDiagnostic = GetZenohPayloadDiagnostic(packageDirectory, capabilities.SupportsZenoh);

            var descriptor = new Ros2ForUnityRuntimeDescriptor(
                displayName,
                packageName,
                runtimeId,
                rosDistro,
                platform,
                capabilities,
                zenohPayloadDiagnostic);
            return descriptor.CommunicationModes.Count == 0 ? null : descriptor;
        }

        private static Ros2ForUnityRuntimeCapabilities ReadRuntimeCapabilities(string packageDirectory)
        {
            var manifestPath = Path.Combine(packageDirectory, "RuntimeSupport", "runtime-manifest.json");
            if (!File.Exists(manifestPath))
                return Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);

            try
            {
                return Ros2ForUnityRuntimeCapabilityParser.Parse(File.ReadAllText(manifestPath));
            }
            catch
            {
                return Ros2ForUnityRuntimeCapabilityParser.Parse(string.Empty);
            }
        }

        private static bool HasCanonicalRuntimeIdentity(
            string packageDirectory,
            string packageName,
            string packageRosDistro,
            string packagePlatform)
        {
            var manifestPath = Path.Combine(
                packageDirectory,
                "RuntimeSupport",
                "runtime-manifest.json");
            if (!File.Exists(manifestPath))
                return false;

            try
            {
                var manifest = JObject.Parse(File.ReadAllText(manifestPath));
                var packageJsonPath = Path.Combine(packageDirectory, "package.json");
                var packageJson = JObject.Parse(File.ReadAllText(packageJsonPath));
                var runtimeId = (string)manifest["runtimeId"];
                if (!string.Equals(
                        (string)manifest["packageName"],
                        packageName,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        (string)manifest["rosDistro"],
                        packageRosDistro,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        (string)manifest["platform"],
                        packagePlatform,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.Equals(
                        (string)packageJson["unity2foxgloveRuntimeId"],
                        runtimeId,
                        StringComparison.Ordinal))
                    return false;
                var conflicts = packageJson["unity2foxgloveConflicts"] as JArray;
                if (conflicts == null
                    || conflicts.Count == 0
                    || conflicts.Any(token =>
                        token.Type != JTokenType.String
                        || string.Equals(token.Value<string>(), packageName, StringComparison.Ordinal)
                        || !token.Value<string>().StartsWith(
                            RuntimePackagePrefix,
                            StringComparison.Ordinal)))
                    return false;

                var conflictNames = conflicts
                    .Values<string>()
                    .ToArray();
                if (conflictNames.Length != conflictNames.Distinct(StringComparer.Ordinal).Count())
                    return false;

                var runtimeRoot = (string)manifest["runtimeRoot"];
                var criticalFiles = manifest["criticalRuntimeFiles"] as JArray;
                if (string.IsNullOrWhiteSpace(runtimeRoot)
                    || criticalFiles == null
                    || criticalFiles.Count == 0)
                    return false;

                var root = Path.Combine(packageDirectory, runtimeRoot);
                foreach (var token in criticalFiles)
                {
                    var fileName = token?.Value<string>();
                    if (string.IsNullOrWhiteSpace(fileName)
                        || !Directory.EnumerateFiles(
                            root,
                            fileName,
                            SearchOption.AllDirectories).Any())
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetZenohPayloadDiagnostic(string packageDirectory, bool manifestDeclaresZenoh)
        {
            if (!manifestDeclaresZenoh)
                return string.Empty;

            var cacheKey = packageDirectory;
            if (ZenohPayloadDiagnostics.TryGetValue(cacheKey, out var cached))
                return cached;

            var diagnostic = ComputeZenohPayloadDiagnostic(packageDirectory);
            ZenohPayloadDiagnostics[cacheKey] = diagnostic;
            return diagnostic;
        }

    }
}
#endif
