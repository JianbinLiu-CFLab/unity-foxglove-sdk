// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Atomically select at most one validated Phase181 custom ROS2 typesupport add-on.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal enum Ros2ForUnityCustomTypesupportSelectionCode
    {
        Ready,
        BaseOnly,
        InvalidProject,
        InvalidBaseRuntime,
        InvalidManifest,
        RequestedCandidateNotReady,
        ResolveFailed,
        ResolvePending,
    }

    /// <summary>
    /// Bounded reason why a repository add-on was not eligible for selection.
    /// This is deliberately separate from the public selection result: callers
    /// receive enough evidence to correct package state without exposing local
    /// paths, hashes, or raw I/O exceptions.
    /// </summary>
    internal enum Ros2ForUnityCustomTypesupportCandidateValidationCode
    {
        None = 0,
        Ready = 1,
        MissingCandidate = 2,
        PackageMetadata = 3,
        StaticSource = 4,
        SourceIdentity = 5,
        ManagedIdentity = 6,
        NativeClosure = 7,
        Unexpected = 8,
    }

    internal sealed class Ros2ForUnityCustomTypesupportSelectionResult
    {
        public Ros2ForUnityCustomTypesupportSelectionResult(
            Ros2ForUnityCustomTypesupportSelectionCode code,
            string activeAddOnPackage,
            string interfaceDigest,
            string baseRuntimeAbiDigest,
            string nativePluginDirectory,
            Ros2ForUnityCustomTypesupportCandidateValidationCode candidateValidationCode =
                Ros2ForUnityCustomTypesupportCandidateValidationCode.None)
        {
            Code = code;
            ActiveAddOnPackage = activeAddOnPackage ?? string.Empty;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            BaseRuntimeAbiDigest = baseRuntimeAbiDigest ?? string.Empty;
            NativePluginDirectory = nativePluginDirectory ?? string.Empty;
            CandidateValidationCode = candidateValidationCode;
        }

        public Ros2ForUnityCustomTypesupportSelectionCode Code { get; }
        public string ActiveAddOnPackage { get; }
        public string InterfaceDigest { get; }
        public string BaseRuntimeAbiDigest { get; }
        public string NativePluginDirectory { get; }
        public Ros2ForUnityCustomTypesupportCandidateValidationCode CandidateValidationCode { get; }
        public bool IsReady => Code == Ros2ForUnityCustomTypesupportSelectionCode.Ready;
    }

    /// <summary>
    /// Pure filesystem/manifest half of runtime/add-on selection.  The caller
    /// supplies the Package Manager resolve action; this type deliberately
    /// never initializes ROS2, loads a native plugin, or writes packages-lock.
    /// </summary>
    internal static partial class Ros2ForUnityCustomTypesupportSelectionTransaction
    {
        internal const string CustomTypesupportPackagePrefix =
            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.";
        private const string RuntimePackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
        internal const string StaticInterfacePackageId = "dev.unity2foxglove.foxrun.ros2.interfaces";
        private const string OptionalFacadePackageId = "dev.unity2foxglove.ros2forunity";
        private const string NativePluginRelativeDirectory =
            "Runtime/Ros2ForUnity/Plugins/Windows/x86_64";
        private const string Ros2csCommonRelativePath =
            "Runtime/Ros2ForUnity/Plugins/ros2cs_common.dll";

        public static Ros2ForUnityCustomTypesupportSelectionResult Apply(
            string projectDirectory,
            string selectedBaseRuntimePackage,
            string requestedAddOnPackage,
            Action resolve)
        {
            if (resolve == null)
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidProject);
            return Apply(
                projectDirectory,
                selectedBaseRuntimePackage,
                requestedAddOnPackage,
                () =>
                {
                    resolve();
                    // An Action can start Package Manager resolution, but it
                    // cannot prove that the requested packages are registered.
                    // Treat it as pending instead of claiming synchronous
                    // completion.
                    return false;
                });
        }

        internal static string ReadManifestText(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            var manifestPath = Path.Combine(projectDirectory, "Packages", "manifest.json");
            var text = File.ReadAllText(manifestPath);
            ParseManifestJson(text);
            return text;
        }

        internal static void RestoreManifest(string projectDirectory, string originalManifest)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrEmpty(originalManifest))
            {
                throw new InvalidOperationException("The original Unity package manifest is unavailable.");
            }

            ParseManifestJson(originalManifest);
            var manifestPath = Path.Combine(projectDirectory, "Packages", "manifest.json");
            WriteAtomically(manifestPath, originalManifest);
            ParseManifestJson(File.ReadAllText(manifestPath));
        }

        public static Ros2ForUnityCustomTypesupportSelectionResult Apply(
            string projectDirectory,
            string selectedBaseRuntimePackage,
            string requestedAddOnPackage,
            Func<bool> resolveAndConfirm)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(selectedBaseRuntimePackage)
                || resolveAndConfirm == null)
            {
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidProject);
            }

            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            var manifestPath = Path.Combine(projectDirectory, "Packages", "manifest.json");
            if (!Directory.Exists(packagesDirectory) || !File.Exists(manifestPath))
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidProject);

            if (!TryReadBaseRuntime(packagesDirectory, selectedBaseRuntimePackage, out var baseRuntime))
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidBaseRuntime);

            string originalManifest;
            JObject manifest;
            try
            {
                originalManifest = File.ReadAllText(manifestPath);
                manifest = ParseManifestJson(originalManifest);
            }
            catch (Exception)
            {
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidManifest);
            }

            Candidate selected = null;
            if (!string.IsNullOrWhiteSpace(requestedAddOnPackage))
            {
                selected = TryReadCandidateByPackageId(
                    packagesDirectory,
                    baseRuntime,
                    requestedAddOnPackage,
                    out var candidateValidationCode);
                if (selected == null)
                    return Failure(
                        Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady,
                        candidateValidationCode);
            }
            else
            {
                var candidates = DiscoverValidatedCandidates(packagesDirectory, baseRuntime).ToArray();
                if (candidates.Length == 1)
                    selected = candidates[0];
            }

            var updated = (JObject)manifest.DeepClone();
            var dependencies = (JObject)updated["dependencies"];
            RemoveOwnedDependencies(dependencies);
            AddDependency(dependencies, selectedBaseRuntimePackage, BuildPackageReference(projectDirectory, packagesDirectory, selectedBaseRuntimePackage));
            if (selected != null)
            {
                AddDependency(
                    dependencies,
                    StaticInterfacePackageId,
                    BuildPackageReference(projectDirectory, packagesDirectory, StaticInterfacePackageId));
                AddDependency(dependencies, selected.PackageId, BuildPackageReference(projectDirectory, packagesDirectory, selected.PackageId));
            }

            var rendered = SerializeManifest(updated, DetectLineEnding(originalManifest));
            try
            {
                // Parse the rendered mutation before persistence, then parse the
                // persisted content before Resolve. This keeps the transaction
                // fail-closed even if a future serializer/write path regresses.
                ParseManifestJson(rendered);
                WriteAtomically(manifestPath, rendered);
                ParseManifestJson(File.ReadAllText(manifestPath));
                var resolved = resolveAndConfirm();
                if (!resolved)
                    return BuildSelectionResult(
                        Ros2ForUnityCustomTypesupportSelectionCode.ResolvePending,
                        selected,
                        baseRuntime);
            }
            catch (Exception)
            {
                try
                {
                    WriteAtomically(manifestPath, originalManifest);
                }
                catch (Exception)
                {
                    // Preserve the bounded result; callers never receive the raw I/O error.
                }

                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.ResolveFailed);
            }

            return BuildSelectionResult(
                selected == null
                    ? Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly
                    : Ros2ForUnityCustomTypesupportSelectionCode.Ready,
                selected,
                baseRuntime);
        }

        private static Ros2ForUnityCustomTypesupportSelectionResult BuildSelectionResult(
            Ros2ForUnityCustomTypesupportSelectionCode code,
            Candidate selected,
            BaseRuntime baseRuntime)
            => selected == null
                ? new Ros2ForUnityCustomTypesupportSelectionResult(
                    code, string.Empty, string.Empty,
                    baseRuntime.ManifestDigest, string.Empty)
                : new Ros2ForUnityCustomTypesupportSelectionResult(
                    code, selected.PackageId, selected.InterfaceDigest,
                    baseRuntime.ManifestDigest,
                    selected.NativePluginDirectory);

        /// <summary>
        /// Re-evaluates the add-on which Unity has already resolved in the
        /// project manifest. This performs no writes and never initializes
        /// ROS2 or a native plugin, so it is safe for define/restart/preflight
        /// decisions before a native session begins.
        /// </summary>
        public static Ros2ForUnityCustomTypesupportSelectionResult EvaluateActive(
            string projectDirectory,
            string selectedBaseRuntimePackage)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(selectedBaseRuntimePackage))
            {
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidProject);
            }

            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            var manifestPath = Path.Combine(projectDirectory, "Packages", "manifest.json");
            if (!Directory.Exists(packagesDirectory) || !File.Exists(manifestPath))
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidProject);

            if (!TryReadBaseRuntime(packagesDirectory, selectedBaseRuntimePackage, out var baseRuntime))
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidBaseRuntime);

            if (!TryReadObject(manifestPath, out var manifest) || !(manifest["dependencies"] is JObject dependencies))
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.InvalidManifest);

            var active = dependencies.Properties()
                .Where(property => property.Name.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal))
                .Select(property => property.Name)
                .OrderBy(packageId => packageId, StringComparer.Ordinal)
                .ToArray();
            if (active.Length == 0)
            {
                return new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly,
                    string.Empty,
                    string.Empty,
                    baseRuntime.ManifestDigest,
                    string.Empty);
            }

            if (active.Length != 1)
                return Failure(Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady);

            var candidate = TryReadCandidateByPackageId(
                packagesDirectory,
                baseRuntime,
                active[0],
                out var candidateValidationCode);
            return candidate == null
                ? Failure(
                    Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady,
                    candidateValidationCode)
                : new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.Ready,
                    candidate.PackageId,
                    candidate.InterfaceDigest,
                    baseRuntime.ManifestDigest,
                    candidate.NativePluginDirectory);
        }

        internal static IReadOnlyList<string> DiscoverCandidatePackageIds(string projectDirectory)
        {
            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            if (!Directory.Exists(packagesDirectory))
                return Array.Empty<string>();
            return Directory.GetDirectories(packagesDirectory, CustomTypesupportPackagePrefix + "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string GetActiveAddOnPackageId(string projectDirectory)
        {
            var active = GetActiveAddOnPackageIds(projectDirectory);
            return active.Count == 1 ? active[0] : string.Empty;
        }

        /// <summary>
        /// Returns the manifest-resolved custom add-on package IDs only. This
        /// is intentionally a bounded metadata query for Inspector/preflight
        /// presentation; it does not inspect candidate directories, mutate a
        /// manifest, resolve packages, or initialize ROS2.
        /// </summary>
        internal static IReadOnlyList<string> GetActiveAddOnPackageIds(string projectDirectory)
        {
            var manifestPath = Path.Combine(projectDirectory ?? string.Empty, "Packages", "manifest.json");
            try
            {
                var dependencies = JObject.Parse(File.ReadAllText(manifestPath))["dependencies"] as JObject;
                return dependencies?.Properties()
                    .Where(property => property.Name.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal))
                    .Select(property => property.Name)
                    .OrderBy(packageId => packageId, StringComparer.Ordinal)
                    .ToArray() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        internal static string PluginDirectory(string projectDirectory, string packageId)
        {
            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            return string.IsNullOrWhiteSpace(packagesDirectory) || string.IsNullOrWhiteSpace(packageId)
                ? string.Empty
                : Path.Combine(packagesDirectory, packageId, NativePluginRelativeDirectory);
        }

        private static IEnumerable<Candidate> DiscoverValidatedCandidates(string packagesDirectory, BaseRuntime baseRuntime)
        {
            foreach (var directory in Directory.GetDirectories(
                         packagesDirectory,
                         CustomTypesupportPackagePrefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (TryReadCandidate(directory, baseRuntime, out var candidate, out _))
                    yield return candidate;
            }
        }

        private static Candidate TryReadCandidateByPackageId(
            string packagesDirectory,
            BaseRuntime baseRuntime,
            string packageId,
            out Ros2ForUnityCustomTypesupportCandidateValidationCode validationCode)
        {
            validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.MissingCandidate;
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            foreach (var directory in Directory.GetDirectories(
                         packagesDirectory,
                         CustomTypesupportPackagePrefix + "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (!StringEquals(Path.GetFileName(directory), packageId))
                    continue;

                return TryReadCandidate(directory, baseRuntime, out var candidate, out validationCode)
                    ? candidate
                    : null;
            }

            return null;
        }

    }
}
