// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Observe the runtime identity staged into a Player build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity
{
    internal static class Ros2ForUnityNativeRuntimeIdentity
    {
        internal static string ObservedRuntimeId { get; private set; }
        internal static string ObservedPackageName { get; private set; }
        internal static string ObservedManifestSha256 { get; private set; }
        internal static string ObservedRmwImplementation { get; private set; }

        internal static bool TryObserve()
        {
            ObservedRuntimeId = null;
            ObservedPackageName = null;
            ObservedManifestSha256 = null;
            ObservedRmwImplementation = null;
            var manifestPath = Path.Combine(
                Application.dataPath,
                "StreamingAssets",
                "Ros2ForUnity",
                "runtime-manifest.json");
            if (!File.Exists(manifestPath))
                return true;

            try
            {
                var manifestBytes = File.ReadAllBytes(manifestPath);
                var json = Encoding.UTF8.GetString(manifestBytes);
                var runtimeId = ReadStringField(json, "runtimeId");
                var packageName = ReadStringField(json, "packageName");
                var rosDistro = ReadStringField(json, "rosDistro");
                var platform = ReadStringField(json, "platform");
                var runtimeRoot = ReadStringField(json, "runtimeRoot");
                var rmwImplementation = ReadStringField(json, "rmwImplementation");
                var criticalFiles = ReadStringArray(json, "criticalRuntimeFiles");
                if (string.IsNullOrWhiteSpace(runtimeId)
                    || string.IsNullOrWhiteSpace(packageName)
                    || string.IsNullOrWhiteSpace(rosDistro)
                    || string.IsNullOrWhiteSpace(platform)
                    || string.IsNullOrWhiteSpace(runtimeRoot)
                    || string.IsNullOrWhiteSpace(rmwImplementation)
                    || criticalFiles.Count == 0)
                    return false;
                if (!string.Equals(
                        runtimeId,
                        "r2fu-" + rosDistro + "-" + platform,
                        StringComparison.OrdinalIgnoreCase)
                    || !packageName.EndsWith(
                        "." + rosDistro + "." + platform,
                        StringComparison.OrdinalIgnoreCase)
                    || !HasCriticalRuntimeFiles(runtimeRoot, criticalFiles))
                    return false;

                var markerPath = Path.Combine(
                    Application.dataPath,
                    "StreamingAssets",
                    "Ros2ForUnity",
                    ".unity2foxglove-staged");
                if (File.Exists(markerPath))
                {
                    var markerParts = File.ReadAllText(markerPath).Trim().Split('|');
                    if (markerParts.Length >= 3
                        && (!string.Equals(markerParts[0], packageName, StringComparison.Ordinal)
                            || !string.Equals(markerParts[1], runtimeId, StringComparison.Ordinal)
                            || !string.Equals(
                                markerParts[2],
                                Hash(manifestBytes),
                                StringComparison.OrdinalIgnoreCase)))
                        return false;
                }

                ObservedRuntimeId = runtimeId;
                ObservedPackageName = packageName;
                ObservedManifestSha256 = Hash(manifestBytes);
                ObservedRmwImplementation = rmwImplementation;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadStringField(string json, string fieldName)
        {
            var marker = "\"" + fieldName + "\"";
            var markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return null;

            var colonIndex = json.IndexOf(':', markerIndex + marker.Length);
            if (colonIndex < 0)
                return null;

            var valueStart = json.IndexOf('\"', colonIndex + 1);
            if (valueStart < 0)
                return null;

            var valueEnd = json.IndexOf('\"', valueStart + 1);
            return valueEnd > valueStart
                ? json.Substring(valueStart + 1, valueEnd - valueStart - 1)
                : null;
        }

        private static List<string> ReadStringArray(string json, string fieldName)
        {
            var values = new List<string>();
            var marker = "\"" + fieldName + "\"";
            var markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return values;
            var start = json.IndexOf('[', markerIndex + marker.Length);
            var end = start < 0 ? -1 : json.IndexOf(']', start + 1);
            if (end < 0)
                return values;
            var cursor = start + 1;
            while (cursor < end)
            {
                var valueStart = json.IndexOf('"', cursor);
                if (valueStart < 0 || valueStart >= end)
                    break;
                var valueEnd = json.IndexOf('"', valueStart + 1);
                if (valueEnd < 0 || valueEnd > end)
                    break;
                values.Add(json.Substring(valueStart + 1, valueEnd - valueStart - 1));
                cursor = valueEnd + 1;
            }

            return values;
        }

        private static bool HasCriticalRuntimeFiles(
            string runtimeRoot,
            IReadOnlyList<string> criticalFiles)
        {
            var roots = new[]
            {
                Path.Combine(
                    Application.dataPath,
                    "StreamingAssets",
                    "Ros2ForUnity",
                    runtimeRoot.Replace('/', Path.DirectorySeparatorChar)),
                Application.dataPath,
            };
            for (var index = 0; index < criticalFiles.Count; index++)
            {
                var relative = criticalFiles[index].Replace('/', Path.DirectorySeparatorChar);
                var fileName = Path.GetFileName(relative);
                var found = false;
                for (var rootIndex = 0; rootIndex < roots.Length && !found; rootIndex++)
                {
                    var root = roots[rootIndex];
                    if (!Directory.Exists(root))
                        continue;
                    found = File.Exists(Path.Combine(root, relative));
                    if (!found)
                    {
                        foreach (var candidate in Directory.EnumerateFiles(
                                     root,
                                     fileName,
                                     SearchOption.AllDirectories))
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
        }
    }
}
