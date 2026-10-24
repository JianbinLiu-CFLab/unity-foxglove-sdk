// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Native closure and assembly identity verification..

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

            if (nativeLibraries.Any(token => token is not JObject))
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
                    || !FileExistsForVerification(fullPath)
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
                        || !FileExistsForVerification(Path.Combine(baseRuntime.Directory, NativePluginRelativeDirectory, library)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

    }
}
