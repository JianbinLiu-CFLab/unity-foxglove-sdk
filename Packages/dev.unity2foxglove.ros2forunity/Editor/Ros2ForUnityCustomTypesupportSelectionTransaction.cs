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
    }

    internal sealed class Ros2ForUnityCustomTypesupportSelectionResult
    {
        public Ros2ForUnityCustomTypesupportSelectionResult(
            Ros2ForUnityCustomTypesupportSelectionCode code,
            string activeAddOnPackage,
            string interfaceDigest,
            string baseRuntimeAbiDigest,
            string nativePluginDirectory)
        {
            Code = code;
            ActiveAddOnPackage = activeAddOnPackage ?? string.Empty;
            InterfaceDigest = interfaceDigest ?? string.Empty;
            BaseRuntimeAbiDigest = baseRuntimeAbiDigest ?? string.Empty;
            NativePluginDirectory = nativePluginDirectory ?? string.Empty;
        }

        public Ros2ForUnityCustomTypesupportSelectionCode Code { get; }
        public string ActiveAddOnPackage { get; }
        public string InterfaceDigest { get; }
        public string BaseRuntimeAbiDigest { get; }
        public string NativePluginDirectory { get; }
        public bool IsReady => Code == Ros2ForUnityCustomTypesupportSelectionCode.Ready;
    }

    /// <summary>
    /// Pure filesystem/manifest half of runtime/add-on selection.  The caller
    /// supplies the Package Manager resolve action; this type deliberately
    /// never initializes ROS2, loads a native plugin, or writes packages-lock.
    /// </summary>
    internal static class Ros2ForUnityCustomTypesupportSelectionTransaction
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
            if (string.IsNullOrWhiteSpace(projectDirectory)
                || string.IsNullOrWhiteSpace(selectedBaseRuntimePackage)
                || resolve == null)
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

            var candidates = DiscoverValidatedCandidates(packagesDirectory, baseRuntime).ToArray();
            Candidate selected = null;
            if (!string.IsNullOrWhiteSpace(requestedAddOnPackage))
            {
                selected = candidates.FirstOrDefault(candidate => StringEquals(candidate.PackageId, requestedAddOnPackage));
                if (selected == null)
                    return Failure(Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady);
            }
            else if (candidates.Length == 1)
            {
                selected = candidates[0];
            }

            var updated = (JObject)manifest.DeepClone();
            var dependencies = (JObject)updated["dependencies"];
            RemoveOwnedDependencies(dependencies);
            AddDependency(dependencies, selectedBaseRuntimePackage, BuildPackageReference(projectDirectory, packagesDirectory, selectedBaseRuntimePackage));
            if (selected != null)
                AddDependency(dependencies, selected.PackageId, BuildPackageReference(projectDirectory, packagesDirectory, selected.PackageId));

            var rendered = SerializeManifest(updated, DetectLineEnding(originalManifest));
            try
            {
                // Parse the rendered mutation before persistence, then parse the
                // persisted content before Resolve. This keeps the transaction
                // fail-closed even if a future serializer/write path regresses.
                ParseManifestJson(rendered);
                WriteAtomically(manifestPath, rendered);
                ParseManifestJson(File.ReadAllText(manifestPath));
                resolve();
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

            return selected == null
                ? new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.BaseOnly,
                    string.Empty,
                    string.Empty,
                    baseRuntime.ManifestDigest,
                    string.Empty)
                : new Ros2ForUnityCustomTypesupportSelectionResult(
                    Ros2ForUnityCustomTypesupportSelectionCode.Ready,
                    selected.PackageId,
                    selected.InterfaceDigest,
                    baseRuntime.ManifestDigest,
                    selected.NativePluginDirectory);
        }

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

            var candidate = DiscoverValidatedCandidates(packagesDirectory, baseRuntime)
                .FirstOrDefault(value => StringEquals(value.PackageId, active[0]));
            return candidate == null
                ? Failure(Ros2ForUnityCustomTypesupportSelectionCode.RequestedCandidateNotReady)
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
                if (TryReadCandidate(directory, baseRuntime, out var candidate))
                    yield return candidate;
            }
        }

        private static bool TryReadBaseRuntime(
            string packagesDirectory,
            string packageId,
            out BaseRuntime baseRuntime)
        {
            baseRuntime = null;
            if (!packageId.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                return false;
            var directory = Path.Combine(packagesDirectory, packageId);
            if (!IsChildPath(directory, packagesDirectory))
                return false;
            if (!TryReadObject(Path.Combine(directory, "RuntimeSupport", "runtime-manifest.json"), out var manifest))
                return false;
            var packageVersion = Text(manifest["packageVersion"]);
            var distro = Text(manifest["rosDistro"]);
            if (!StringEquals(Text(manifest["packageName"]), packageId)
                || string.IsNullOrWhiteSpace(packageVersion)
                || string.IsNullOrWhiteSpace(distro)
                || !StringEquals(Text(manifest["platform"]), "win64")
                || !StringEquals(Text(manifest["architecture"]), "x86_64"))
            {
                return false;
            }

            baseRuntime = new BaseRuntime(
                packageId,
                packageVersion,
                distro,
                NormalizedJsonSha256(manifest),
                directory);
            return true;
        }

        private static bool TryReadCandidate(string directory, BaseRuntime baseRuntime, out Candidate candidate)
        {
            candidate = null;
            try
            {
                var packageId = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(packageId)
                    || !packageId.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal)
                    || !TryReadObject(Path.Combine(directory, "package.json"), out var package)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-manifest.json"), out var manifest)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-inventory.json"), out var inventory))
                {
                    return false;
                }

                var dependencies = package["dependencies"] as JObject;
                if (!StringEquals(Text(package["name"]), packageId)
                    || package["unity2foxgloveFoxRunCustomTypesupportAddOn"]?.Value<bool>() != true
                    || dependencies == null
                    || Text(dependencies[OptionalFacadePackageId]).Length == 0
                    || !StringEquals(Text(dependencies[baseRuntime.PackageId]), baseRuntime.PackageVersion))
                {
                    return false;
                }

                var source = manifest["source"] as JObject;
                var baseRuntimeToken = manifest["baseRuntime"] as JObject;
                if (source == null || baseRuntimeToken == null
                    || !TryReadStaticSourceLock(directory, out var staticSource)
                    || !StringEquals(Text(source["upmPackageId"]), StaticInterfacePackageId)
                    || !StringEquals(Text(source["rosPackageName"]), staticSource.RosPackageName)
                    || source["interfaceRevision"]?.Value<int?>() != staticSource.InterfaceRevision
                    || !StringEquals(Text(source["interfaceDigest"]), staticSource.InterfaceDigest)
                    || !StringEquals(Text(manifest["distro"]), baseRuntime.Distro)
                    || !StringEquals(Text(manifest["platform"]), "win64")
                    || !StringEquals(Text(manifest["architecture"]), "x86_64")
                    || !StringEquals(Text(baseRuntimeToken["packageId"]), baseRuntime.PackageId)
                    || !StringEquals(Text(baseRuntimeToken["runtimeManifestSha256"]), baseRuntime.ManifestDigest)
                    || baseRuntimeToken["runtimeManifestVersion"]?.Value<int?>() != 1)
                {
                    return false;
                }

                if (!HasMatchingRos2csIdentity(directory, manifest, baseRuntime)
                    || !HasVerifiedNativeClosure(directory, manifest, inventory, baseRuntime))
                {
                    return false;
                }

                candidate = new Candidate(
                    packageId,
                    Text(source["interfaceDigest"]),
                    Path.Combine(directory, NativePluginRelativeDirectory));
                return true;
            }
            catch (Exception)
            {
                // Package contents are untrusted input to this selector.
                candidate = null;
                return false;
            }
        }

        private static bool TryReadStaticSourceLock(string candidateDirectory, out StaticSourceLock staticSource)
        {
            staticSource = null;
            var packagesDirectory = Directory.GetParent(candidateDirectory)?.FullName;
            var staticLock = Path.Combine(
                packagesDirectory ?? string.Empty,
                StaticInterfacePackageId,
                "RuntimeSupport",
                "foxrun-ros2-interface-lock.json");
            if (!TryReadObject(staticLock, out var sourceLock))
                return false;
            var rosPackageName = Text(sourceLock["rosPackageName"]);
            var interfaceRevision = sourceLock["interfaceRevision"]?.Value<int?>() ?? 0;
            var interfaceDigest = Text(sourceLock["interfaceDigest"]);
            if (!StringEquals(Text(sourceLock["unityPackageId"]), StaticInterfacePackageId)
                || !IsRevisionedRosPackageName(rosPackageName, interfaceRevision)
                || !IsSha256(interfaceDigest))
            {
                return false;
            }

            staticSource = new StaticSourceLock(rosPackageName, interfaceRevision, interfaceDigest);
            return true;
        }

        private static bool HasMatchingRos2csIdentity(string candidateDirectory, JObject manifest, BaseRuntime baseRuntime)
        {
            var identity = manifest["managed"]?["ros2Message"] as JObject;
            var baseAssembly = Path.Combine(baseRuntime.Directory, Ros2csCommonRelativePath);
            return identity != null
                   && StringEquals(Text(identity["assemblyName"]), "ros2cs_common")
                   && IsVersion(Text(identity["version"]))
                   && IsGuid(Text(identity["mvid"]))
                   && IsSha256(Text(identity["sha256"]))
                   && File.Exists(baseAssembly)
                   && TryReadAssemblyMvid(baseAssembly, out var actualMvid)
                   && StringEquals(Text(identity["mvid"]), actualMvid)
                   && StringEquals(Text(identity["sha256"]), FileSha256(baseAssembly));
        }

        // Reads only CLI metadata rather than loading ros2cs_common.dll into the
        // Editor AppDomain. Loading the selected native runtime's managed
        // dependency here could pin a stale DLL across a package switch.
        private static bool TryReadAssemblyMvid(string path, out string mvid)
        {
            mvid = string.Empty;
            try
            {
                var image = File.ReadAllBytes(path);
                if (!TryReadUInt16(image, 0, out var dosSignature) || dosSignature != 0x5a4d
                    || !TryReadUInt32(image, 0x3c, out var peOffsetValue)
                    || !TryToOffset(peOffsetValue, image.Length, out var peOffset)
                    || !TryReadUInt32(image, peOffset, out var peSignature) || peSignature != 0x00004550
                    || !TryReadUInt16(image, peOffset + 6, out var sectionCount)
                    || !TryReadUInt16(image, peOffset + 20, out var optionalHeaderSize))
                {
                    return false;
                }

                var optionalHeader = peOffset + 24;
                if (!HasRange(image, optionalHeader, optionalHeaderSize)
                    || !TryReadUInt16(image, optionalHeader, out var magic))
                {
                    return false;
                }

                var dataDirectoryOffset = magic == 0x10b ? 96 : magic == 0x20b ? 112 : -1;
                var cliDirectory = optionalHeader + dataDirectoryOffset + 14 * 8;
                if (dataDirectoryOffset < 0
                    || !TryReadUInt32(image, cliDirectory, out var cliRva)
                    || !TryRvaToFileOffset(image, cliRva, optionalHeader + optionalHeaderSize, sectionCount, out var cliOffset)
                    || !TryReadUInt32(image, cliOffset + 8, out var metadataRva)
                    || !TryRvaToFileOffset(image, metadataRva, optionalHeader + optionalHeaderSize, sectionCount, out var metadataOffset)
                    || !TryReadUInt32(image, metadataOffset, out var metadataSignature)
                    || metadataSignature != 0x424a5342
                    || !TryReadUInt32(image, metadataOffset + 12, out var versionLengthValue)
                    || !TryToOffset(versionLengthValue, image.Length, out var versionLength))
                {
                    return false;
                }

                var streamHeaders = Align4(metadataOffset + 16 + versionLength);
                if (!TryReadUInt16(image, streamHeaders + 2, out var streamCount))
                    return false;
                streamHeaders += 4;

                var guidHeapOffset = -1;
                var tablesOffset = -1;
                var cursor = streamHeaders;
                for (var index = 0; index < streamCount; index++)
                {
                    if (!TryReadUInt32(image, cursor, out var relativeOffset)
                        || !TryReadUInt32(image, cursor + 4, out _)
                        || !TryToOffset(relativeOffset, image.Length, out var streamOffset)
                        || !TryReadStreamName(image, cursor + 8, out var name, out var nextHeader))
                    {
                        return false;
                    }

                    if (string.Equals(name, "#GUID", StringComparison.Ordinal))
                        guidHeapOffset = metadataOffset + streamOffset;
                    else if (string.Equals(name, "#~", StringComparison.Ordinal)
                             || string.Equals(name, "#-", StringComparison.Ordinal))
                        tablesOffset = metadataOffset + streamOffset;
                    cursor = nextHeader;
                }

                if (guidHeapOffset < 0 || tablesOffset < 0
                    || !TryReadByte(image, tablesOffset + 6, out var heapSizes)
                    || !TryReadUInt64(image, tablesOffset + 8, out var validTables)
                    || (validTables & 1UL) == 0)
                {
                    return false;
                }

                var rowCounts = tablesOffset + 24;
                var tableCount = CountSetBits(validTables);
                if (!TryReadUInt32(image, rowCounts, out var moduleRows) || moduleRows == 0
                    || !HasRange(image, rowCounts, tableCount * 4))
                {
                    return false;
                }

                var tableData = rowCounts + tableCount * 4;
                var stringIndexBytes = (heapSizes & 0x01) == 0 ? 2 : 4;
                var guidIndexBytes = (heapSizes & 0x02) == 0 ? 2 : 4;
                var mvidIndexOffset = tableData + 2 + stringIndexBytes;
                uint mvidIndex;
                if (guidIndexBytes == 2)
                {
                    if (!TryReadUInt16(image, mvidIndexOffset, out var shortMvidIndex))
                        return false;
                    mvidIndex = shortMvidIndex;
                }
                else if (!TryReadUInt32(image, mvidIndexOffset, out mvidIndex))
                {
                    return false;
                }

                if (mvidIndex == 0 || mvidIndex > int.MaxValue)
                    return false;
                var mvidOffsetValue = (long)guidHeapOffset + ((long)mvidIndex - 1L) * 16L;
                if (mvidOffsetValue < 0 || mvidOffsetValue > int.MaxValue)
                    return false;
                var mvidOffset = (int)mvidOffsetValue;
                if (!HasRange(image, mvidOffset, 16))
                    return false;
                var bytes = new byte[16];
                Array.Copy(image, mvidOffset, bytes, 0, bytes.Length);
                mvid = new Guid(bytes).ToString("D");
                return true;
            }
            catch (Exception)
            {
                mvid = string.Empty;
                return false;
            }
        }

        private static bool TryRvaToFileOffset(byte[] image, uint rva, int sectionHeaders, ushort sectionCount, out int offset)
        {
            offset = -1;
            for (var index = 0; index < sectionCount; index++)
            {
                var section = sectionHeaders + index * 40;
                if (!TryReadUInt32(image, section + 8, out var virtualSize)
                    || !TryReadUInt32(image, section + 12, out var virtualAddress)
                    || !TryReadUInt32(image, section + 16, out var rawSize)
                    || !TryReadUInt32(image, section + 20, out var rawOffset))
                {
                    return false;
                }

                var sectionLength = Math.Max(virtualSize, rawSize);
                if (rva < virtualAddress || (ulong)rva >= (ulong)virtualAddress + sectionLength)
                    continue;
                return TryToOffset(rawOffset + rva - virtualAddress, image.Length, out offset);
            }

            return false;
        }

        private static bool TryReadStreamName(byte[] image, int offset, out string name, out int nextHeader)
        {
            name = string.Empty;
            nextHeader = -1;
            var end = offset;
            while (end < image.Length && image[end] != 0)
                end++;
            if (end >= image.Length)
                return false;
            name = System.Text.Encoding.ASCII.GetString(image, offset, end - offset);
            nextHeader = Align4(end + 1);
            return nextHeader <= image.Length;
        }

        private static int Align4(int value)
            => (value + 3) & ~3;

        private static int CountSetBits(ulong value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }

        private static bool TryReadByte(byte[] image, int offset, out byte value)
        {
            value = 0;
            if (!HasRange(image, offset, 1))
                return false;
            value = image[offset];
            return true;
        }

        private static bool TryReadUInt16(byte[] image, int offset, out ushort value)
        {
            value = 0;
            if (!HasRange(image, offset, 2))
                return false;
            value = BitConverter.ToUInt16(image, offset);
            return true;
        }

        private static bool TryReadUInt32(byte[] image, int offset, out uint value)
        {
            value = 0;
            if (!HasRange(image, offset, 4))
                return false;
            value = BitConverter.ToUInt32(image, offset);
            return true;
        }

        private static bool TryReadUInt64(byte[] image, int offset, out ulong value)
        {
            value = 0;
            if (!HasRange(image, offset, 8))
                return false;
            value = BitConverter.ToUInt64(image, offset);
            return true;
        }

        private static bool TryToOffset(uint value, int length, out int offset)
        {
            offset = -1;
            if (value > int.MaxValue || value >= length)
                return false;
            offset = (int)value;
            return true;
        }

        private static bool HasRange(byte[] image, int offset, int length)
            => image != null && offset >= 0 && length >= 0 && offset <= image.Length - length;

        private static bool HasVerifiedNativeClosure(
            string candidateDirectory,
            JObject manifest,
            JObject inventory,
            BaseRuntime baseRuntime)
        {
            var nativeLibraries = manifest["nativeLibraries"] as JArray;
            var inventoryEntries = inventory["entries"] as JArray;
            var rmws = manifest["supportedRmwImplementations"] as JArray;
            var closures = manifest["rmwClosures"] as JObject;
            if (nativeLibraries == null || nativeLibraries.Count == 0
                || inventoryEntries == null || rmws == null || rmws.Count == 0 || closures == null)
            {
                return false;
            }

            var nativePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in nativeLibraries.OfType<JObject>())
            {
                var path = Text(entry["path"]);
                var fullPath = Path.Combine(candidateDirectory, path.Replace('/', Path.DirectorySeparatorChar));
                if (!IsSafePackageRelativePath(path)
                    || !IsChildPath(fullPath, Path.Combine(candidateDirectory, NativePluginRelativeDirectory))
                    || !File.Exists(fullPath)
                    || !StringEquals(Text(entry["sha256"]), FileSha256(fullPath))
                    || !nativePaths.Add(path))
                {
                    return false;
                }
            }

            var inventoryPaths = new HashSet<string>(inventoryEntries.OfType<JObject>().Select(entry => Text(entry["path"])), StringComparer.Ordinal);
            if (!nativePaths.IsSubsetOf(inventoryPaths))
                return false;

            foreach (var rmw in rmws.Select(token => Text(token)))
            {
                var closure = closures[rmw] as JObject;
                var baseLibraries = closure?["baseRuntimeLibraries"] as JArray;
                var addOnLibraries = closure?["addOnLibraries"] as JArray;
                if (string.IsNullOrWhiteSpace(rmw) || closure == null || baseLibraries == null || addOnLibraries == null
                    || !new HashSet<string>(addOnLibraries.Select(Text), StringComparer.Ordinal).SetEquals(nativePaths))
                {
                    return false;
                }

                foreach (var library in baseLibraries.Select(Text))
                {
                    if (string.IsNullOrWhiteSpace(library)
                        || library.IndexOfAny(new[] { '/', '\\' }) >= 0
                        || !File.Exists(Path.Combine(baseRuntime.Directory, NativePluginRelativeDirectory, library)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void RemoveOwnedDependencies(JObject dependencies)
        {
            foreach (var property in dependencies.Properties()
                         .Where(property => property.Name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal)
                                            || property.Name.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal))
                         .ToArray())
            {
                property.Remove();
            }
        }

        private static void AddDependency(JObject dependencies, string packageId, string reference)
        {
            var anchor = dependencies.Property(OptionalFacadePackageId)
                         ?? dependencies.Property("dev.unity2foxglove.sdk")
                         ?? dependencies.Properties().FirstOrDefault();
            if (anchor == null)
                throw new InvalidOperationException("manifest has no dependency anchor");
            anchor.AddAfterSelf(new JProperty(packageId, reference));
        }

        private static string RepositoryPackagesDirectory(string projectDirectory)
        {
            var project = new DirectoryInfo(projectDirectory);
            return Path.Combine(project.Parent?.FullName ?? string.Empty, "Packages");
        }

        private static string BuildPackageReference(string projectDirectory, string packagesDirectory, string packageId)
        {
            var from = new Uri(AppendDirectorySeparator(Path.GetFullPath(Path.Combine(projectDirectory, "Packages"))));
            var target = new Uri(Path.GetFullPath(Path.Combine(packagesDirectory, packageId)));
            return "file:" + Uri.UnescapeDataString(from.MakeRelativeUri(target).ToString()).Replace('\\', '/');
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

        private static JObject ParseManifestJson(string text)
        {
            var manifest = JObject.Parse(text);
            if (!(manifest["dependencies"] is JObject))
                throw new InvalidDataException("Unity package manifest has no dependencies object.");
            return manifest;
        }

        private static void WriteAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, content);
            try
            {
                File.Replace(temporary, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporary, path, true);
                File.Delete(temporary);
            }
            catch (IOException) when (!File.Exists(path))
            {
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string SerializeManifest(JObject manifest, string lineEnding)
            => manifest.ToString(Formatting.Indented)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", lineEnding)
                .TrimEnd() + lineEnding;

        private static string DetectLineEnding(string text)
            => text?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";

        private static string AppendDirectorySeparator(string value)
            => value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? value
                : value + Path.DirectorySeparatorChar;

        private static bool IsChildPath(string candidate, string root)
        {
            try
            {
                var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSafePackageRelativePath(string value)
            => !string.IsNullOrWhiteSpace(value)
               && value.IndexOf('\\') < 0
               && !Path.IsPathRooted(value)
               && !value.Split('/').Any(part => part == "." || part == "..");

        private static string Text(JToken token)
            => token?.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : string.Empty;

        private static bool IsSha256(string value)
            => value?.Length == 64 && value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));

        internal static bool IsRevisionedRosPackageName(string value, int interfaceRevision)
        {
            if (string.IsNullOrWhiteSpace(value) || interfaceRevision <= 0)
                return false;

            var versionMarker = value.LastIndexOf("_v", StringComparison.Ordinal);
            if (versionMarker <= 0 || versionMarker + 2 >= value.Length || !IsRosPackageStem(value, versionMarker))
                return false;

            var parsedRevision = 0;
            for (var index = versionMarker + 2; index < value.Length; index++)
            {
                var character = value[index];
                if (character < '0' || character > '9'
                    || parsedRevision > (Int32.MaxValue - (character - '0')) / 10)
                {
                    return false;
                }

                parsedRevision = parsedRevision * 10 + (character - '0');
            }

            return parsedRevision == interfaceRevision;
        }

        private static bool IsRosPackageStem(string value, int length)
        {
            if (value[0] < 'a' || value[0] > 'z')
                return false;

            for (var index = 1; index < length; index++)
            {
                var character = value[index];
                if (!((character >= 'a' && character <= 'z')
                      || (character >= '0' && character <= '9')
                      || character == '_'))
                {
                    return false;
                }
            }

            return value[length - 1] != '_';
        }

        private static bool IsGuid(string value)
            => Guid.TryParseExact(value, "D", out _);

        private static bool IsVersion(string value)
            => Version.TryParse(value, out var version) && version.Revision >= 0;

        private static bool StringEquals(string left, string right)
            => string.Equals(left, right, StringComparison.Ordinal);

        private static string FileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string NormalizedJsonSha256(JToken token)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(CanonicalJson(token));
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string CanonicalJson(JToken token)
        {
            if (token is JObject obj)
            {
                return "{" + string.Join(",", obj.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => CanonicalJson(new JValue(property.Name)) + ":" + CanonicalJson(property.Value))) + "}";
            }

            if (token is JArray array)
                return "[" + string.Join(",", array.Select(CanonicalJson)) + "]";

            return token.ToString(Formatting.None);
        }

        private static Ros2ForUnityCustomTypesupportSelectionResult Failure(
            Ros2ForUnityCustomTypesupportSelectionCode code)
            => new Ros2ForUnityCustomTypesupportSelectionResult(code, string.Empty, string.Empty, string.Empty, string.Empty);

        private sealed class BaseRuntime
        {
            public BaseRuntime(string packageId, string packageVersion, string distro, string manifestDigest, string directory)
            {
                PackageId = packageId;
                PackageVersion = packageVersion;
                Distro = distro;
                ManifestDigest = manifestDigest;
                Directory = directory;
            }

            public string PackageId { get; }
            public string PackageVersion { get; }
            public string Distro { get; }
            public string ManifestDigest { get; }
            public string Directory { get; }
        }

        private sealed class Candidate
        {
            public Candidate(string packageId, string interfaceDigest, string nativePluginDirectory)
            {
                PackageId = packageId;
                InterfaceDigest = interfaceDigest;
                NativePluginDirectory = nativePluginDirectory;
            }

            public string PackageId { get; }
            public string InterfaceDigest { get; }
            public string NativePluginDirectory { get; }
        }

        private sealed class StaticSourceLock
        {
            public StaticSourceLock(string rosPackageName, int interfaceRevision, string interfaceDigest)
            {
                RosPackageName = rosPackageName;
                InterfaceRevision = interfaceRevision;
                InterfaceDigest = interfaceDigest;
            }

            public string RosPackageName { get; }
            public int InterfaceRevision { get; }
            public string InterfaceDigest { get; }
        }
    }
}
