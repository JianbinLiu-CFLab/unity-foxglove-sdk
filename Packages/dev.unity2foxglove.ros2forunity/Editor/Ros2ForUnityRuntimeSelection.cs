// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Project-level active runtime selection for optional R2FU runtime packages.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal sealed class Ros2ForUnityRuntimeDescriptor
    {
        public Ros2ForUnityRuntimeDescriptor(
            string displayName,
            string packageName,
            string runtimeId,
            string rosDistro,
            string platform,
            bool supportsZenoh)
        {
            DisplayName = displayName ?? string.Empty;
            PackageName = packageName ?? string.Empty;
            RuntimeId = runtimeId ?? string.Empty;
            RosDistro = rosDistro ?? string.Empty;
            Platform = platform ?? string.Empty;
            SupportsZenoh = supportsZenoh;
        }

        public string DisplayName { get; }
        public string PackageName { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string Platform { get; }
        public bool SupportsZenoh { get; }
    }

    internal sealed class Ros2ForUnityRuntimeSelectionStatus
    {
        public Ros2ForUnityRuntimeSelectionStatus(
            IReadOnlyList<Ros2ForUnityRuntimeDescriptor> installedRuntimes,
            string activeRuntimePackage,
            Ros2ForUnityRuntimeDescriptor selectedRuntime,
            string diagnostic)
        {
            InstalledRuntimes = installedRuntimes ?? Array.Empty<Ros2ForUnityRuntimeDescriptor>();
            ActiveRuntimePackage = activeRuntimePackage ?? string.Empty;
            SelectedRuntime = selectedRuntime;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public IReadOnlyList<Ros2ForUnityRuntimeDescriptor> InstalledRuntimes { get; }
        public string ActiveRuntimePackage { get; }
        public Ros2ForUnityRuntimeDescriptor SelectedRuntime { get; }
        public string Diagnostic { get; }
        public bool HasSelection => SelectedRuntime != null;
    }

    internal static class Ros2ForUnityRuntimeSelection
    {
        public const string BaseCompileSymbol = "UNITY2FOXGLOVE_ROS2_FOR_UNITY";
        public const string RuntimePackagePrefix = "dev.unity2foxglove.ros2forunity.runtime.";
        public const string FastDdsCommunicationMode = "fastdds";
        public const string ZenohCommunicationMode = "zenoh";
        public const string FastDdsRmwImplementation = "rmw_fastrtps_cpp";
        public const string ZenohRmwImplementation = "rmw_zenoh_cpp";
        private const string SessionRuntimeKey = "Unity2Foxglove.R2FU.SessionRuntime";
        private const string SessionCommunicationModeKey = "Unity2Foxglove.R2FU.SessionCommunicationMode";
        private const string CommunicationModeEditorUserSettingsKey =
            "Unity2Foxglove.R2FU.CommunicationMode";

        public static string ProjectDirectoryFromApplication()
        {
            var assetsDirectory = new DirectoryInfo(Application.dataPath);
            return assetsDirectory.Parent?.FullName ?? string.Empty;
        }

        public static Ros2ForUnityRuntimeSelectionStatus GetStatus()
            => GetStatus(ProjectDirectoryFromApplication());

        public static Ros2ForUnityRuntimeSelectionStatus GetStatus(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    Array.Empty<Ros2ForUnityRuntimeDescriptor>(),
                    string.Empty,
                    null,
                    "Could not resolve the Unity project directory.");
            }

            var candidates = DiscoverCandidateRuntimes(projectDirectory).ToArray();
            var activePackages = ReadManifestRuntimePackages(projectDirectory).ToArray();
            var activePackage = activePackages.Length == 1 ? activePackages[0] : string.Empty;
            var explicitSelection = candidates.FirstOrDefault(
                runtime => string.Equals(runtime.PackageName, activePackage, StringComparison.Ordinal));

            if (explicitSelection != null)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    activePackage,
                    explicitSelection,
                    string.Empty);
            }

            if (activePackages.Length > 1)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    string.Join(", ", activePackages),
                    null,
                    "Multiple ROS2 For Unity runtime packages are resolved in the Unity manifest. Select one active runtime.");
            }

            if (!string.IsNullOrWhiteSpace(activePackage))
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    activePackage,
                    null,
                    "The active ROS2 For Unity runtime package is not available as a repository candidate: " + activePackage);
            }

            if (candidates.Length > 0)
            {
                return new Ros2ForUnityRuntimeSelectionStatus(
                    candidates,
                    string.Empty,
                    null,
                    "No ROS2 For Unity runtime package is resolved in the Unity manifest. Select a candidate to make it active.");
            }

            return new Ros2ForUnityRuntimeSelectionStatus(
                candidates,
                string.Empty,
                null,
                "No ROS2 For Unity runtime candidate package was found under the repository Packages directory.");
        }

        public static void SwitchActiveRuntimePackage(string projectDirectory, string packageName)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot switch ROS2 For Unity runtime while Play Mode is active or changing.");

            var candidate = DiscoverCandidateRuntimes(projectDirectory)
                .FirstOrDefault(runtime => string.Equals(runtime.PackageName, packageName, StringComparison.Ordinal));
            if (candidate == null)
                throw new InvalidOperationException("Runtime package is not a repository candidate: " + packageName);

            var manifestPath = ManifestPath(projectDirectory);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("Unity package manifest was not found.", manifestPath);

            var manifest = File.ReadAllText(manifestPath);
            ValidateManifestJson(manifest, manifestPath);
            manifest = RemoveRuntimePackageDependencies(manifest);
            manifest = AddRuntimePackageDependency(manifest, candidate.PackageName, projectDirectory);
            ValidateManifestJson(manifest, manifestPath);
            WriteManifestAtomically(manifestPath, manifest);
            ApplyCommunicationModeEnvironment(projectDirectory);
            Client.Resolve();
        }

        public static string GetSessionRuntimePackage()
            => SessionState.GetString(SessionRuntimeKey, string.Empty);

        public static string GetSessionCommunicationMode()
            => SessionState.GetString(SessionCommunicationModeKey, string.Empty);

        public static IReadOnlyList<string> GetCommunicationModeIds(Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime != null && runtime.SupportsZenoh)
                return new[] { FastDdsCommunicationMode, ZenohCommunicationMode };
            return new[] { FastDdsCommunicationMode };
        }

        public static string GetCommunicationModeDisplayName(string mode)
        {
            return string.Equals(mode, ZenohCommunicationMode, StringComparison.Ordinal)
                ? "Zenoh (rmw_zenoh_cpp)"
                : "FastDDS (default)";
        }

        public static string GetRmwImplementationForCommunicationMode(string mode)
        {
            return string.Equals(mode, ZenohCommunicationMode, StringComparison.Ordinal)
                ? ZenohRmwImplementation
                : FastDdsRmwImplementation;
        }

        public static string GetCommunicationModeForRuntime(Ros2ForUnityRuntimeDescriptor runtime)
        {
            if (runtime == null || !runtime.SupportsZenoh)
                return FastDdsCommunicationMode;

            var saved = EditorUserSettings.GetConfigValue(CommunicationModeEditorUserSettingsKey);
            return string.Equals(saved, ZenohCommunicationMode, StringComparison.Ordinal)
                ? ZenohCommunicationMode
                : FastDdsCommunicationMode;
        }

        public static void SetCommunicationMode(
            string projectDirectory,
            Ros2ForUnityRuntimeDescriptor runtime,
            string mode)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot switch ROS2 For Unity communication mode while Play Mode is active or changing.");

            if (runtime == null)
                throw new InvalidOperationException("Select an active ROS2 For Unity runtime before changing communication mode.");

            if (!GetCommunicationModeIds(runtime).Contains(mode, StringComparer.Ordinal))
                throw new InvalidOperationException("Communication mode is not supported by the active runtime: " + mode);

            EditorUserSettings.SetConfigValue(CommunicationModeEditorUserSettingsKey, mode);
            ApplyCommunicationModeEnvironment(projectDirectory);
        }

        public static void ApplyCommunicationModeEnvironment(string projectDirectory)
        {
            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
                return;

            var mode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            Environment.SetEnvironmentVariable("RMW_IMPLEMENTATION", GetRmwImplementationForCommunicationMode(mode));
        }

        public static void BindActiveRuntimeForPlayMode(string projectDirectory)
        {
            var sessionRuntime = GetSessionRuntimePackage();
            var status = GetStatus(projectDirectory);
            if (status.SelectedRuntime == null)
                return;

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            Environment.SetEnvironmentVariable(
                "RMW_IMPLEMENTATION",
                GetRmwImplementationForCommunicationMode(communicationMode));

            if (string.IsNullOrWhiteSpace(sessionRuntime))
                SessionState.SetString(SessionRuntimeKey, status.SelectedRuntime.PackageName);

            if (string.IsNullOrWhiteSpace(GetSessionCommunicationMode()))
                SessionState.SetString(SessionCommunicationModeKey, communicationMode);
        }

        public static string GetRuntimePackageRequiringEditorRestart(string projectDirectory)
            => GetRuntimePackageRequiringEditorRestart(GetStatus(projectDirectory));

        public static string GetRuntimePackageRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            var sessionRuntime = GetSessionRuntimePackage();
            if (string.IsNullOrWhiteSpace(sessionRuntime))
                return string.Empty;

            if (status == null || status.SelectedRuntime == null)
                return string.Empty;

            return string.Equals(status.SelectedRuntime.PackageName, sessionRuntime, StringComparison.Ordinal)
                ? string.Empty
                : status.SelectedRuntime.PackageName;
        }

        public static bool IsEditorRestartRequired(string projectDirectory)
            => !string.IsNullOrWhiteSpace(GetRuntimePackageRequiringEditorRestart(projectDirectory))
               || !string.IsNullOrWhiteSpace(GetCommunicationModeRequiringEditorRestart(projectDirectory));

        public static string GetCommunicationModeRequiringEditorRestart(string projectDirectory)
            => GetCommunicationModeRequiringEditorRestart(GetStatus(projectDirectory));

        public static string GetCommunicationModeRequiringEditorRestart(Ros2ForUnityRuntimeSelectionStatus status)
        {
            var sessionMode = GetSessionCommunicationMode();
            if (string.IsNullOrWhiteSpace(sessionMode))
                return string.Empty;

            if (status == null || status.SelectedRuntime == null)
                return string.Empty;

            var communicationMode = GetCommunicationModeForRuntime(status.SelectedRuntime);
            return string.Equals(communicationMode, sessionMode, StringComparison.Ordinal)
                ? string.Empty
                : communicationMode;
        }

        public static void RestartEditor(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory))
                throw new InvalidOperationException("Could not resolve the Unity project directory.");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Cannot restart Unity while Play Mode is active or changing.");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorApplication.OpenProject(projectDirectory);
        }

        public static IReadOnlyList<Ros2ForUnityRuntimeDescriptor> DiscoverCandidateRuntimes(string projectDirectory)
        {
            var packagesDirectory = RepositoryPackagesDirectory(projectDirectory);
            if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
                return Array.Empty<Ros2ForUnityRuntimeDescriptor>();

            return Directory
                .GetDirectories(packagesDirectory, RuntimePackagePrefix + "*", SearchOption.TopDirectoryOnly)
                .Where(path => !IsEmbeddedPackage(projectDirectory, path))
                .Select(TryCreateDescriptor)
                .Where(descriptor => descriptor != null)
                .OrderBy(descriptor => descriptor.DisplayName, StringComparer.Ordinal)
                .ToArray();
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
            if (!File.Exists(manifestPath))
                return Array.Empty<string>();

            var dependencies = ReadManifestDependencies(File.ReadAllText(manifestPath), manifestPath);
            if (dependencies == null)
                return Array.Empty<string>();

            return dependencies
                .Properties()
                .Select(property => property.Name)
                .Where(name => name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
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

            var rosDistro = parts[0];
            var platform = string.Join(".", parts.Skip(1));
            var runtimeId = "r2fu-" + rosDistro + "-" + platform.Replace('.', '-');
            var displayName = ToDisplayName(rosDistro) + " " + ToDisplayName(platform.Replace('.', ' '));

            return new Ros2ForUnityRuntimeDescriptor(
                displayName,
                packageName,
                runtimeId,
                rosDistro,
                platform,
                IsZenohCapableDistro(rosDistro) && HasZenohPayload(packageDirectory));
        }

        private static bool IsZenohCapableDistro(string rosDistro)
            => string.Equals(rosDistro, "lyrical", StringComparison.Ordinal);

        private static bool HasZenohPayload(string packageDirectory)
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
                return false;

            return Directory
                .EnumerateDirectories(pluginsRoot, "*", SearchOption.AllDirectories)
                .Any(pluginRoot =>
                {
                    var pluginShare = Path.Combine(pluginRoot, "share", "rmw_zenoh_cpp", "config");
                    return HasNativeLibrary(pluginRoot, "rmw_zenoh_cpp")
                           && HasNativeLibrary(pluginRoot, "zenohc")
                           && File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"))
                           && File.Exists(Path.Combine(pluginShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"))
                           && File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"))
                           && File.Exists(Path.Combine(streamingAssetsShare, "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"));
                });
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
            var lineEnding = DetectLineEnding(manifest);
            var lines = Regex.Split(manifest ?? string.Empty, "\r\n|\n|\r").ToList();
            var removed = false;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (!Regex.IsMatch(
                    lines[i],
                    "^\\s*\"" + Regex.Escape(RuntimePackagePrefix) + "[^\"]+\"\\s*:\\s*\"[^\"]+\",?\\s*$",
                    RegexOptions.CultureInvariant))
                {
                    continue;
                }

                lines.RemoveAt(i);
                removed = true;
            }

            if (removed)
                RemoveTrailingDependencyComma(lines);

            return string.Join(lineEnding, lines).TrimEnd() + lineEnding;
        }

        private static string AddRuntimePackageDependency(string manifest, string packageName, string projectDirectory)
        {
            var lineEnding = DetectLineEnding(manifest);
            var lines = Regex.Split(manifest ?? string.Empty, "\r\n|\n|\r").ToList();
            var dependencyLine = "    \"" + packageName + "\": \"" + BuildRuntimePackageReference(projectDirectory, packageName) + "\",";
            var insertIndex = lines.FindIndex(line => line.Contains("\"dev.unity2foxglove.ros2forunity\"", StringComparison.Ordinal));
            if (insertIndex < 0)
                insertIndex = lines.FindIndex(line => line.Contains("\"dev.unity2foxglove.sdk\"", StringComparison.Ordinal));
            if (insertIndex < 0)
                insertIndex = lines.FindIndex(line => line.Contains("\"dependencies\"", StringComparison.Ordinal));

            if (insertIndex < 0)
            {
                lines.Add(dependencyLine);
            }
            else
            {
                lines.Insert(insertIndex + 1, dependencyLine);
            }

            RemoveTrailingDependencyComma(lines);
            return string.Join(lineEnding, lines).TrimEnd() + lineEnding;
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
            var fromUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(fromDirectory)));
            var toUri = new Uri(Path.GetFullPath(toPath));
            return Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
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

        private static void RemoveTrailingDependencyComma(List<string> lines)
        {
            var inDependencies = false;
            var depth = 0;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!inDependencies && line.Contains("\"dependencies\"", StringComparison.Ordinal))
                    inDependencies = true;

                if (!inDependencies)
                    continue;

                for (var j = 0; j < line.Length; j++)
                {
                    var ch = line[j];
                    if (ch == '{')
                    {
                        depth++;
                    }
                    else if (ch == '}')
                    {
                        if (depth == 1)
                        {
                            RemoveTrailingCommaFromPreviousLine(lines, i);
                            return;
                        }

                        depth = Math.Max(0, depth - 1);
                    }
                }
            }
        }

        private static void RemoveTrailingCommaFromPreviousLine(List<string> lines, int beforeIndex)
        {
            for (var i = beforeIndex - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var trimmed = line.TrimEnd();
                if (trimmed.EndsWith(",", StringComparison.Ordinal))
                    lines[i] = trimmed.Substring(0, trimmed.Length - 1) + line.Substring(trimmed.Length);
                return;
            }
        }

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
#endif
