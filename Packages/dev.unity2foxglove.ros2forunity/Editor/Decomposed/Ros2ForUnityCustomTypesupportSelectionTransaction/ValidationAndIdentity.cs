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
    internal static partial class Ros2ForUnityCustomTypesupportSelectionTransaction
    {
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

        private static bool TryReadCandidate(
            string directory,
            BaseRuntime baseRuntime,
            out Candidate candidate,
            out Ros2ForUnityCustomTypesupportCandidateValidationCode validationCode)
        {
            candidate = null;
            validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Unexpected;
            try
            {
                var packageId = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(packageId)
                    || !packageId.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal)
                    || !TryReadObject(Path.Combine(directory, "package.json"), out var package)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-manifest.json"), out var manifest)
                    || !TryReadObject(Path.Combine(directory, "RuntimeSupport", "typesupport-inventory.json"), out var inventory))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.PackageMetadata;
                    return false;
                }

                var dependencies = package["dependencies"] as JObject;
                if (!StringEquals(Text(package["name"]), packageId)
                    || package["unity2foxgloveFoxRunCustomTypesupportAddOn"]?.Value<bool>() != true
                    || dependencies == null
                    || Text(dependencies[OptionalFacadePackageId]).Length == 0
                    || !StringEquals(Text(dependencies[baseRuntime.PackageId]), baseRuntime.PackageVersion))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.PackageMetadata;
                    return false;
                }

                var source = manifest["source"] as JObject;
                var baseRuntimeToken = manifest["baseRuntime"] as JObject;
                if (source == null || baseRuntimeToken == null
                    || !TryReadStaticSourceLock(directory, out var staticSource))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.StaticSource;
                    return false;
                }

                if (!StringEquals(Text(source["upmPackageId"]), StaticInterfacePackageId)
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
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.SourceIdentity;
                    return false;
                }

                if (!HasMatchingRos2csIdentity(directory, manifest, baseRuntime))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.ManagedIdentity;
                    return false;
                }

                if (!HasVerifiedNativeClosure(directory, manifest, inventory, baseRuntime))
                {
                    validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.NativeClosure;
                    return false;
                }

                candidate = new Candidate(
                    packageId,
                    Text(source["interfaceDigest"]),
                    Path.Combine(directory, NativePluginRelativeDirectory));
                validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Ready;
                return true;
            }
            catch (Exception)
            {
                // Package contents are untrusted input to this selector.
                candidate = null;
                validationCode = Ros2ForUnityCustomTypesupportCandidateValidationCode.Unexpected;
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
                || !HasMatchingRosPackageRevision(rosPackageName, interfaceRevision)
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
                   && FileExistsForVerification(baseAssembly)
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
                var image = ReadAllBytesForVerification(path);
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

    }
}
