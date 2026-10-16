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
namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityRuntimeSelection
    {
        private static string ComputeZenohPayloadDiagnostic(string packageDirectory)
        {
            var pluginsRoot = Path.Combine(packageDirectory, "Runtime", "Ros2ForUnity", "Plugins");
            var streamingAssetsShare = Path.Combine(
                packageDirectory,
                "Runtime",
                "Ros2ForUnity",
                "StreamingAssets",
                "Ros2ForUnity",
                "share",
                "rmw_zenoh_cpp",
                "config");
            if (!Directory.Exists(pluginsRoot))
                return "Zenoh communication mode is unavailable because this runtime package has no native plugin directory.";

            var pluginRoots = Directory
                .EnumerateDirectories(pluginsRoot, "*", SearchOption.AllDirectories)
                .ToArray();
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var pluginRoot in pluginRoots)
            {
                var pluginShare = Path.Combine(pluginRoot, "share", "rmw_zenoh_cpp", "config");
                var required = new[]
                {
                    (Present: HasNativeLibrary(pluginRoot, "rmw_zenoh_cpp"), Name: "rmw_zenoh_cpp native library"),
                    (Present: HasNativeLibrary(pluginRoot, "zenohc"), Name: "zenohc native library"),
                    (Present: File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5")), Name: "plugin Zenoh session config"),
                    (Present: File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5")), Name: "plugin Zenoh router config"),
                    (Present: File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5")), Name: "StreamingAssets Zenoh session config"),
                    (Present: File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5")), Name: "StreamingAssets Zenoh router config"),
                };
                if (required.All(item => item.Present))
                    return string.Empty;

                foreach (var item in required.Where(item => !item.Present))
                    missing.Add(item.Name);
            }

            if (missing.Count == 0)
                missing.Add("complete Zenoh native payload");

            return "Zenoh communication mode is unavailable for this runtime package. Missing: "
                   + string.Join(", ", missing)
                   + ". Rebuild or re-import the runtime ZIP with rmw_zenoh_cpp and config assets.";
        }

        private static bool HasNativeLibrary(string directory, string libraryName)
        {
            return File.Exists(Path.Combine(directory, libraryName + ".dll"))
                   || File.Exists(Path.Combine(directory, "lib" + libraryName + ".so"))
                   || File.Exists(Path.Combine(directory, "lib" + libraryName + ".dylib"));
        }

        private static bool IsEmbeddedPackage(string projectDirectory, string packageDirectory)
        {
            var embeddedRoot = Path.GetFullPath(Path.Combine(projectDirectory, "Packages"));
            var candidate = Path.GetFullPath(packageDirectory);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return candidate.StartsWith(embeddedRoot + Path.DirectorySeparatorChar, comparison);
        }

        private static bool ContainsPackageName(string json, string packageName)
        {
            try
            {
                return string.Equals((string)JObject.Parse(json ?? string.Empty)["name"], packageName, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ToDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(
                " ",
                value.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static string ManifestPath(string projectDirectory)
            => Path.Combine(projectDirectory, "Packages", "manifest.json");

        private static JObject ReadManifestJson(string manifest, string manifestPath)
        {
            try
            {
                return JObject.Parse(manifest ?? string.Empty);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Unity package manifest is not valid JSON: " + manifestPath, ex);
            }
        }

        private static JObject ReadManifestDependencies(string manifest, string manifestPath)
        {
            return ReadManifestJson(manifest, manifestPath)["dependencies"] as JObject;
        }

        private static void ValidateManifestJson(string manifest, string manifestPath)
        {
            ReadManifestJson(manifest, manifestPath);
        }

        private static string RemoveRuntimePackageDependencies(string manifest)
        {
            var root = ReadManifestJson(manifest, "Unity package manifest");
            var dependencies = root["dependencies"] as JObject
                ?? throw new InvalidOperationException("Unity package manifest is missing a dependencies object.");
            foreach (var property in dependencies.Properties()
                         .Where(property => property.Name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                         .ToArray())
            {
                property.Remove();
            }

            return SerializeManifest(root, DetectLineEnding(manifest));
        }

        private static string AddRuntimePackageDependency(string manifest, string packageName, string projectDirectory)
        {
            var lineEnding = DetectLineEnding(manifest);
            var root = ReadManifestJson(manifest, "Unity package manifest");
            var dependencies = root["dependencies"] as JObject
                ?? throw new InvalidOperationException("Unity package manifest is missing a dependencies object.");
            var anchor = dependencies.Property("dev.unity2foxglove.ros2forunity")
                         ?? dependencies.Property("dev.unity2foxglove.sdk")
                         ?? dependencies.Properties().FirstOrDefault();
            if (anchor == null)
                throw new InvalidOperationException("Unity package manifest dependencies object is empty; cannot anchor the ROS2 For Unity runtime package.");

            anchor.AddAfterSelf(new JProperty(packageName, BuildRuntimePackageReference(projectDirectory, packageName)));
            return SerializeManifest(root, lineEnding);
        }

        private static string SerializeManifest(JObject root, string lineEnding)
        {
            var serialized = root.ToString(Newtonsoft.Json.Formatting.Indented)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", lineEnding);
            return serialized.TrimEnd() + lineEnding;
        }

        private static string DetectLineEnding(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Environment.NewLine;

            var crlfIndex = text.IndexOf("\r\n", StringComparison.Ordinal);
            var lfIndex = text.IndexOf('\n');
            var crIndex = text.IndexOf('\r');
            if (crlfIndex >= 0 && (lfIndex < 0 || crlfIndex <= lfIndex))
                return "\r\n";
            if (lfIndex >= 0)
                return "\n";
            if (crIndex >= 0)
                return "\r";
            return Environment.NewLine;
        }

        private static string BuildRuntimePackageReference(string projectDirectory, string packageName)
        {
            var projectPackagesDirectory = Path.Combine(projectDirectory, "Packages");
            var runtimePackageDirectory = Path.Combine(RepositoryPackagesDirectory(projectDirectory), packageName);
            var relativePath = GetRelativePath(projectPackagesDirectory, runtimePackageDirectory)
                .Replace('\\', '/')
                .TrimStart('/');
            return "file:" + relativePath;
        }

        private static string GetRelativePath(string fromDirectory, string toPath)
        {
            var fromFull = TrimDirectorySeparators(Path.GetFullPath(fromDirectory));
            var toFull = TrimDirectorySeparators(Path.GetFullPath(toPath));
            var fromRoot = Path.GetPathRoot(fromFull) ?? string.Empty;
            var toRoot = Path.GetPathRoot(toFull) ?? string.Empty;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(fromRoot, toRoot, comparison))
                return toFull;

            var fromParts = SplitPathParts(fromFull.Substring(fromRoot.Length));
            var toParts = SplitPathParts(toFull.Substring(toRoot.Length));
            var common = 0;
            while (common < fromParts.Length
                   && common < toParts.Length
                   && string.Equals(fromParts[common], toParts[common], comparison))
            {
                common++;
            }

            var parts = new List<string>();
            for (var i = common; i < fromParts.Length; i++)
                parts.Add("..");
            for (var i = common; i < toParts.Length; i++)
                parts.Add(toParts[i]);

            return parts.Count == 0 ? "." : string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Path.DirectorySeparatorChar.ToString();

            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                   || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string TrimDirectorySeparators(string path)
        {
            var root = Path.GetPathRoot(path) ?? string.Empty;
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length < root.Length ? root : trimmed;
        }

        private static string[] SplitPathParts(string path)
            => path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        private static string GetCommunicationModeSettingsKey(Ros2ForUnityRuntimeDescriptor runtime)
            => runtime == null || string.IsNullOrWhiteSpace(runtime.PackageName)
                ? CommunicationModeEditorUserSettingsKey
                : CommunicationModeEditorUserSettingsKey + "." + runtime.PackageName;

        private static void WriteManifestAtomically(string manifestPath, string manifest)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            var tempPath = Path.Combine(directory ?? string.Empty, Path.GetFileName(manifestPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(tempPath, manifest);
            try
            {
                File.Replace(tempPath, manifestPath, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(tempPath, manifestPath, overwrite: true);
                File.Delete(tempPath);
            }
            catch (IOException) when (!File.Exists(manifestPath))
            {
                File.Move(tempPath, manifestPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }
    }
}
#endif
