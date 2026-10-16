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
