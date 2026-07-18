// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Read-only metadata discovery for one manifest-resolved custom ROS2 typesupport add-on.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    /// <summary>
    /// Reads only the static interface lock and the active add-on's declared
    /// metadata. It deliberately does not load a managed assembly, initialize
    /// ROS2, load a native plugin, enumerate arbitrary candidate directories,
    /// or mutate the Unity package manifest.
    /// </summary>
    internal static class Ros2ForUnityCustomTypesupportDiscovery
    {
        private const string LockRelativePath = "RuntimeSupport/foxrun-ros2-interface-lock.json";
        private const string AddOnManifestRelativePath = "RuntimeSupport/typesupport-manifest.json";
        private const string AddOnInventoryRelativePath = "RuntimeSupport/typesupport-inventory.json";
        private static readonly object CacheGate = new object();
        private static string _cachedProjectDirectory;
        private static string _cachedActiveAddOnPackage;
        private static Ros2ForUnityCustomTypesupportDiscoverySnapshot _cachedSnapshot;

        internal static Ros2ForUnityCustomTypesupportDiscoverySnapshot Discover(
            string projectDirectory,
            string activeAddOnPackage)
        {
            projectDirectory = projectDirectory ?? string.Empty;
            activeAddOnPackage = activeAddOnPackage ?? string.Empty;
            lock (CacheGate)
            {
                if (_cachedSnapshot != null
                    && StringEquals(_cachedProjectDirectory, projectDirectory)
                    && StringEquals(_cachedActiveAddOnPackage, activeAddOnPackage))
                {
                    return _cachedSnapshot;
                }
            }

            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            var source = ReadStaticSource(packagesDirectory);
            var snapshot = string.IsNullOrWhiteSpace(activeAddOnPackage)
                ? new Ros2ForUnityCustomTypesupportDiscoverySnapshot(source, null)
                : new Ros2ForUnityCustomTypesupportDiscoverySnapshot(
                    source,
                    ReadAddOn(
                        ResolveActiveAddOnDirectory(projectDirectory, activeAddOnPackage),
                        activeAddOnPackage));
            lock (CacheGate)
            {
                _cachedProjectDirectory = projectDirectory;
                _cachedActiveAddOnPackage = activeAddOnPackage;
                _cachedSnapshot = snapshot;
            }

            return snapshot;
        }

        /// <summary>
        /// Clears the bounded metadata cache after a package-resolution, source
        /// generation, or script-reload transition. The Inspector never polls
        /// package files every repaint; selection and command owners explicitly
        /// invalidate this cache when their authoritative state changes.
        /// </summary>
        internal static void InvalidateCache()
        {
            lock (CacheGate)
            {
                _cachedProjectDirectory = null;
                _cachedActiveAddOnPackage = null;
                _cachedSnapshot = null;
            }
        }

        private static Ros2ForUnityCustomTypesupportStaticSourceSnapshot ReadStaticSource(string packagesDirectory)
        {
            var lockPath = Path.Combine(
                packagesDirectory,
                Ros2ForUnityCustomTypesupportSelectionTransaction.StaticInterfacePackageId,
                LockRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(lockPath))
                return Ros2ForUnityCustomTypesupportStaticSourceSnapshot.Missing();

            if (!TryReadObject(lockPath, out var sourceLock))
                return Ros2ForUnityCustomTypesupportStaticSourceSnapshot.Invalid();

            var rosPackageName = Text(sourceLock["rosPackageName"]);
            var interfaceDigest = Text(sourceLock["interfaceDigest"]);
            var revision = sourceLock["interfaceRevision"]?.Value<int?>() ?? 0;
            var contracts = sourceLock["contracts"] as JArray;
            if (sourceLock["lockSchemaVersion"]?.Value<int?>() != 1
                || sourceLock["interfaceSchemaVersion"]?.Value<int?>() != 1
                || !StringEquals(
                    Text(sourceLock["unityPackageId"]),
                    Ros2ForUnityCustomTypesupportSelectionTransaction.StaticInterfacePackageId)
                || !StringEquals(
                    rosPackageName,
                    Ros2ForUnityCustomTypesupportSelectionTransaction.StaticRosPackageName)
                || revision <= 0
                || !IsSha256(interfaceDigest)
                || contracts == null)
            {
                return Ros2ForUnityCustomTypesupportStaticSourceSnapshot.Invalid();
            }

            var envelopes = contracts
                .OfType<JObject>()
                .Select(contract => Text(contract["envelopeMessageName"]))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => rosPackageName + "/msg/" + name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (envelopes.Length != contracts.Count)
                return Ros2ForUnityCustomTypesupportStaticSourceSnapshot.Invalid();

            return new Ros2ForUnityCustomTypesupportStaticSourceSnapshot(
                present: true,
                valid: true,
                rosPackageName,
                revision,
                interfaceDigest,
                envelopes);
        }

        private static Ros2ForUnityCustomTypesupportAddOnSnapshot ReadAddOn(
            string addOnDirectory,
            string activeAddOnPackage)
        {
            if (string.IsNullOrWhiteSpace(addOnDirectory))
                return Ros2ForUnityCustomTypesupportAddOnSnapshot.InvalidManifest(activeAddOnPackage);

            var packagePath = Path.Combine(addOnDirectory, "package.json");
            var manifestPath = Path.Combine(addOnDirectory, AddOnManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var inventoryPath = Path.Combine(addOnDirectory, AddOnInventoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!TryReadObject(packagePath, out var package)
                || !TryReadObject(manifestPath, out var manifest))
            {
                return Ros2ForUnityCustomTypesupportAddOnSnapshot.InvalidManifest(activeAddOnPackage);
            }

            var source = manifest["source"] as JObject;
            var baseRuntime = manifest["baseRuntime"] as JObject;
            var managed = manifest["managed"] as JObject;
            var typeMap = managed?["typeMap"] as JArray;
            var supportedRmws = manifest["supportedRmwImplementations"] as JArray;
            var dependencies = package["dependencies"] as JObject;
            var packageValid = StringEquals(Text(package["name"]), activeAddOnPackage)
                               && package["unity2foxgloveFoxRunCustomTypesupportAddOn"]?.Value<bool>() == true
                               && dependencies != null
                               && !string.IsNullOrWhiteSpace(Text(dependencies["dev.unity2foxglove.ros2forunity"]));
            var manifestValid = packageValid
                                && source != null
                                && baseRuntime != null
                                && managed != null
                                && typeMap != null
                                && supportedRmws != null
                                && manifest["schemaVersion"]?.Value<int?>() == 1
                                && StringEquals(
                                    Text(source["upmPackageId"]),
                                    Ros2ForUnityCustomTypesupportSelectionTransaction.StaticInterfacePackageId)
                                && StringEquals(
                                    Text(source["rosPackageName"]),
                                    Ros2ForUnityCustomTypesupportSelectionTransaction.StaticRosPackageName)
                                && source["interfaceRevision"]?.Value<int?>() > 0
                                && IsSha256(Text(source["interfaceDigest"]))
                                && !string.IsNullOrWhiteSpace(Text(manifest["distro"]))
                                && !string.IsNullOrWhiteSpace(Text(baseRuntime["packageId"]))
                                && StringEquals(Text(manifest["platform"]), "win64")
                                && StringEquals(Text(manifest["architecture"]), "x86_64");
            if (!manifestValid)
                return Ros2ForUnityCustomTypesupportAddOnSnapshot.InvalidManifest(activeAddOnPackage);

            var inventoryValid = TryReadObject(inventoryPath, out var inventory)
                                 && inventory["schemaVersion"]?.Value<int?>() == 1
                                 && inventory["entries"] is JArray;
            var entries = inventoryValid
                ? ((JArray)inventory["entries"]).OfType<JObject>().ToArray()
                : Array.Empty<JObject>();
            if (inventoryValid)
            {
                inventoryValid = entries.Length == ((JArray)inventory["entries"]).Count
                                 && entries.All(entry =>
                                     !string.IsNullOrWhiteSpace(Text(entry["path"]))
                                     && !string.IsNullOrWhiteSpace(Text(entry["role"]))
                                     && IsSha256(Text(entry["sha256"])));
            }

            var managedCanonicalTypes = typeMap
                .OfType<JObject>()
                .Where(entry => !string.IsNullOrWhiteSpace(Text(entry["canonicalRosType"]))
                                && !string.IsNullOrWhiteSpace(Text(entry["managedType"])))
                .Select(entry => Text(entry["canonicalRosType"]))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var rmws = supportedRmws
                .Select(Text)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var catalogCount = entries.Count(entry => StringEquals(Text(entry["role"]), "catalog"));
            return new Ros2ForUnityCustomTypesupportAddOnSnapshot(
                activeAddOnPackage,
                manifestValid: true,
                inventoryValid,
                Text(source["interfaceDigest"]),
                source["interfaceRevision"]?.Value<int?>() ?? 0,
                Text(manifest["distro"]),
                Text(baseRuntime["packageId"]),
                rmws,
                managedCanonicalTypes,
                catalogCount);
        }

        /// <summary>
        /// Uses Unity's resolved package metadata in the Editor. The test-only
        /// fallback parses the project's active file reference; it never falls
        /// back to an arbitrary repository candidate directory.
        /// </summary>
        private static string ResolveActiveAddOnDirectory(
            string projectDirectory,
            string activeAddOnPackage)
        {
#if UNITY_EDITOR
            try
            {
                var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(candidate => StringEquals(candidate.name, activeAddOnPackage));
                return package != null && !string.IsNullOrWhiteSpace(package.resolvedPath)
                    ? package.resolvedPath
                    : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
#else
            return ResolveFilePackageReference(projectDirectory, activeAddOnPackage);
#endif
        }

        private static string ResolveFilePackageReference(
            string projectDirectory,
            string activeAddOnPackage)
        {
            try
            {
                var manifestPath = Path.Combine(projectDirectory ?? string.Empty, "Packages", "manifest.json");
                var dependencies = JObject.Parse(File.ReadAllText(manifestPath))["dependencies"] as JObject;
                var packageReference = Text(dependencies?[activeAddOnPackage]);
                if (!packageReference.StartsWith("file:", StringComparison.Ordinal))
                    return string.Empty;

                var referencePath = Uri.UnescapeDataString(packageReference.Substring("file:".Length));
                string fullPath;
                if (Uri.TryCreate(referencePath, UriKind.Absolute, out var absoluteUri) && absoluteUri.IsFile)
                    fullPath = absoluteUri.LocalPath;
                else
                    fullPath = Path.GetFullPath(Path.Combine(projectDirectory, "Packages", referencePath));

                return Directory.Exists(fullPath) ? fullPath : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string RepositoryPackagesDirectory(string projectDirectory)
        {
            try
            {
                var project = new DirectoryInfo(projectDirectory ?? string.Empty);
                return Path.Combine(project.Parent?.FullName ?? string.Empty, "Packages");
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool TryReadObject(string path, out JObject value)
        {
            value = null;
            try
            {
                value = JObject.Parse(File.ReadAllText(path));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Text(JToken token)
            => token?.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : string.Empty;

        private static bool IsSha256(string value)
            => value?.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f'));

        private static bool StringEquals(string left, string right)
            => string.Equals(left, right, StringComparison.Ordinal);
    }

    internal sealed class Ros2ForUnityCustomTypesupportDiscoverySnapshot
    {
        public Ros2ForUnityCustomTypesupportDiscoverySnapshot(
            Ros2ForUnityCustomTypesupportStaticSourceSnapshot source,
            Ros2ForUnityCustomTypesupportAddOnSnapshot addOn)
        {
            Source = source ?? Ros2ForUnityCustomTypesupportStaticSourceSnapshot.Missing();
            AddOn = addOn;
        }

        public Ros2ForUnityCustomTypesupportStaticSourceSnapshot Source { get; }
        public Ros2ForUnityCustomTypesupportAddOnSnapshot AddOn { get; }
    }

    internal sealed class Ros2ForUnityCustomTypesupportStaticSourceSnapshot
    {
        public Ros2ForUnityCustomTypesupportStaticSourceSnapshot(
            bool present,
            bool valid,
            string rosPackageName,
            int interfaceRevision,
            string interfaceDigest,
            IReadOnlyList<string> envelopeTypes)
        {
            Present = present;
            Valid = valid;
            RosPackageName = rosPackageName ?? string.Empty;
            InterfaceRevision = interfaceRevision;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            EnvelopeTypes = (envelopeTypes ?? Array.Empty<string>()).ToArray();
        }

        public bool Present { get; }
        public bool Valid { get; }
        public string RosPackageName { get; }
        public int InterfaceRevision { get; }
        public string InterfaceDigest { get; }
        public IReadOnlyList<string> EnvelopeTypes { get; }

        public static Ros2ForUnityCustomTypesupportStaticSourceSnapshot Missing()
            => new Ros2ForUnityCustomTypesupportStaticSourceSnapshot(
                present: false,
                valid: false,
                string.Empty,
                0,
                string.Empty,
                Array.Empty<string>());

        public static Ros2ForUnityCustomTypesupportStaticSourceSnapshot Invalid()
            => new Ros2ForUnityCustomTypesupportStaticSourceSnapshot(
                present: true,
                valid: false,
                string.Empty,
                0,
                string.Empty,
                Array.Empty<string>());
    }

    internal sealed class Ros2ForUnityCustomTypesupportAddOnSnapshot
    {
        public Ros2ForUnityCustomTypesupportAddOnSnapshot(
            string packageId,
            bool manifestValid,
            bool inventoryValid,
            string interfaceDigest,
            int interfaceRevision,
            string distribution,
            string baseRuntimePackage,
            IReadOnlyList<string> supportedRmws,
            IReadOnlyList<string> managedCanonicalTypes,
            int catalogCount)
        {
            PackageId = packageId ?? string.Empty;
            ManifestValid = manifestValid;
            InventoryValid = inventoryValid;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            InterfaceRevision = interfaceRevision;
            Distribution = distribution ?? string.Empty;
            BaseRuntimePackage = baseRuntimePackage ?? string.Empty;
            SupportedRmws = (supportedRmws ?? Array.Empty<string>()).ToArray();
            ManagedCanonicalTypes = (managedCanonicalTypes ?? Array.Empty<string>()).ToArray();
            CatalogCount = catalogCount;
        }

        public string PackageId { get; }
        public bool ManifestValid { get; }
        public bool InventoryValid { get; }
        public string InterfaceDigest { get; }
        public int InterfaceRevision { get; }
        public string Distribution { get; }
        public string BaseRuntimePackage { get; }
        public IReadOnlyList<string> SupportedRmws { get; }
        public IReadOnlyList<string> ManagedCanonicalTypes { get; }
        public int CatalogCount { get; }

        public static Ros2ForUnityCustomTypesupportAddOnSnapshot InvalidManifest(string packageId)
            => new Ros2ForUnityCustomTypesupportAddOnSnapshot(
                packageId,
                manifestValid: false,
                inventoryValid: false,
                string.Empty,
                0,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<string>(),
                0);
    }
}
